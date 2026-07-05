using System.Text.RegularExpressions;
using wslc_desktop.Models;
using YamlDotNet.RepresentationModel;

namespace wslc_desktop.Services;

public sealed class ComposePlanService : IComposePlanService
{
    private static readonly Regex VariablePattern = new(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedServiceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "image",
        "command",
        "ports",
        "volumes",
        "environment",
        "depends_on"
    };

    private readonly IWslcImageService _imageService;
    private readonly IWslcContainerService _containerService;
    private readonly IOperationTracker _operationTracker;
    private readonly Dictionary<string, ComposeProjectSummary> _projects = new(StringComparer.OrdinalIgnoreCase);

    public ComposePlanService(
        IWslcImageService imageService,
        IWslcContainerService containerService,
        IOperationTracker operationTracker)
    {
        _imageService = imageService;
        _containerService = containerService;
        _operationTracker = operationTracker;
    }

    public Task<IReadOnlyList<ComposeProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ComposeProjectSummary>>(
            _projects.Values.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<IReadOnlyList<ComposeServicePlan>> PreviewAsync(string composePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(composePath))
        {
            throw new ArgumentException("Compose path is required.", nameof(composePath));
        }

        if (!File.Exists(composePath))
        {
            throw new FileNotFoundException("Compose file was not found.", composePath);
        }

        string composeDirectory = Path.GetDirectoryName(Path.GetFullPath(composePath)) ?? Environment.CurrentDirectory;
        var environment = await LoadDotEnvAsync(composeDirectory, cancellationToken);
        await using var stream = File.OpenRead(composePath);
        var yaml = new YamlStream();
        yaml.Load(new StreamReader(stream));

        var root = yaml.Documents.Count == 0
            ? throw new InvalidOperationException("Compose file is empty.")
            : yaml.Documents[0].RootNode as YamlMappingNode
                ?? throw new InvalidOperationException("Compose root must be a mapping.");

        string projectName = GetOptionalScalar(root, "name", environment)
            ?? new DirectoryInfo(composeDirectory).Name;

        var services = GetRequiredMapping(root, "services");
        var plans = new List<ComposeServicePlan>();

        foreach (var serviceEntry in services.Children)
        {
            string serviceName = GetKey(serviceEntry.Key);
            if (serviceEntry.Value is not YamlMappingNode service)
            {
                throw new InvalidOperationException($"Compose service '{serviceName}' must be a mapping.");
            }

            plans.Add(ParseService(projectName, serviceName, service, composeDirectory, environment));
        }

        return plans
            .OrderBy(plan => plan.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ContainerSummary>> CreateAndStartAsync(string composePath, CancellationToken cancellationToken = default)
    {
        var plans = await PreviewAsync(composePath, cancellationToken);
        var orderedPlans = OrderByDependencies(plans);
        var created = new List<ContainerSummary>();

        foreach (var plan in orderedPlans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PullImageAsync(plan.Image, cancellationToken);

            var container = await _containerService.CreateAsync(new ContainerCreateRequest(
                $"{plan.ProjectName}-{plan.ServiceName}",
                plan.Image,
                plan.Command,
                plan.Ports,
                plan.Mounts,
                plan.Environment,
                EnableGpu: false,
                AutoRemove: false), cancellationToken);

            await _containerService.StartAsync(container.Id, cancellationToken);
            created.Add(container);
        }

        if (plans.Count > 0)
        {
            string sourcePath = Path.GetFullPath(composePath);
            _projects[sourcePath] = new ComposeProjectSummary(
                plans[0].ProjectName,
                plans.Count,
                created.Count,
                sourcePath);
        }

        _operationTracker.Track(new OperationRecord(
            Guid.NewGuid().ToString("N"),
            "Compose create/start",
            OperationState.Succeeded,
            $"{created.Count} service container{(created.Count == 1 ? string.Empty : "s")} created.",
            DateTimeOffset.Now));

        return created;
    }

    private static ComposeServicePlan ParseService(
        string projectName,
        string serviceName,
        YamlMappingNode service,
        string composeDirectory,
        IReadOnlyDictionary<string, string> variables)
    {
        string image = GetOptionalScalar(service, "image", variables)
            ?? throw new InvalidOperationException($"Compose service '{serviceName}' must specify an image.");

        var unsupportedKeys = service.Children.Keys
            .Select(GetKey)
            .Where(key => !SupportedServiceKeys.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ComposeServicePlan(
            projectName,
            serviceName,
            image,
            ParseCommand(service, variables),
            ParsePorts(service, variables),
            ParseVolumes(service, composeDirectory, variables),
            ParseEnvironment(service, variables),
            ParseDependsOn(service),
            unsupportedKeys);
    }

    private static IReadOnlyList<string> ParseCommand(YamlMappingNode service, IReadOnlyDictionary<string, string> variables)
    {
        if (!TryGetValue(service, "command", out var command))
        {
            return [];
        }

        return command switch
        {
            YamlScalarNode scalar => ContainerCreateInputParser.ParseCommandLine(Interpolate(scalar.Value ?? string.Empty, variables)),
            YamlSequenceNode sequence => sequence.Children
                .Select(node => Interpolate(GetScalarValue(node), variables))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray(),
            _ => throw new InvalidOperationException("Compose command must be a scalar or sequence.")
        };
    }

    private static IReadOnlyList<PortMapping> ParsePorts(YamlMappingNode service, IReadOnlyDictionary<string, string> variables)
    {
        if (!TryGetValue(service, "ports", out var ports))
        {
            return [];
        }

        if (ports is not YamlSequenceNode sequence)
        {
            throw new InvalidOperationException("Compose ports must be a sequence.");
        }

        return ContainerCreateInputParser.ParsePortMappings(string.Join(",",
            sequence.Children.Select(node => Interpolate(GetScalarValue(node), variables))));
    }

    private static IReadOnlyList<ContainerMount> ParseVolumes(
        YamlMappingNode service,
        string composeDirectory,
        IReadOnlyDictionary<string, string> variables)
    {
        if (!TryGetValue(service, "volumes", out var volumes))
        {
            return [];
        }

        if (volumes is not YamlSequenceNode sequence)
        {
            throw new InvalidOperationException("Compose volumes must be a sequence.");
        }

        var mountInputs = sequence.Children
            .Select(node => ConvertComposeVolume(Interpolate(GetScalarValue(node), variables), composeDirectory))
            .ToArray();

        return ContainerCreateInputParser.ParseMounts(string.Join(",", mountInputs));
    }

    private static IReadOnlyDictionary<string, string> ParseEnvironment(
        YamlMappingNode service,
        IReadOnlyDictionary<string, string> variables)
    {
        if (!TryGetValue(service, "environment", out var environment))
        {
            return new Dictionary<string, string>();
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        switch (environment)
        {
            case YamlMappingNode mapping:
                foreach (var entry in mapping.Children)
                {
                    result[GetKey(entry.Key)] = Interpolate(GetScalarValue(entry.Value), variables);
                }
                break;

            case YamlSequenceNode sequence:
                foreach (var node in sequence.Children)
                {
                    string item = Interpolate(GetScalarValue(node), variables);
                    int equals = item.IndexOf('=');
                    if (equals < 0)
                    {
                        result[item] = variables.TryGetValue(item, out string? value) ? value : string.Empty;
                    }
                    else
                    {
                        result[item[..equals]] = item[(equals + 1)..];
                    }
                }
                break;

            default:
                throw new InvalidOperationException("Compose environment must be a mapping or sequence.");
        }

        return result;
    }

    private static IReadOnlyList<string> ParseDependsOn(YamlMappingNode service)
    {
        if (!TryGetValue(service, "depends_on", out var dependsOn))
        {
            return [];
        }

        return dependsOn switch
        {
            YamlSequenceNode sequence => sequence.Children.Select(GetScalarValue).ToArray(),
            YamlMappingNode mapping => mapping.Children.Keys.Select(GetKey).ToArray(),
            YamlScalarNode scalar when !string.IsNullOrWhiteSpace(scalar.Value) => [scalar.Value],
            _ => []
        };
    }

    private static IReadOnlyList<ComposeServicePlan> OrderByDependencies(IReadOnlyList<ComposeServicePlan> plans)
    {
        var remaining = plans.ToList();
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ComposeServicePlan>();

        while (remaining.Count > 0)
        {
            int before = remaining.Count;

            foreach (var plan in remaining.ToArray())
            {
                if (plan.DependsOn.All(dependency => completed.Contains(dependency) || plans.All(candidate => !candidate.ServiceName.Equals(dependency, StringComparison.OrdinalIgnoreCase))))
                {
                    ordered.Add(plan);
                    completed.Add(plan.ServiceName);
                    remaining.Remove(plan);
                }
            }

            if (remaining.Count == before)
            {
                throw new InvalidOperationException("Compose service dependencies contain a cycle.");
            }
        }

        return ordered;
    }

    private async Task PullImageAsync(string image, CancellationToken cancellationToken)
    {
        await foreach (var _ in _imageService.PullImageAsync(new ImagePullRequest(image), cancellationToken))
        {
        }
    }

    private static string ConvertComposeVolume(string volume, string composeDirectory)
    {
        int separator = volume.LastIndexOf(":/", StringComparison.Ordinal);
        if (separator < 0)
        {
            throw new InvalidOperationException($"Unsupported compose volume syntax: {volume}");
        }

        string source = volume[..separator].Trim();
        string targetAndMode = volume[(separator + 1)..].Trim();

        if (IsRelativeBindMount(source))
        {
            source = Path.GetFullPath(Path.Combine(composeDirectory, source));
        }

        return $"{source}=>{targetAndMode}";
    }

    private static bool IsRelativeBindMount(string source)
    {
        return source.StartsWith(".", StringComparison.Ordinal)
            || source.StartsWith("~", StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, string>> LoadDotEnvAsync(string composeDirectory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(composeDirectory, ".env");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(path))
        {
            return result;
        }

        foreach (string rawLine in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            result[line[..equals].Trim()] = line[(equals + 1)..].Trim().Trim('"');
        }

        return result;
    }

    private static string Interpolate(string value, IReadOnlyDictionary<string, string> variables)
    {
        return VariablePattern.Replace(value, match =>
        {
            string name = match.Groups["name"].Value;
            return variables.TryGetValue(name, out string? replacement) ? replacement : string.Empty;
        });
    }

    private static YamlMappingNode GetRequiredMapping(YamlMappingNode parent, string key)
    {
        return TryGetValue(parent, key, out var value) && value is YamlMappingNode mapping
            ? mapping
            : throw new InvalidOperationException($"Compose key '{key}' is required and must be a mapping.");
    }

    private static string? GetOptionalScalar(YamlMappingNode parent, string key, IReadOnlyDictionary<string, string> variables)
    {
        return TryGetValue(parent, key, out var value)
            ? Interpolate(GetScalarValue(value), variables)
            : null;
    }

    private static bool TryGetValue(YamlMappingNode mapping, string key, out YamlNode value)
    {
        foreach (var entry in mapping.Children)
        {
            if (GetKey(entry.Key).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = new YamlScalarNode();
        return false;
    }

    private static string GetKey(YamlNode node)
    {
        return node is YamlScalarNode scalar
            ? scalar.Value ?? string.Empty
            : throw new InvalidOperationException("Compose mapping keys must be scalar values.");
    }

    private static string GetScalarValue(YamlNode node)
    {
        return node is YamlScalarNode scalar
            ? scalar.Value ?? string.Empty
            : throw new InvalidOperationException("Compose value must be a scalar value.");
    }
}
