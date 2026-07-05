using System.Runtime.CompilerServices;
using WslcDesktop.Contracts;

namespace WslcDesktop.DaemonClient;

public sealed class MockWslcdNativeClient : IWslcdNativeClient
{
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
        StatsOneShot: false,
        StatsStreaming: false,
        ExecNonInteractive: true,
        ExecInteractiveTty: false,
        DockerNetworkCrud: false,
        ImagePull: true,
        ImagePullProgress: false,
        ImageRemove: true,
        ImagePush: false,
        ImageBuild: false);

    private readonly List<ContainerSummaryDto> _containers =
    [
        new ContainerSummaryDto(
            "mock-web",
            "mock-web",
            "nginx:latest",
            ContainerRuntimeState.Running,
            1.2,
            "64 MB",
            DateTimeOffset.Now.AddHours(-2).ToString("yyyy-MM-dd HH:mm"),
            "2h",
            "8080->80/tcp",
            "nginx -g daemon off;",
            "{}")
    ];

    private readonly List<ImageSummaryDto> _images =
    [
        new ImageSummaryDto("mock-nginx", "nginx", "latest", "72 MB", "mock", true)
    ];

    private readonly List<VolumeSummaryDto> _volumes =
    [
        new VolumeSummaryDto("mock-data", "128 MB", "mock-web", "mock", true)
    ];

    private readonly List<OperationRecordDto> _operations = [];

    public Task<WslcdHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new WslcdHealthResponse(
            "Mock",
            WslcdDefaults.DaemonName,
            WslcdDefaults.NativePipeName,
            WslcdDefaults.DockerPipeName,
            WslcdDefaults.DefaultRuntimeProviderName,
            WslcdDefaults.DefaultLogDirectory,
            WslcdDefaults.DefaultOperationRetentionCount,
            DateTimeOffset.Now));
    }

    public Task<WslcdVersionResponse> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new WslcdVersionResponse(
            WslcdDefaults.ProductName,
            WslcdDefaults.DaemonName,
            WslcdDefaults.NativeApiVersion,
            WslcdDefaults.DockerApiVersion,
            WslcdDefaults.DockerMinApiVersion,
            WslcdDefaults.DefaultRuntimeProviderName,
            WslcdDefaults.OperatingSystem,
            WslcdDefaults.Architecture,
            "mock"));
    }

    public Task<RuntimeInfoResponse> GetRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RuntimeInfoResponse(WslcdDefaults.DefaultRuntimeProviderName, true, "mock", Capabilities, "Mock daemon client."));
    }

    public Task<ProviderPreviewResponse> PreviewProviderAsync(ProviderPreviewRequest request, CancellationToken cancellationToken = default)
    {
        bool isDockerApi = request.RuntimeProviderName.Equals(RuntimeProviderSelection.DockerApi, StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(new ProviderPreviewResponse(
            isDockerApi ? RuntimeProviderSelection.DockerApi : RuntimeProviderSelection.WslcCli,
            isDockerApi ? request.DockerApiHost : "wslc.exe",
            !isDockerApi || !string.IsNullOrWhiteSpace(request.DockerApiHost),
            !isDockerApi,
            isDockerApi ? "Mock Docker API provider is not connected." : "Mock WSLC provider is available.",
            isDockerApi ? EmptyCapabilities() : Capabilities));
    }

    public Task<IReadOnlyList<ContainerSummaryDto>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ContainerSummaryDto>>(_containers.ToArray());
    }

    public Task<ContainerSummaryDto> InspectContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_containers.First());
    }

    public Task<ContainerCreateResultDto> CreateContainerAsync(ContainerCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        var container = new ContainerSummaryDto(
            string.IsNullOrWhiteSpace(request.Name) ? Guid.NewGuid().ToString("N")[..12] : request.Name,
            string.IsNullOrWhiteSpace(request.Name) ? request.Image : request.Name,
            request.Image,
            ContainerRuntimeState.Created,
            0,
            "-",
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"),
            "Created",
            request.Ports.Count == 0 ? "-" : string.Join(", ", request.Ports.Select(port => $"{port.HostPort}->{port.ContainerPort}/{port.Protocol}")),
            request.Command.Count == 0 ? "(image default)" : string.Join(" ", request.Command),
            "{}");
        _containers.Add(container);
        return Task.FromResult(new ContainerCreateResultDto(container.Id, []));
    }

    public Task StartContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StopContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemoveContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        _containers.RemoveAll(container => container.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || container.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ImageSummaryDto>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ImageSummaryDto>>(_images.ToArray());
    }

    public Task<IReadOnlyList<ImagePullProgressDto>> PullImageAsync(ImagePullRequest request, CancellationToken cancellationToken = default)
    {
        _images.Add(new ImageSummaryDto(request.Reference, request.Reference, "latest", "mock", "mock", false));
        return Task.FromResult<IReadOnlyList<ImagePullProgressDto>>(
        [
            ImagePullProgressDto.StatusFrame(request.Reference, "Starting"),
            ImagePullProgressDto.StatusFrame(request.Reference, "Completed")
        ]);
    }

    public async IAsyncEnumerable<ImagePullProgressDto> PullImageProgressAsync(
        ImagePullRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var frame in await PullImageAsync(request, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
        }
    }

    public Task RemoveImageAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        _images.RemoveAll(image => image.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || $"{image.Repository}:{image.Tag}".Equals(idOrName, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VolumeSummaryDto>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<VolumeSummaryDto>>(_volumes.ToArray());
    }

    public Task<VolumeSummaryDto> CreateVolumeAsync(VolumeCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        var volume = new VolumeSummaryDto(request.Name, "-", "-", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"), true);
        _volumes.Add(volume);
        return Task.FromResult(volume);
    }

    public Task RemoveVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        _volumes.RemoveAll(volume => volume.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NetworkEndpointSummaryDto>> ListNetworksAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<NetworkEndpointSummaryDto>>(
        [
            new NetworkEndpointSummaryDto("mock-web", 8080, 80, "tcp", "http://localhost:8080")
        ]);
    }

    public async IAsyncEnumerable<LogFrameDto> StreamLogsAsync(
        string idOrName,
        LogStreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new LogFrameDto(DateTimeOffset.Now, "stdout", "mock log line");
        await Task.CompletedTask;
    }

    public Task<ExecCreateResultDto> CreateExecAsync(string idOrName, ExecCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ExecCreateResultDto(Guid.NewGuid().ToString("N")));
    }

    public Task<IReadOnlyList<ExecFrameDto>> StartExecAsync(string execId, ExecStartOptionsDto options, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ExecFrameDto>>(
        [
            new ExecFrameDto(DateTimeOffset.Now, "stdout", "mock exec")
        ]);
    }

    public Task<ExecDetailDto> InspectExecAsync(string execId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ExecDetailDto(execId, Running: false, ExitCode: 0, Tty: false));
    }

    public Task<IReadOnlyList<OperationRecordDto>> ListOperationsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OperationRecordDto>>(_operations.ToArray());
    }

    public async Task<WslcdDiagnosticsResponse> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return new WslcdDiagnosticsResponse(
            await GetHealthAsync(cancellationToken),
            await GetVersionAsync(cancellationToken),
            await GetRuntimeAsync(cancellationToken),
            _operations.ToArray(),
            DateTimeOffset.Now);
    }

    private static RuntimeCapabilities EmptyCapabilities()
    {
        return new RuntimeCapabilities(
            ListContainers: false,
            InspectContainer: false,
            CreateContainer: false,
            StartStopContainer: false,
            RemoveContainer: false,
            NamedVolumes: false,
            BindMounts: false,
            PortPublishing: false,
            RestartPolicy: false,
            LogsBuffered: false,
            LogsFollow: false,
            StatsOneShot: false,
            StatsStreaming: false,
            ExecNonInteractive: false,
            ExecInteractiveTty: false,
            DockerNetworkCrud: false,
            ImagePull: false,
            ImagePullProgress: false,
            ImageRemove: false,
            ImagePush: false,
            ImageBuild: false);
    }
}
