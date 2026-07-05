using WslcDesktop.DaemonClient;
using Contracts = WslcDesktop.Contracts;
using AppModels = wslc_desktop.Models;

namespace wslc_desktop.Services;

public sealed class DaemonWslcHostService : IWslcHostService
{
    private readonly IWslcdNativeClient _client;
    private readonly DaemonProcessManager _daemon;

    public DaemonWslcHostService(IWslcdNativeClient client, DaemonProcessManager daemon)
    {
        _client = client;
        _daemon = daemon;
    }

    public async Task<AppModels.EngineSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _daemon.EnsureRunningAsync(cancellationToken);
            var runtime = await _client.GetRuntimeAsync(cancellationToken);
            var version = await _client.GetVersionAsync(cancellationToken);

            if (!runtime.IsAvailable)
            {
                return new AppModels.EngineSummary(
                    AppModels.EngineReadinessState.MissingComponents,
                    runtime.Version,
                    runtime.Message,
                    0,
                    0,
                    "-",
                    0,
                    "-");
            }

            var containers = await _client.ListContainersAsync(cancellationToken);
            var images = await _client.ListImagesAsync(cancellationToken);
            var running = containers.Count(container => container.State == Contracts.ContainerRuntimeState.Running);
            var stats = containers.Where(container => container.CpuPercent > 0 || !string.IsNullOrWhiteSpace(container.MemoryUsed)).ToArray();

            return new AppModels.EngineSummary(
                AppModels.EngineReadinessState.Ready,
                version.Version,
                runtime.Message,
                running,
                images.Count,
                "-",
                stats.Length == 0 ? 0 : stats.Average(container => container.CpuPercent),
                stats.FirstOrDefault()?.MemoryUsed ?? "-");
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or TimeoutException or HttpRequestException or IOException or WslcdClientException)
        {
            return new AppModels.EngineSummary(
                AppModels.EngineReadinessState.Error,
                "-",
                $"wslcd readiness check failed: {ex.Message}",
                0,
                0,
                "-",
                0,
                "-");
        }
    }

    public async Task<IReadOnlyList<string>> GetMissingComponentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _daemon.EnsureRunningAsync(cancellationToken);
            var runtime = await _client.GetRuntimeAsync(cancellationToken);
            return runtime.IsAvailable ? [] : [runtime.Message];
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or TimeoutException or HttpRequestException or IOException or WslcdClientException)
        {
            return [$"wslcd unavailable: {ex.Message}"];
        }
    }
}

public sealed class DaemonRuntimeProviderPreviewService : IRuntimeProviderPreviewService
{
    private readonly IWslcdNativeClient _client;
    private readonly DaemonProcessManager _daemon;

    public DaemonRuntimeProviderPreviewService(IWslcdNativeClient client, DaemonProcessManager daemon)
    {
        _client = client;
        _daemon = daemon;
    }

    public async Task<AppModels.ProviderPreviewSnapshot> PreviewAsync(
        string runtimeProvider,
        string dockerApiHost,
        bool allowTcpDockerApi,
        CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        var preview = await _client.PreviewProviderAsync(new Contracts.ProviderPreviewRequest(
            AppModels.RuntimeProviderSelection.Normalize(runtimeProvider),
            dockerApiHost,
            allowTcpDockerApi), cancellationToken);

        return new AppModels.ProviderPreviewSnapshot(
            preview.RuntimeProviderName,
            preview.Host,
            preview.IsConfigured,
            preview.IsAvailable,
            preview.Message,
            BuildCapabilitySummary(preview.Capabilities));
    }

    private static string BuildCapabilitySummary(Contracts.RuntimeCapabilities capabilities)
    {
        var enabled = new List<string>();
        if (capabilities.ListContainers && capabilities.StartStopContainer)
        {
            enabled.Add("containers");
        }

        if (capabilities.ImagePull && capabilities.ImageRemove)
        {
            enabled.Add("images");
        }

        if (capabilities.NamedVolumes)
        {
            enabled.Add("volumes");
        }

        if (capabilities.LogsBuffered || capabilities.LogsFollow)
        {
            enabled.Add("logs");
        }

        if (capabilities.ExecNonInteractive || capabilities.ExecInteractiveTty)
        {
            enabled.Add("exec");
        }

        return enabled.Count == 0 ? "-" : string.Join(", ", enabled);
    }
}

