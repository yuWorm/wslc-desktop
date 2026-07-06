using WslcDesktop.Contracts;

namespace WslcDesktop.DaemonClient;

public interface IWslcdNativeClient
{
    Task<WslcdHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<WslcdVersionResponse> GetVersionAsync(CancellationToken cancellationToken = default);

    Task<RuntimeInfoResponse> GetRuntimeAsync(CancellationToken cancellationToken = default);

    Task<ProviderPreviewResponse> PreviewProviderAsync(ProviderPreviewRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContainerSummaryDto>> ListContainersAsync(CancellationToken cancellationToken = default);

    Task<ContainerSummaryDto> InspectContainerAsync(string idOrName, CancellationToken cancellationToken = default);

    Task<ContainerCreateResultDto> CreateContainerAsync(ContainerCreateRequestDto request, CancellationToken cancellationToken = default);

    Task StartContainerAsync(string idOrName, CancellationToken cancellationToken = default);

    Task StopContainerAsync(string idOrName, CancellationToken cancellationToken = default);

    Task RemoveContainerAsync(string idOrName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageSummaryDto>> ListImagesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImagePullTaskDto>> ListImagePullTasksAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<ImagePullProgressDto> PullImageProgressAsync(ImagePullRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImagePullProgressDto>> PullImageAsync(ImagePullRequest request, CancellationToken cancellationToken = default);

    Task RemoveImageAsync(string idOrName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VolumeSummaryDto>> ListVolumesAsync(CancellationToken cancellationToken = default);

    Task<VolumeSummaryDto> CreateVolumeAsync(VolumeCreateRequestDto request, CancellationToken cancellationToken = default);

    Task RemoveVolumeAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NetworkEndpointSummaryDto>> ListNetworksAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<LogFrameDto> StreamLogsAsync(string idOrName, LogStreamOptions options, CancellationToken cancellationToken = default);

    Task<ExecCreateResultDto> CreateExecAsync(string idOrName, ExecCreateRequestDto request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecFrameDto>> StartExecAsync(string execId, ExecStartOptionsDto options, CancellationToken cancellationToken = default);

    Task<ExecDetailDto> InspectExecAsync(string execId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationRecordDto>> ListOperationsAsync(CancellationToken cancellationToken = default);

    Task<WslcdDiagnosticsResponse> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}
