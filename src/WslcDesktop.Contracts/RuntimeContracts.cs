namespace WslcDesktop.Contracts;

public enum ContainerRuntimeState
{
    Created,
    Running,
    Stopped,
    Exited,
    Unknown
}

public sealed record RuntimeCapabilities(
    bool ListContainers,
    bool InspectContainer,
    bool CreateContainer,
    bool StartStopContainer,
    bool RemoveContainer,
    bool NamedVolumes,
    bool BindMounts,
    bool PortPublishing,
    bool RestartPolicy,
    bool LogsBuffered,
    bool LogsFollow,
    bool StatsOneShot,
    bool StatsStreaming,
    bool ExecNonInteractive,
    bool ExecInteractiveTty,
    bool DockerNetworkCrud,
    bool ImagePull,
    bool ImagePullProgress,
    bool ImageRemove,
    bool ImagePush,
    bool ImageBuild);

public sealed record RuntimeInfoResponse(
    string ProviderName,
    bool IsAvailable,
    string Version,
    RuntimeCapabilities Capabilities,
    string Message);

public sealed record ContainerSummaryDto(
    string Id,
    string Name,
    string Image,
    ContainerRuntimeState State,
    double CpuPercent,
    string MemoryUsed,
    string Created,
    string Uptime,
    string PortSummary,
    string Command,
    string InspectJson = "",
    IReadOnlyDictionary<string, string>? Labels = null);

public sealed record ContainerCreateRequestDto(
    string Name,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyList<PortMappingDto> Ports,
    IReadOnlyList<ContainerMountDto> Mounts,
    IReadOnlyDictionary<string, string> Environment,
    bool AutoRemove,
    IReadOnlyDictionary<string, string>? Labels = null);

public sealed record ContainerCreateResultDto(
    string Id,
    IReadOnlyList<string> Warnings);

public sealed record PortMappingDto(
    int HostPort,
    int ContainerPort,
    string Protocol);

public sealed record ContainerMountDto(
    string Source,
    string Target,
    bool IsReadOnly,
    bool IsNamedVolume);

public sealed record ContainerStopOptionsDto(int TimeoutSeconds);

public sealed record ContainerRemoveOptionsDto(
    bool Force,
    bool RemoveVolumes);

public sealed record ImageSummaryDto(
    string Id,
    string Repository,
    string Tag,
    string Size,
    string Created,
    bool IsInUse);

public sealed record ImagePullRequest(string Reference);

public enum ImagePullProgressKind
{
    Status,
    Progress
}

public sealed record ImagePullProgressDto(
    string Id,
    string Status,
    ulong CurrentBytes,
    ulong TotalBytes,
    ImagePullProgressKind Kind = ImagePullProgressKind.Status)
{
    public bool HasByteProgress => Kind == ImagePullProgressKind.Progress && TotalBytes > 0;

    public static ImagePullProgressDto StatusFrame(string id, string status)
    {
        return new ImagePullProgressDto(id, status, 0, 0, ImagePullProgressKind.Status);
    }

    public static ImagePullProgressDto ProgressFrame(string id, string status, ulong currentBytes, ulong totalBytes)
    {
        return new ImagePullProgressDto(id, status, currentBytes, totalBytes, ImagePullProgressKind.Progress);
    }
}

public sealed record VolumeSummaryDto(
    string Name,
    string Size,
    string UsedBy,
    string Created,
    bool IsNamed,
    IReadOnlyDictionary<string, string>? Labels = null);

public sealed record VolumeCreateRequestDto(
    string Name,
    IReadOnlyDictionary<string, string>? Labels = null);

public sealed record NetworkEndpointSummaryDto(
    string ContainerName,
    int HostPort,
    int ContainerPort,
    string Protocol,
    string Url);

public sealed record OperationRecordDto(
    string OperationId,
    string Provider,
    string ResourceType,
    string ResourceId,
    string Action,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    string StdoutTail,
    string StderrTail);

public sealed record NativeErrorResponse(
    string Code,
    string Message,
    string Provider,
    string Command,
    int? ExitCode,
    string StdoutTail,
    string StderrTail);

public sealed record LogStreamOptions(
    int Tail,
    bool Follow,
    bool Timestamps,
    DateTimeOffset? Since,
    DateTimeOffset? Until);

public sealed record LogFrameDto(
    DateTimeOffset Timestamp,
    string Stream,
    string Message);

public sealed record StatsStreamOptions(
    bool Stream);

public sealed record StatsFrameDto(
    string ContainerId,
    string ContainerName,
    double CpuPercent,
    string MemoryUsed,
    DateTimeOffset Timestamp);

public sealed record ExecCreateRequestDto(
    IReadOnlyList<string> Command,
    string StandardInput,
    bool AttachStdout,
    bool AttachStderr,
    bool Tty);

public sealed record ExecCreateResultDto(string ExecId);

public sealed record ExecStartOptionsDto(bool Detach, bool Tty);

public sealed record ExecFrameDto(
    DateTimeOffset Timestamp,
    string Stream,
    string Message);

public sealed record ExecDetailDto(
    string ExecId,
    bool Running,
    int? ExitCode,
    bool Tty);