public sealed class DaemonWslcImageService : IWslcImageService
{
    private readonly IWslcdNativeClient _client;
    private readonly DaemonProcessManager _daemon;

    public DaemonWslcImageService(IWslcdNativeClient client, DaemonProcessManager daemon)
    {
        _client = client;
        _daemon = daemon;
    }

    public async Task<IReadOnlyList<AppModels.ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        var images = await _client.ListImagesAsync(cancellationToken);
        return images.Select(MapImage).ToArray();
    }

    public async IAsyncEnumerable<AppModels.ImagePullProgress> PullImageAsync(
        AppModels.ImagePullRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reference))
        {
            throw new ArgumentException("Image reference is required.", nameof(request));
        }

        await _daemon.EnsureRunningAsync(cancellationToken);
        await foreach (var frame in _client.PullImageProgressAsync(new Contracts.ImagePullRequest(request.Reference.Trim()), cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AppModels.ImagePullProgress(
                frame.Id,
                frame.Status,
                frame.CurrentBytes,
                frame.TotalBytes,
                frame.Kind == Contracts.ImagePullProgressKind.Progress
                    ? AppModels.ImagePullProgressKind.Progress
                    : AppModels.ImagePullProgressKind.Status);
        }
    }

    public async Task DeleteImageAsync(string imageReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            throw new ArgumentException("Image reference is required.", nameof(imageReference));
        }

        await _daemon.EnsureRunningAsync(cancellationToken);
        await _client.RemoveImageAsync(imageReference.Trim(), cancellationToken);
    }

    private static AppModels.ImageSummary MapImage(Contracts.ImageSummaryDto image)
    {
        return new AppModels.ImageSummary(image.Id, image.Repository, image.Tag, image.Size, image.Created, image.IsInUse);
    }
}

public sealed class DaemonWslcContainerService : IWslcContainerService
{
    private readonly IWslcdNativeClient _client;
    private readonly DaemonProcessManager _daemon;

    public DaemonWslcContainerService(IWslcdNativeClient client, DaemonProcessManager daemon)
    {
        _client = client;
        _daemon = daemon;
    }

    public async Task<IReadOnlyList<AppModels.ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        var containers = await _client.ListContainersAsync(cancellationToken);
        return containers.Select(MapContainer).ToArray();
    }

    public async Task<AppModels.ContainerSummary> CreateAsync(AppModels.ContainerCreateRequest request, CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        var result = await _client.CreateContainerAsync(new Contracts.ContainerCreateRequestDto(
            request.Name,
            request.Image,
            request.Command,
            request.Ports.Select(port => new Contracts.PortMappingDto(port.HostPort, port.ContainerPort, port.Protocol)).ToArray(),
            request.Mounts.Select(mount => new Contracts.ContainerMountDto(mount.Source, mount.Target, mount.IsReadOnly, mount.IsNamedVolume)).ToArray(),
            request.Environment,
            request.AutoRemove), cancellationToken);

        try
        {
            return MapContainer(await _client.InspectContainerAsync(result.Id, cancellationToken));
        }
        catch
        {
            return new AppModels.ContainerSummary(
                result.Id,
                string.IsNullOrWhiteSpace(request.Name) ? result.Id : request.Name,
                request.Image,
                AppModels.ContainerRuntimeState.Created,
                0,
                "-",
                DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"),
                "Created",
                request.Ports.Count == 0 ? "-" : string.Join(", ", request.Ports.Select(port => $"{port.HostPort}->{port.ContainerPort}/{port.Protocol}")),
                request.Command.Count == 0 ? "(image default)" : string.Join(" ", request.Command),
                string.Empty);
        }
    }

    public async Task StartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        await _client.StartContainerAsync(containerId, cancellationToken);
    }

    public async Task StopAsync(string containerId, CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        await _client.StopContainerAsync(containerId, cancellationToken);
    }

    public async Task RestartAsync(string containerId, CancellationToken cancellationToken = default)
    {
        await StopAsync(containerId, cancellationToken);
        await StartAsync(containerId, cancellationToken);
    }

    public async Task DeleteAsync(string containerId, CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        await _client.RemoveContainerAsync(containerId, cancellationToken);
    }

    internal static AppModels.ContainerSummary MapContainer(Contracts.ContainerSummaryDto container)
    {
        return new AppModels.ContainerSummary(
            container.Id,
            container.Name,
            container.Image,
            MapState(container.State),
            container.CpuPercent,
            container.MemoryUsed,
            container.Created,
            container.Uptime,
            container.PortSummary,
            container.Command,
            container.InspectJson);
    }

    private static AppModels.ContainerRuntimeState MapState(Contracts.ContainerRuntimeState state)
    {
        return state switch
        {
            Contracts.ContainerRuntimeState.Created => AppModels.ContainerRuntimeState.Created,
            Contracts.ContainerRuntimeState.Running => AppModels.ContainerRuntimeState.Running,
            Contracts.ContainerRuntimeState.Stopped => AppModels.ContainerRuntimeState.Stopped,
            Contracts.ContainerRuntimeState.Exited => AppModels.ContainerRuntimeState.Exited,
            _ => AppModels.ContainerRuntimeState.Unknown
        };
    }
}

