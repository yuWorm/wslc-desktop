using wslc_desktop.Models;

namespace wslc_desktop.Services;

public interface IWslcHostService
{
    Task<EngineSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetMissingComponentsAsync(CancellationToken cancellationToken = default);
}

public interface IMockWslcScenarioService
{
    MockHostScenario HostScenario { get; set; }
}

public interface IWslcImageService
{
    Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<ImagePullProgress> PullImageAsync(ImagePullRequest request, CancellationToken cancellationToken = default);

    Task DeleteImageAsync(string imageReference, CancellationToken cancellationToken = default);
}

public interface IWslcContainerService
{
    Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default);

    Task<ContainerSummary> CreateAsync(ContainerCreateRequest request, CancellationToken cancellationToken = default);

    Task StartAsync(string containerId, CancellationToken cancellationToken = default);

    Task StopAsync(string containerId, CancellationToken cancellationToken = default);

    Task RestartAsync(string containerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string containerId, CancellationToken cancellationToken = default);
}

public interface IWslcProcessService
{
    IAsyncEnumerable<ContainerLogLine> StreamLogsAsync(string containerId, CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default);
}

public interface IWslcTerminalService
{
    Task<ITerminalSession> ConnectAsync(TerminalConnectRequest request, CancellationToken cancellationToken = default);

    Task OpenExternalAsync(TerminalConnectRequest request, CancellationToken cancellationToken = default);
}

public interface ITerminalSession : IAsyncDisposable
{
    event EventHandler<TerminalOutputEvent>? OutputReceived;

    event EventHandler<TerminalSessionState>? StateChanged;

    string ContainerId { get; }

    string Shell { get; }

    TerminalSessionState State { get; }

    Task SendInputAsync(string input, CancellationToken cancellationToken = default);

    Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public interface IWslcVolumeService
{
    Task<IReadOnlyList<VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default);

    Task CreateNamedVolumeAsync(VolumeCreateRequest request, CancellationToken cancellationToken = default);

    Task DeleteNamedVolumeAsync(string name, CancellationToken cancellationToken = default);
}

public interface IWslcNetworkService
{
    Task<IReadOnlyList<NetworkEndpointSummary>> ListPublishedPortsAsync(CancellationToken cancellationToken = default);
}

public interface IComposePlanService
{
    Task<IReadOnlyList<ComposeProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComposeServicePlan>> PreviewAsync(string composePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContainerSummary>> CreateAndStartAsync(string composePath, CancellationToken cancellationToken = default);
}

public interface IWslcCommandFallback
{
    Task<CommandResult> RunAsync(string arguments, CancellationToken cancellationToken = default);
}

public interface IAppSettingsService
{
    Task<AppSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettingsSnapshot settings, CancellationToken cancellationToken = default);
}

public interface IRuntimeProviderPreviewService
{
    Task<ProviderPreviewSnapshot> PreviewAsync(
        string runtimeProvider,
        string dockerApiHost,
        bool allowTcpDockerApi,
        CancellationToken cancellationToken = default);
}

public interface IStartupTaskService
{
    Task<StartupTaskSnapshot> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<StartupTaskSnapshot> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

public interface IDaemonDiagnosticsService
{
    Task<DaemonDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default);

    Task<DaemonDiagnosticsSnapshot> CaptureAsync(bool startIfNeeded, CancellationToken cancellationToken = default);
}

public interface IDaemonControlService
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task RestartAsync(CancellationToken cancellationToken = default);

    Task<DaemonStopResult> StopAllAsync(CancellationToken cancellationToken = default);
}

public interface IAppDiagnosticsService
{
    Task<string> ExportAsync(CancellationToken cancellationToken = default);
}

public interface IOperationTracker
{
    Task<IReadOnlyList<OperationRecord>> ListRecentAsync(CancellationToken cancellationToken = default);

    void Track(OperationRecord operation);
}
