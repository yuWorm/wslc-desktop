using WslcDesktop.Contracts;

namespace WslcDesktop.Runtime;

public interface IContainerRuntimeProvider
{
    string Name { get; }

    Task<RuntimeInfoResponse> GetRuntimeInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContainerSummaryDto>> ListContainersAsync(CancellationToken cancellationToken = default);

    Task<ContainerSummaryDto> InspectContainerAsync(string idOrName, CancellationToken cancellationToken = default);

    Task<ContainerCreateResultDto> CreateContainerAsync(ContainerCreateRequestDto request, CancellationToken cancellationToken = default);

    Task StartContainerAsync(string idOrName, CancellationToken cancellationToken = default);

    Task StopContainerAsync(string idOrName, ContainerStopOptionsDto options, CancellationToken cancellationToken = default);

    Task RemoveContainerAsync(string idOrName, ContainerRemoveOptionsDto options, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageSummaryDto>> ListImagesAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<ImagePullProgressDto> PullImageProgressAsync(string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImagePullProgressDto>> PullImageAsync(string reference, CancellationToken cancellationToken = default);

    Task RemoveImageAsync(string idOrName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VolumeSummaryDto>> ListVolumesAsync(CancellationToken cancellationToken = default);

    Task<VolumeSummaryDto> CreateVolumeAsync(string name, IReadOnlyDictionary<string, string>? labels = null, CancellationToken cancellationToken = default);

    Task RemoveVolumeAsync(string name, bool force, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NetworkEndpointSummaryDto>> ListNetworksAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<LogFrameDto> StreamLogsAsync(string idOrName, LogStreamOptions options, CancellationToken cancellationToken = default);

    IAsyncEnumerable<StatsFrameDto> StreamStatsAsync(string idOrName, StatsStreamOptions options, CancellationToken cancellationToken = default);

    Task<ExecCreateResultDto> CreateExecAsync(string idOrName, ExecCreateRequestDto request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ExecFrameDto> StartExecAsync(string execId, ExecStartOptionsDto options, CancellationToken cancellationToken = default);

    Task<ExecDetailDto> InspectExecAsync(string execId, CancellationToken cancellationToken = default);
}