public sealed class DaemonWslcProcessService : IWslcProcessService
{
    private readonly IWslcdNativeClient _client;
    private readonly DaemonProcessManager _daemon;

    public DaemonWslcProcessService(IWslcdNativeClient client, DaemonProcessManager daemon)
    {
        _client = client;
        _daemon = daemon;
    }

    public async IAsyncEnumerable<AppModels.ContainerLogLine> StreamLogsAsync(
        string containerId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        await foreach (var frame in _client.StreamLogsAsync(
            containerId,
            new Contracts.LogStreamOptions(Tail: 200, Follow: false, Timestamps: true, Since: null, Until: null),
            cancellationToken))
        {
            yield return new AppModels.ContainerLogLine(frame.Timestamp, MapLogStream(frame.Stream), frame.Message);
        }
    }

    public async Task<AppModels.ProcessExecutionResult> ExecuteAsync(AppModels.ProcessExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Command.Count == 0)
        {
            throw new ArgumentException("Command is required.", nameof(request));
        }

        await _daemon.EnsureRunningAsync(cancellationToken);
        var exec = await _client.CreateExecAsync(request.ContainerId, new Contracts.ExecCreateRequestDto(
            request.Command,
            request.StandardInput,
            AttachStdout: true,
            AttachStderr: true,
            Tty: false), cancellationToken);

        var frames = await _client.StartExecAsync(exec.ExecId, new Contracts.ExecStartOptionsDto(Detach: false, Tty: false), cancellationToken);
        var detail = await _client.InspectExecAsync(exec.ExecId, cancellationToken);

        string stdout = string.Concat(frames.Where(frame => !frame.Stream.Equals("stderr", StringComparison.OrdinalIgnoreCase)).Select(frame => frame.Message));
        string stderr = string.Concat(frames.Where(frame => frame.Stream.Equals("stderr", StringComparison.OrdinalIgnoreCase)).Select(frame => frame.Message));
        return new AppModels.ProcessExecutionResult(detail.ExitCode ?? 0, stdout, stderr);
    }

    private static AppModels.LogStream MapLogStream(string stream)
    {
        return stream.Equals("stderr", StringComparison.OrdinalIgnoreCase)
            ? AppModels.LogStream.Stderr
            : AppModels.LogStream.Stdout;
    }
}

public sealed class DaemonWslcVolumeService : IWslcVolumeService
{
    private readonly IWslcdNativeClient _client;
    private readonly DaemonProcessManager _daemon;

    public DaemonWslcVolumeService(IWslcdNativeClient client, DaemonProcessManager daemon)
    {
        _client = client;
        _daemon = daemon;
    }

