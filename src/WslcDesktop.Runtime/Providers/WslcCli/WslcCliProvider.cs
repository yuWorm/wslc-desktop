using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using WslcDesktop.Contracts;

namespace WslcDesktop.Runtime.Providers.WslcCli;

public sealed class WslcCliProvider : IContainerRuntimeProvider
{
    private const int MaxExecSessionCount = 128;
    private static readonly TimeSpan CompletedExecRetention = TimeSpan.FromMinutes(15);

    private static readonly RuntimeCapabilities Capabilities = new(
        ListContainers: true,
        InspectContainer: true,
        CreateContainer: true,
        StartStopContainer: true,
        RemoveContainer: true,
        NamedVolumes: true,
        BindMounts: true,
        PortPublishing: true,
        RestartPolicy: false,
        LogsBuffered: true,
        LogsFollow: false,
        StatsOneShot: true,
        StatsStreaming: false,
        ExecNonInteractive: true,
        ExecInteractiveTty: false,
        DockerNetworkCrud: false,
        ImagePull: true,
        ImagePullProgress: false,
        ImageRemove: true,
        ImagePush: false,
        ImageBuild: false);

    private readonly WslcCommandRunner _runner;
    private readonly WslcRuntimeSettings _settings;
    private readonly ConcurrentDictionary<string, ExecSession> _execSessions = new(StringComparer.OrdinalIgnoreCase);

    public WslcCliProvider(WslcCommandRunner runner)
    {
        _runner = runner;
        _settings = WslcRuntimeSettings.Empty;
    }

    public WslcCliProvider(WslcCommandRunner runner, WslcRuntimeSettings settings)
    {
        _runner = runner;
        _settings = settings;
    }

    public string Name => WslcdDefaults.DefaultRuntimeProviderName;

