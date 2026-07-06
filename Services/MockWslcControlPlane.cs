using System.Collections.Concurrent;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public sealed class MockWslcControlPlane :
    IWslcHostService,
    IMockWslcScenarioService,
    IWslcImageService,
    IWslcContainerService,
    IWslcProcessService,
    IWslcVolumeService,
    IWslcNetworkService,
    IComposePlanService,
    IWslcCommandFallback,
    IAppSettingsService,
    IOperationTracker
{
    private readonly List<ContainerSummary> _containers =
    [
        new(
            "2cf0b8d1",
            "web",
            "docker.io/library/nginx:latest",
            ContainerRuntimeState.Running,
            1.8,
            "38 MB",
            "Today",
            "9 minutes",
            "localhost:8080 -> 80/tcp",
            "nginx -g 'daemon off;'",
            Labels: new Dictionary<string, string>
            {
                [ComposeLabels.Project] = "sample-stack",
                [ComposeLabels.Service] = "web",
                [ComposeLabels.ProjectConfigFiles] = @"C:\Projects\sample\compose.yaml",
                [ComposeLabels.ProjectWorkingDir] = @"C:\Projects\sample"
            }),
        new(
            "a91dc77e",
            "worker",
            "docker.io/library/ubuntu:latest",
            ContainerRuntimeState.Stopped,
            0,
            "-",
            "Today",
            "Exited",
            "-",
            "/bin/bash",
            Labels: new Dictionary<string, string>
            {
                [ComposeLabels.Project] = "sample-stack",
                [ComposeLabels.Service] = "worker",
                [ComposeLabels.ProjectConfigFiles] = @"C:\Projects\sample\compose.yaml",
                [ComposeLabels.ProjectWorkingDir] = @"C:\Projects\sample"
            })
    ];

    private readonly ConcurrentQueue<OperationRecord> _operations = new();
    private readonly List<ImagePullTaskSnapshot> _pullTasks = [];
    private readonly List<VolumeSummary> _volumes =
    [
        new("web-content", "24 MB", "web", "Today", true),
        new("postgres-data", "1.3 GB", "db", "Yesterday", true)
    ];

    private AppSettingsSnapshot _settings = new(
        DataRoot: Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WSLC Desktop"),
        CpuCount: Math.Max(1, Environment.ProcessorCount / 2),
        MemoryMB: 4096,
        DefaultShell: "/bin/sh",
        PreferExternalTerminal: false,
        Language: AppLanguage.System);

    public MockHostScenario HostScenario { get; set; } = MockHostScenario.Ready;

    public MockWslcControlPlane()
    {
        Track(new OperationRecord(
            Guid.NewGuid().ToString("N"),
            "Mock control plane initialized",
            OperationState.Succeeded,
            "Phase 0 runs without WSLC preview components.",
            DateTimeOffset.Now));
    }

    public Task<EngineSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var summary = HostScenario switch
        {
            MockHostScenario.MissingComponents => new EngineSummary(
                EngineReadinessState.MissingComponents,
                "-",
                "Mock scenario: WSL/WSLC components are missing. Real remediation starts in Phase 2.",
                0,
                0,
                "-",
                0,
                "-"),
            MockHostScenario.Unsupported => new EngineSummary(
                EngineReadinessState.Unsupported,
                "-",
                "Mock scenario: this Windows build does not expose the WSLC preview API.",
                0,
                0,
                "-",
                0,
                "-"),
            MockHostScenario.Error => new EngineSummary(
                EngineReadinessState.Error,
                "-",
                "Mock scenario: host readiness check failed.",
                0,
                0,
                "-",
                0,
                "-"),
            _ => new EngineSummary(
                EngineReadinessState.Ready,
                "mock-preview",
                "Mock WSLC CLI host is ready.",
                _containers.Count(c => c.State == ContainerRuntimeState.Running),
                2,
                "1.5 GB",
                2.3,
                "412 MB")
        };

        return Task.FromResult(summary);
    }

    public Task<IReadOnlyList<string>> GetMissingComponentsAsync(CancellationToken cancellationToken = default)
    {
        if (HostScenario == MockHostScenario.MissingComponents)
        {
            return Task.FromResult<IReadOnlyList<string>>(["WSL", "WSLC"]);
        }

        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task StartAsync(AppSettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        Track(new OperationRecord(
            Guid.NewGuid().ToString("N"),
            "CLI control plane initialized",
            OperationState.Succeeded,
            "Mock wslc CLI control plane is available.",
            DateTimeOffset.Now));

        return Task.CompletedTask;
    }

    public Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        Track(new OperationRecord(
            Guid.NewGuid().ToString("N"),
            "CLI control plane released",
            OperationState.Succeeded,
            "Mock wslc CLI control plane released.",
            DateTimeOffset.Now));

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ImageSummary> images =
        [
            new("sha256:7f", "docker.io/library/nginx", "latest", "192 MB", "2 days ago", true),
            new("sha256:4e", "docker.io/library/ubuntu", "latest", "78 MB", "5 days ago", true)
        ];

        return Task.FromResult(images);
    }

    public Task<IReadOnlyList<ImagePullTaskSnapshot>> ListPullTasksAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ImagePullTaskSnapshot>>(_pullTasks.OrderByDescending(task => task.StartedAt).ToArray());
    }

    public async IAsyncEnumerable<ImagePullProgress> PullImageAsync(
        ImagePullRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string taskId = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        _pullTasks.Add(new ImagePullTaskSnapshot(
            taskId,
            request.Reference,
            "mock",
            "Running",
            startedAt,
            null,
            request.Reference,
            "Queued",
            0,
            0,
            string.Empty));

        for (ulong step = 1; step <= 3; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(150, cancellationToken);
            UpdateMockPullTask(taskId, "Running", $"MockPulling {step}/3", step, 3, string.Empty, completedAt: null);
            yield return new ImagePullProgress(request.Reference, "MockPulling", step, 3, ImagePullProgressKind.Progress);
        }

        UpdateMockPullTask(taskId, "Succeeded", "Completed", 3, 3, string.Empty, DateTimeOffset.UtcNow);
        Track(new OperationRecord(
            Guid.NewGuid().ToString("N"),
            "Pull image",
            OperationState.Succeeded,
            $"Mock image pull completed for {request.Reference}.",
            DateTimeOffset.Now));
    }

    public Task DeleteImageAsync(string imageReference, CancellationToken cancellationToken = default)
    {
        Track(new OperationRecord(
            Guid.NewGuid().ToString("N"),
            "Delete image",
            OperationState.Succeeded,
            $"Mock image delete requested for {imageReference}.",
            DateTimeOffset.Now));

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ContainerSummary>>(_containers.ToArray());
    }

    public Task<ContainerSummary> CreateAsync(ContainerCreateRequest request, CancellationToken cancellationToken = default)
    {
        var container = new ContainerSummary(
            Guid.NewGuid().ToString("N")[..12],
            string.IsNullOrWhiteSpace(request.Name) ? $"container-{_containers.Count + 1}" : request.Name,
            request.Image,
            ContainerRuntimeState.Created,
            0,
            "-",
            "Today",
            "Created",
            request.Ports.Count == 0
                ? "-"
                : string.Join(", ", request.Ports.Select(port => $"localhost:{port.HostPort} -> {port.ContainerPort}/{port.Protocol}")),
            request.Command.Count == 0 ? "/bin/sh" : string.Join(" ", request.Command),
            Labels: request.Labels);

        _containers.Add(container);
        Track(new OperationRecord(
            Guid.NewGuid().ToString("N"),
            "Create container",
            OperationState.Succeeded,
            container.Name,
            DateTimeOffset.Now));

        return Task.FromResult(container);
    }

    public Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        UpdateContainerState(containerId, ContainerRuntimeState.Running, "Running");
        TrackContainerOperation(containerId, "Start container");
        return Task.CompletedTask;
    }

    public Task StopAsync(string containerId, CancellationToken cancellationToken = default)
    {
        UpdateContainerState(containerId, ContainerRuntimeState.Stopped, "Exited");
        TrackContainerOperation(containerId, "Stop container");
        return Task.CompletedTask;
    }

    public Task RestartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        TrackContainerOperation(containerId, "Restart container");
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string containerId, CancellationToken cancellationToken = default)
    {
        TrackContainerOperation(containerId, "Delete container");
        _containers.RemoveAll(container => container.Id.Equals(containerId, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ContainerLogLine> StreamLogsAsync(
        string containerId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string name = _containers.FirstOrDefault(c => c.Id == containerId)?.Name ?? containerId;

        for (int index = 1; index <= 4; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);
            yield return new ContainerLogLine(
                DateTimeOffset.Now,
                LogStream.Stdout,
                $"{name}: mock log line {index}");
        }
    }

    public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var output = $"mock exec: {string.Join(" ", request.Command)}";
        if (!string.IsNullOrWhiteSpace(request.StandardInput))
        {
            output += Environment.NewLine + request.StandardInput;
        }

        return Task.FromResult(new ProcessExecutionResult(0, output, string.Empty));
    }

    public Task<IReadOnlyList<VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<VolumeSummary>>(_volumes.ToArray());
    }

    public Task CreateNamedVolumeAsync(VolumeCreateRequest request, CancellationToken cancellationToken = default)
    {
        _volumes.Add(new VolumeSummary(request.Name, $"{request.SizeBytes / 1024 / 1024} MB", "-", "Today", true));
        return Task.CompletedTask;
    }

    public Task DeleteNamedVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        _volumes.RemoveAll(volume => volume.IsNamed && volume.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NetworkEndpointSummary>> ListPublishedPortsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NetworkEndpointSummary> endpoints =
        [
            new("web", 8080, 80, "tcp", "http://localhost:8080")
        ];

        return Task.FromResult(endpoints);
    }

    public Task<IReadOnlyList<ComposeProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ComposeProjectSummary> projects = _containers
            .Where(container => container.Labels?.ContainsKey(ComposeLabels.Project) == true)
            .GroupBy(container => container.Labels![ComposeLabels.Project], StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildMockProjectSummary(group.Key, group.ToArray()))
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(projects);
    }

    public Task<ComposeProjectDetails> InspectProjectAsync(string projectName, CancellationToken cancellationToken = default)
    {
        var containers = GetMockProjectContainers(projectName);
        var project = BuildMockProjectSummary(projectName, containers);
        var services = containers
            .GroupBy(container => container.Labels?.GetValueOrDefault(ComposeLabels.Service) ?? container.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ComposeServiceRuntimeSummary(
                projectName,
                group.Key,
                group.First().Image,
                group.Count(),
                group.Count(container => container.State == ContainerRuntimeState.Running),
                JoinMockValues(group.Select(container => container.PortSummary), "-"),
                JoinMockValues(group.Select(container => container.State.ToString()), "-")))
            .OrderBy(service => service.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rows = containers
            .Select(container => new ComposeContainerRuntimeSummary(
                projectName,
                container.Labels?.GetValueOrDefault(ComposeLabels.Service) ?? container.Name,
                container.Id,
                container.Name,
                container.Image,
                container.State,
                container.CpuPercent,
                container.MemoryUsed,
                container.Created,
                container.Uptime,
                container.PortSummary))
            .OrderBy(container => container.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(container => container.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(new ComposeProjectDetails(project, services, rows));
    }

    public Task<IReadOnlyList<ComposeServicePlan>> PreviewAsync(string composePath, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ComposeServicePlan> plan =
        [
            new(
                "sample-stack",
                "web",
                "docker.io/library/nginx:latest",
                ["/docker-entrypoint.sh", "nginx", "-g", "daemon off;"],
                [new PortMapping(8080, 80, "tcp")],
                [new ContainerMount(@"C:\Projects\sample\site", "/usr/share/nginx/html", true, false)],
                new Dictionary<string, string>(),
                [],
                []),
            new(
                "sample-stack",
                "worker",
                "docker.io/library/ubuntu:latest",
                ["/bin/bash", "-lc", "sleep infinity"],
                [],
                [],
                new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Development" },
                [],
                ["depends_on.condition"])
        ];

        return Task.FromResult(plan);
    }

    public async Task<IReadOnlyList<ContainerSummary>> CreateAndStartAsync(string composePath, CancellationToken cancellationToken = default)
    {
        var plan = await PreviewAsync(composePath, cancellationToken);
        var created = new List<ContainerSummary>();

        foreach (var service in plan)
        {
            var container = await CreateAsync(new ContainerCreateRequest(
                $"{service.ProjectName}-{service.ServiceName}",
                service.Image,
                service.Command,
                service.Ports,
                service.Mounts,
                service.Environment,
                EnableGpu: false,
                AutoRemove: false,
                new Dictionary<string, string>
                {
                    [ComposeLabels.Project] = service.ProjectName,
                    [ComposeLabels.Service] = service.ServiceName,
                    [ComposeLabels.ProjectConfigFiles] = composePath,
                    [ComposeLabels.ProjectWorkingDir] = Path.GetDirectoryName(composePath) ?? string.Empty
                }), cancellationToken);

            await StartAsync(container.Id, cancellationToken);
            created.Add(container);
        }

        return created;
    }

    public async Task StartProjectAsync(string projectName, CancellationToken cancellationToken = default)
    {
        foreach (var container in GetMockProjectContainers(projectName).Where(container => container.State != ContainerRuntimeState.Running))
        {
            await StartAsync(container.Id, cancellationToken);
        }
    }

    public async Task StopProjectAsync(string projectName, CancellationToken cancellationToken = default)
    {
        foreach (var container in GetMockProjectContainers(projectName).Where(container => container.State == ContainerRuntimeState.Running))
        {
            await StopAsync(container.Id, cancellationToken);
        }
    }

    public async Task RestartProjectAsync(string projectName, CancellationToken cancellationToken = default)
    {
        foreach (var container in GetMockProjectContainers(projectName).Where(container => container.State == ContainerRuntimeState.Running))
        {
            await RestartAsync(container.Id, cancellationToken);
        }
    }

    public async Task DeleteProjectAsync(string projectName, CancellationToken cancellationToken = default)
    {
        foreach (var container in GetMockProjectContainers(projectName))
        {
            if (container.State == ContainerRuntimeState.Running)
            {
                await StopAsync(container.Id, cancellationToken);
            }

            await DeleteAsync(container.Id, cancellationToken);
        }
    }

    public Task<CommandResult> RunAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var result = new CommandResult(
            0,
            $"mock wslc.exe {arguments}",
            string.Empty,
            TimeSpan.FromMilliseconds(12));

        return Task.FromResult(result);
    }

    public Task<AppSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings);
    }

    public Task SaveAsync(AppSettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OperationRecord>> ListRecentAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OperationRecord>>(_operations.Take(8).ToArray());
    }

    public void Track(OperationRecord operation)
    {
        _operations.Enqueue(operation);

        while (_operations.Count > 20 && _operations.TryDequeue(out _))
        {
        }
    }

    private void TrackContainerOperation(string containerId, string title)
    {
        var name = _containers.FirstOrDefault(c => c.Id == containerId)?.Name ?? containerId;
        Track(new OperationRecord(
            Guid.NewGuid().ToString("N"),
            title,
            OperationState.Succeeded,
            $"{title} requested for {name}.",
            DateTimeOffset.Now));
    }

    private void UpdateContainerState(string containerId, ContainerRuntimeState state, string uptime)
    {
        int index = _containers.FindIndex(container => container.Id.Equals(containerId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        _containers[index] = _containers[index] with
        {
            State = state,
            Uptime = uptime,
            CpuPercent = state == ContainerRuntimeState.Running ? 1.2 : 0,
            MemoryUsed = state == ContainerRuntimeState.Running ? "42 MB" : "-"
        };
    }

    private IReadOnlyList<ContainerSummary> GetMockProjectContainers(string projectName)
    {
        return _containers
            .Where(container => container.Labels is not null
                && container.Labels.TryGetValue(ComposeLabels.Project, out string? value)
                && value.Equals(projectName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(container => container.Labels?.GetValueOrDefault(ComposeLabels.Service) ?? container.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(container => container.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ComposeProjectSummary BuildMockProjectSummary(string projectName, IReadOnlyCollection<ContainerSummary> containers)
    {
        int serviceCount = containers
            .Select(container => container.Labels?.GetValueOrDefault(ComposeLabels.Service))
            .Where(service => !string.IsNullOrWhiteSpace(service))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        string sourcePath = containers
            .Select(container => container.Labels?.GetValueOrDefault(ComposeLabels.ProjectConfigFiles))
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
            ?? string.Empty;

        return new ComposeProjectSummary(
            projectName,
            serviceCount == 0 ? containers.Count : serviceCount,
            containers.Count(container => container.State == ContainerRuntimeState.Running),
            sourcePath);
    }

    private static string JoinMockValues(IEnumerable<string> values, string emptyValue)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "-")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinct.Length == 0 ? emptyValue : string.Join(", ", distinct);
    }

    private void UpdateMockPullTask(
        string taskId,
        string state,
        string status,
        ulong currentBytes,
        ulong totalBytes,
        string errorMessage,
        DateTimeOffset? completedAt)
    {
        int index = _pullTasks.FindIndex(task => task.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        _pullTasks[index] = _pullTasks[index] with
        {
            State = state,
            Status = status,
            CurrentBytes = currentBytes,
            TotalBytes = totalBytes,
            ErrorMessage = errorMessage,
            CompletedAt = completedAt
        };
    }
}