    public async Task<IReadOnlyList<AppModels.VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        var volumes = await _client.ListVolumesAsync(cancellationToken);
        return volumes.Select(MapVolume).ToArray();
    }

    public async Task CreateNamedVolumeAsync(AppModels.VolumeCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Volume name is required.", nameof(request));
        }

        await _daemon.EnsureRunningAsync(cancellationToken);
        await _client.CreateVolumeAsync(new Contracts.VolumeCreateRequestDto(request.Name.Trim()), cancellationToken);
    }

    public async Task DeleteNamedVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Volume name is required.", nameof(name));
        }

        await _daemon.EnsureRunningAsync(cancellationToken);
        await _client.RemoveVolumeAsync(name.Trim(), cancellationToken);
    }

    private static AppModels.VolumeSummary MapVolume(Contracts.VolumeSummaryDto volume)
    {
        return new AppModels.VolumeSummary(volume.Name, volume.Size, volume.UsedBy, volume.Created, volume.IsNamed);
    }
}

public sealed class DaemonWslcNetworkService : IWslcNetworkService
{
    private readonly IWslcdNativeClient _client;
    private readonly DaemonProcessManager _daemon;

    public DaemonWslcNetworkService(IWslcdNativeClient client, DaemonProcessManager daemon)
    {
        _client = client;
        _daemon = daemon;
    }

    public async Task<IReadOnlyList<AppModels.NetworkEndpointSummary>> ListPublishedPortsAsync(CancellationToken cancellationToken = default)
    {
        await _daemon.EnsureRunningAsync(cancellationToken);
        var endpoints = await _client.ListNetworksAsync(cancellationToken);
        return endpoints
            .Select(endpoint => new AppModels.NetworkEndpointSummary(endpoint.ContainerName, endpoint.HostPort, endpoint.ContainerPort, endpoint.Protocol, endpoint.Url))
            .OrderBy(endpoint => endpoint.HostPort)
            .ThenBy(endpoint => endpoint.ContainerName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class DaemonOperationTracker : IOperationTracker
{
    private readonly IWslcdNativeClient _client;
    private readonly DaemonProcessManager _daemon;
    private readonly List<AppModels.OperationRecord> _localOperations = [];
    private readonly object _syncRoot = new();

    public DaemonOperationTracker(IWslcdNativeClient client, DaemonProcessManager daemon)
    {
        _client = client;
        _daemon = daemon;
    }

    public async Task<IReadOnlyList<AppModels.OperationRecord>> ListRecentAsync(CancellationToken cancellationToken = default)
    {
        var operations = new List<AppModels.OperationRecord>();

        try
        {
            await _daemon.EnsureRunningAsync(cancellationToken);
            var daemonOperations = await _client.ListOperationsAsync(cancellationToken);
            operations.AddRange(daemonOperations.Select(MapOperation));
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or TimeoutException or HttpRequestException or IOException or WslcdClientException)
        {
            operations.Add(new AppModels.OperationRecord(
                Guid.NewGuid().ToString("N"),
                "wslcd unavailable",
                AppModels.OperationState.Failed,
                ex.Message,
                DateTimeOffset.Now));
        }

        lock (_syncRoot)
        {
            operations.AddRange(_localOperations);
        }

        return operations
            .OrderByDescending(operation => operation.StartedAt)
            .Take(50)
            .ToArray();
    }

    public void Track(AppModels.OperationRecord operation)
    {
        lock (_syncRoot)
        {
            _localOperations.Add(operation);
            if (_localOperations.Count > 50)
            {
                _localOperations.RemoveRange(0, _localOperations.Count - 50);
            }
        }
    }

    private static AppModels.OperationRecord MapOperation(Contracts.OperationRecordDto operation)
    {
        return new AppModels.OperationRecord(
            operation.OperationId,
            $"{operation.Action} {operation.ResourceType}".Trim(),
            MapState(operation.Status),
            string.IsNullOrWhiteSpace(operation.StderrTail) ? operation.StdoutTail : operation.StderrTail,
            operation.StartedAt);
    }

    private static AppModels.OperationState MapState(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "queued" => AppModels.OperationState.Pending,
            "running" => AppModels.OperationState.Running,
            "succeeded" => AppModels.OperationState.Succeeded,
            "failed" => AppModels.OperationState.Failed,
            "canceled" => AppModels.OperationState.Canceled,
            _ => AppModels.OperationState.Running
        };
    }
}