    public async Task<RuntimeInfoResponse> GetRuntimeInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync("version", cancellationToken);
        return result.ExitCode == 0
            ? new RuntimeInfoResponse(Name, true, result.StandardOutput, Capabilities, "WSLC CLI is available.")
            : new RuntimeInfoResponse(Name, false, string.Empty, Capabilities, result.StandardError);
    }

    public async Task<IReadOnlyList<ContainerSummaryDto>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunRequiredAsync("container list --all --format json", cancellationToken);
        var containers = WslcCliOutputParser.ParseContainers(result.StandardOutput).ToList();

        var statsResult = await _runner.RunAsync("stats --all --format json", cancellationToken);
        if (statsResult.ExitCode == 0)
        {
            var stats = WslcCliOutputParser.ParseStats(statsResult.StandardOutput);
            containers = containers
                .Select(container => stats.TryGetValue(container.Id, out var snapshot) || stats.TryGetValue(container.Name, out snapshot)
                    ? container with { CpuPercent = snapshot.CpuPercent, MemoryUsed = snapshot.MemoryUsed }
                    : container)
                .ToList();
        }

        return await EnrichContainersWithInspectAsync(containers, cancellationToken);
    }

    public async Task<ContainerSummaryDto> InspectContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        string id = RequireId(idOrName);
        var containers = await ListContainersAsync(cancellationToken);
        return containers.FirstOrDefault(container =>
                container.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                container.Name.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Container not found: {id}");
    }

    public async Task<ContainerCreateResultDto> CreateContainerAsync(ContainerCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Image))
        {
            throw new ArgumentException("Container image is required.", nameof(request));
        }

        ValidateCreateRequest(request);

        var args = new List<string> { "create" };
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            args.Add("--name");
            args.Add(request.Name.Trim());
        }

        if (request.AutoRemove)
        {
            args.Add("--rm");
        }

        foreach (var pair in request.Environment)
        {
            args.Add("-e");
            args.Add($"{pair.Key}={pair.Value}");
        }

        foreach (var pair in request.Labels ?? new Dictionary<string, string>())
        {
            args.Add("-l");
            args.Add(string.IsNullOrEmpty(pair.Value) ? pair.Key : $"{pair.Key}={pair.Value}");
        }

        foreach (var port in request.Ports)
        {
            args.Add("-p");
            args.Add($"{port.HostPort}:{port.ContainerPort}/{NormalizeProtocol(port.Protocol)}");
        }

        foreach (var mount in request.Mounts)
        {
            args.Add("-v");
            args.Add($"{mount.Source}:{mount.Target}{(mount.IsReadOnly ? ":ro" : string.Empty)}");
        }

        args.Add(request.Image.Trim());
        args.AddRange(request.Command.Where(value => !string.IsNullOrWhiteSpace(value)));

        var result = await RunRequiredAsync(WslcCliCommandLine.Join(args), cancellationToken);
        string id = FirstOutputToken(result.StandardOutput);
        return new ContainerCreateResultDto(string.IsNullOrWhiteSpace(id) ? request.Name : ShortId(id), []);
    }

    public Task StartContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(WslcCliCommandLine.Join("container", "start", RequireId(idOrName)), cancellationToken);
    }

    public Task StopContainerAsync(string idOrName, ContainerStopOptionsDto options, CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(WslcCliCommandLine.Join("container", "stop", RequireId(idOrName)), cancellationToken);
    }

    public Task RemoveContainerAsync(string idOrName, ContainerRemoveOptionsDto options, CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(WslcCliCommandLine.Join("container", "remove", "-f", RequireId(idOrName)), cancellationToken);
    }

    public async Task<IReadOnlyList<ImageSummaryDto>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunRequiredAsync("image list --format json", cancellationToken);
        return WslcCliOutputParser.ParseImages(result.StandardOutput);
    }

    public async IAsyncEnumerable<ImagePullProgressDto> PullImageProgressAsync(
        string reference,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Image reference is required.", nameof(reference));
        }

        string requestedImage = reference.Trim();
        string image = WslcImageReferencePolicy.ApplyMirror(requestedImage, _settings);
        yield return ImagePullProgressDto.StatusFrame(image, "Pulling");
        await RunRequiredAsync(WslcCliCommandLine.Join("pull", image), cancellationToken);
        yield return ImagePullProgressDto.StatusFrame(image, "Pulled");

        string rewriteTarget = WslcImageReferencePolicy.GetRewriteTarget(image, _settings);
        if (!string.IsNullOrWhiteSpace(rewriteTarget) &&
            !rewriteTarget.Equals(image, StringComparison.OrdinalIgnoreCase))
        {
            yield return ImagePullProgressDto.StatusFrame(rewriteTarget, "Tagging");
            await RunRequiredAsync(WslcCliCommandLine.Join("tag", image, rewriteTarget), cancellationToken);
            if (_settings.RemoveRewrittenSourceTag)
            {
                yield return ImagePullProgressDto.StatusFrame(image, "Removing source tag");
                await RunRequiredAsync(WslcCliCommandLine.Join("rmi", image), cancellationToken);
            }

            yield return ImagePullProgressDto.StatusFrame(rewriteTarget, "Completed");
            yield break;
        }

        yield return ImagePullProgressDto.StatusFrame(image, "Completed");
    }

    public async Task<IReadOnlyList<ImagePullProgressDto>> PullImageAsync(string reference, CancellationToken cancellationToken = default)
    {
        var frames = new List<ImagePullProgressDto>();
        await foreach (var frame in PullImageProgressAsync(reference, cancellationToken))
        {
            frames.Add(frame);
        }

        return frames;
    }

    public Task RemoveImageAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(WslcCliCommandLine.Join("rmi", RequireId(idOrName)), cancellationToken);
    }

    public async Task<IReadOnlyList<VolumeSummaryDto>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunRequiredAsync("volume list --format json", cancellationToken);
        return WslcCliOutputParser.ParseVolumes(result.StandardOutput);
    }

    public async Task<VolumeSummaryDto> CreateVolumeAsync(string name, IReadOnlyDictionary<string, string>? labels = null, CancellationToken cancellationToken = default)
    {
        string volumeName = RequireId(name);
        var args = new List<string> { "volume", "create" };
        foreach (var pair in labels ?? new Dictionary<string, string>())
        {
            args.Add("-l");
            args.Add(string.IsNullOrEmpty(pair.Value) ? pair.Key : $"{pair.Key}={pair.Value}");
        }

        args.Add(volumeName);
        await RunRequiredAsync(WslcCliCommandLine.Join(args), cancellationToken);
        return new VolumeSummaryDto(volumeName, "-", "-", DateTimeOffset.Now.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), true, labels);
    }

    public Task RemoveVolumeAsync(string name, bool force, CancellationToken cancellationToken = default)
    {
        return RunRequiredAsync(WslcCliCommandLine.Join("volume", "remove", force ? "-f" : string.Empty, RequireId(name)), cancellationToken);
    }

    public async Task<IReadOnlyList<NetworkEndpointSummaryDto>> ListNetworksAsync(CancellationToken cancellationToken = default)
    {
        var containers = await ListContainersAsync(cancellationToken);
        return WslcCliOutputParser.ParsePublishedPorts(containers);
    }

    public async IAsyncEnumerable<LogFrameDto> StreamLogsAsync(
        string idOrName,
        LogStreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int tail = options.Tail <= 0 ? 200 : options.Tail;
        var result = await RunRequiredAsync(WslcCliCommandLine.Join("logs", "--tail", tail.ToString(System.Globalization.CultureInfo.InvariantCulture), RequireId(idOrName)), cancellationToken);
        foreach (string line in result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return new LogFrameDto(DateTimeOffset.UtcNow, "stdout", line);
            }
        }
    }

    public async IAsyncEnumerable<StatsFrameDto> StreamStatsAsync(
        string idOrName,
        StatsStreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var container = await InspectContainerAsync(idOrName, cancellationToken);
        var result = await _runner.RunAsync("stats --all --format json", cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new RuntimeCommandException(Name, result);
        }

        var stats = WslcCliOutputParser.ParseStats(result.StandardOutput);
        if (stats.TryGetValue(container.Id, out var snapshot) || stats.TryGetValue(container.Name, out snapshot))
        {
            yield return new StatsFrameDto(container.Id, container.Name, snapshot.CpuPercent, snapshot.MemoryUsed, DateTimeOffset.UtcNow);
        }
    }

    public Task<ExecCreateResultDto> CreateExecAsync(string idOrName, ExecCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        string containerId = RequireId(idOrName);
        if (request.Command.Count == 0)
        {
            throw new ArgumentException("Exec command is required.", nameof(request));
        }

        string execId = Guid.NewGuid().ToString("N");
        PruneExecSessions();
        _execSessions[execId] = new ExecSession(execId, containerId, request.Command.ToArray(), request.StandardInput, request.Tty, DateTimeOffset.UtcNow, null, null, false);
        return Task.FromResult(new ExecCreateResultDto(execId));
    }

    public async IAsyncEnumerable<ExecFrameDto> StartExecAsync(
        string execId,
        ExecStartOptionsDto options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_execSessions.TryGetValue(execId, out var session))
        {
            throw new KeyNotFoundException($"Exec session not found: {execId}");
        }

        _execSessions[execId] = session with { Running = true, CompletedAt = null };
        var args = new List<string> { "exec", session.ContainerId };
        args.AddRange(string.IsNullOrEmpty(session.StandardInput)
            ? session.Command
            : CreateShellPipedCommand(session.Command, session.StandardInput));

        var result = await _runner.RunAsync(WslcCliCommandLine.Join(args), cancellationToken);
        _execSessions[execId] = session with { ExitCode = result.ExitCode, Running = false, CompletedAt = DateTimeOffset.UtcNow };
        PruneExecSessions();

        if (!string.IsNullOrEmpty(result.StandardOutput))
        {
            yield return new ExecFrameDto(DateTimeOffset.UtcNow, "stdout", result.StandardOutput);
        }

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            yield return new ExecFrameDto(DateTimeOffset.UtcNow, "stderr", result.StandardError);
        }
    }

    public Task<ExecDetailDto> InspectExecAsync(string execId, CancellationToken cancellationToken = default)
    {
        PruneExecSessions();
        return _execSessions.TryGetValue(execId, out var session)
            ? Task.FromResult(new ExecDetailDto(execId, session.Running, session.ExitCode, session.Tty))
            : throw new KeyNotFoundException($"Exec session not found: {execId}");
    }

    private async Task<RuntimeCommandResult> RunRequiredAsync(string arguments, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new RuntimeCommandException(Name, result);
        }

        return result;
    }

    private static string RequireId(string idOrName)
    {
        return string.IsNullOrWhiteSpace(idOrName)
            ? throw new ArgumentException("Resource id or name is required.", nameof(idOrName))
            : idOrName.Trim();
    }

    private static void ValidateCreateRequest(ContainerCreateRequestDto request)
    {
        foreach (var port in request.Ports)
        {
            ValidatePort(port.HostPort, "host");
            ValidatePort(port.ContainerPort, "container");
        }

        var duplicateRequested = request.Ports
            .GroupBy(port => $"{port.HostPort}/{NormalizeProtocol(port.Protocol)}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateRequested is not null)
        {
            throw new ArgumentException($"Duplicate host port mapping in request: {duplicateRequested.Key}.");
        }

        foreach (var mount in request.Mounts.Where(mount => !mount.IsNamedVolume))
        {
            if (!Directory.Exists(mount.Source) && !File.Exists(mount.Source))
            {
                throw new DirectoryNotFoundException($"Bind mount source does not exist: {mount.Source}");
            }
        }
    }

    private static void ValidatePort(int port, string label)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(label, port, "Ports must be between 1 and 65535.");
        }
    }

    private async Task<IReadOnlyList<ContainerSummaryDto>> EnrichContainersWithInspectAsync(
        IReadOnlyList<ContainerSummaryDto> containers,
        CancellationToken cancellationToken)
    {
        var enriched = new List<ContainerSummaryDto>(containers.Count);
        foreach (var container in containers)
        {
            var inspect = await _runner.RunAsync(WslcCliCommandLine.Join("inspect", container.Id), cancellationToken);
            if (inspect.ExitCode != 0)
            {
                enriched.Add(container);
                continue;
            }

            var inspected = WslcCliOutputParser.ParseContainers(inspect.StandardOutput).FirstOrDefault();
            if (inspected is null)
            {
                enriched.Add(container);
                continue;
            }

            enriched.Add(container with
            {
                InspectJson = inspected.InspectJson,
                Labels = inspected.Labels,
                Command = inspected.Command == "-" ? container.Command : inspected.Command,
                PortSummary = inspected.PortSummary == "-" ? container.PortSummary : inspected.PortSummary
            });
        }

        return enriched;
    }

    private static string NormalizeProtocol(string protocol)
    {
        return protocol.Equals("udp", StringComparison.OrdinalIgnoreCase) ? "udp" : "tcp";
    }

    private static string FirstOutputToken(string output)
    {
        return output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string ShortId(string id)
    {
        return id.Length <= 12 ? id : id[..12];
    }

    private static List<string> CreateShellPipedCommand(IReadOnlyList<string> command, string stdin)
    {
        string quotedInput = QuoteShell(stdin);
        string quotedCommand = string.Join(" ", command.Select(QuoteShell));
        return ["/bin/sh", "-c", $"printf %s {quotedInput} | {quotedCommand}"];
    }

    private static string QuoteShell(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private void PruneExecSessions()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var pair in _execSessions)
        {
            if (!pair.Value.Running &&
                pair.Value.CompletedAt is not null &&
                now - pair.Value.CompletedAt > CompletedExecRetention)
            {
                _execSessions.TryRemove(pair.Key, out _);
            }
        }

        if (_execSessions.Count <= MaxExecSessionCount)
        {
            return;
        }

        foreach (var session in _execSessions.Values
            .Where(session => !session.Running)
            .OrderBy(session => session.CompletedAt ?? session.CreatedAt)
            .Take(Math.Max(0, _execSessions.Count - MaxExecSessionCount)))
        {
            _execSessions.TryRemove(session.ExecId, out _);
        }
    }

    private sealed record ExecSession(
        string ExecId,
        string ContainerId,
        IReadOnlyList<string> Command,
        string StandardInput,
        bool Tty,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt,
        int? ExitCode,
        bool Running);
}
