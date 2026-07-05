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
            "nginx -g 'daemon off;'"),
        new(
            "a91dc77e",
            "api-dev",
            "docker.io/library/ubuntu:latest",
            ContainerRuntimeState.Stopped,
            0,
            "-",
            "Today",
            "Exited",
            "-",
            "/bin/bash")
    ];

    private readonly ConcurrentQueue<OperationRecord> _operations = new();
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

    public async IAsyncEnumerable<ImagePullProgress> PullImageAsync(
        ImagePullRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (ulong step = 1; step <= 3; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(150, cancellationToken);
            yield return new ImagePullProgress(request.Reference, "MockPulling", step, 3, ImagePullProgressKind.Progress);
        }

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
            request.Command.Count == 0 ? "/bin/sh" : string.Join(" ", request.Command));

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
        TrackContainerOperation(containerId, "Start container");
        return Task.CompletedTask;
    }

    public Task StopAsync(string containerId, CancellationToken cancellationToken = default)
    {
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
        IReadOnlyList<ComposeProjectSummary> projects =
        [
            new("sample-stack", 2, 1, @"C:\Projects\sample\compose.yaml")
        ];

        return Task.FromResult(projects);
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
                AutoRemove: false), cancellationToken);

            await StartAsync(container.Id, cancellationToken);
            created.Add(container);
        }

        return created;
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
}
