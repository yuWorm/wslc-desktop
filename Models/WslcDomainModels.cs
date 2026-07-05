namespace wslc_desktop.Models;

public enum EngineReadinessState
{
    Unknown,
    Ready,
    MissingComponents,
    Unsupported,
    Error
}

public enum ContainerRuntimeState
{
    Created,
    Running,
    Stopped,
    Exited,
    Unknown
}

public enum OperationState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Canceled
}

public enum LogStream
{
    Stdout,
    Stderr
}

public enum MockHostScenario
{
    Ready,
    MissingComponents,
    Unsupported,
    Error
}

public enum StartupTaskAvailability
{
    Unknown,
    Available,
    DisabledByUser,
    DisabledByPolicy,
    Unavailable
}

public enum WslcPrerequisiteState
{
    Ready,
    MissingWsl,
    WslUpdateRequired
}

public static class RuntimeProviderSelection
{
    public const string WslcCli = "wslc-cli";
    public const string DockerApi = "docker-api";

    public static string Normalize(string? value)
    {
        return string.Equals(value, DockerApi, StringComparison.OrdinalIgnoreCase)
            ? DockerApi
            : WslcCli;
    }
}

public sealed record EngineSummary(
    EngineReadinessState State,
    string Version,
    string Message,
    int RunningContainers,
    int ImageCount,
    string StorageUsed,
    double CpuPercent,
    string MemoryUsed);

public sealed record ContainerSummary(
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
    string InspectJson = "");

public sealed record ContainerCreateRequest(
    string Name,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyList<PortMapping> Ports,
    IReadOnlyList<ContainerMount> Mounts,
    IReadOnlyDictionary<string, string> Environment,
    bool EnableGpu,
    bool AutoRemove);

public sealed record PortMapping(
    int HostPort,
    int ContainerPort,
    string Protocol);

public sealed record ContainerMount(
    string Source,
    string Target,
    bool IsReadOnly,
    bool IsNamedVolume);

public sealed record ImageSummary(
    string Id,
    string Repository,
    string Tag,
    string Size,
    string Created,
    bool IsInUse)
{
    public string UsageText => IsInUse
        ? Localized("In use", "使用中")
        : Localized("Unused", "未使用");

    private static string Localized(string english, string chinese)
    {
        return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
            ? chinese
            : english;
    }
}

public sealed record ImagePullRequest(string Reference);

public enum ImagePullProgressKind
{
    Status,
    Progress
}

public sealed record ImagePullProgress(
    string Id,
    string Status,
    ulong CurrentBytes,
    ulong TotalBytes,
    ImagePullProgressKind Kind = ImagePullProgressKind.Status)
{
    public bool HasByteProgress => Kind == ImagePullProgressKind.Progress && TotalBytes > 0;
}

public sealed record VolumeSummary(
    string Name,
    string Size,
    string UsedBy,
    string Created,
    bool IsNamed)
{
    public string Kind => IsNamed ? "Named" : "Bind";

    public string KindText => IsNamed ? "local" : "bind";
}

public sealed record VolumeCreateRequest(
    string Name,
    ulong SizeBytes);

public sealed record NetworkEndpointSummary(
    string ContainerName,
    int HostPort,
    int ContainerPort,
    string Protocol,
    string Url);

public sealed record ComposeProjectSummary(
    string Name,
    int ServiceCount,
    int RunningCount,
    string SourcePath)
{
    public string StatusText => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"
        ? $"{RunningCount} 个运行中 / {ServiceCount} 个服务"
        : $"{RunningCount} running / {ServiceCount} services";
}
public sealed record ComposeServicePlan(
    string ProjectName,
    string ServiceName,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyList<PortMapping> Ports,
    IReadOnlyList<ContainerMount> Mounts,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> UnsupportedKeys)
{
    public string CommandSummary => Command.Count == 0 ? "(image default)" : string.Join(" ", Command);

    public string PortSummary => Ports.Count == 0
        ? "-"
        : string.Join(", ", Ports.Select(port => $"{port.HostPort}:{port.ContainerPort}/{port.Protocol}"));

    public string MountSummary => Mounts.Count == 0
        ? "-"
        : string.Join(", ", Mounts.Select(mount => $"{mount.Source} -> {mount.Target}{(mount.IsReadOnly ? " (ro)" : string.Empty)}"));

    public string EnvironmentSummary => Environment.Count == 0
        ? "-"
        : $"{Environment.Count} variable{(Environment.Count == 1 ? string.Empty : "s")}";

    public string DependsOnSummary => DependsOn.Count == 0 ? "-" : string.Join(", ", DependsOn);

    public string UnsupportedSummary => UnsupportedKeys.Count == 0 ? "-" : string.Join(", ", UnsupportedKeys);
}

public sealed record OperationRecord(
    string Id,
    string Title,
    OperationState State,
    string Detail,
    DateTimeOffset StartedAt);

public sealed record ContainerLogLine(
    DateTimeOffset Timestamp,
    LogStream Stream,
    string Message);

public sealed record ProcessExecutionRequest(
    string ContainerId,
    IReadOnlyList<string> Command,
    string StandardInput);

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public sealed record ProviderPreviewSnapshot(
    string RuntimeProvider,
    string Host,
    bool IsConfigured,
    bool IsAvailable,
    string Message,
    string CapabilitySummary);

public sealed record StartupTaskSnapshot(
    bool IsEnabled,
    StartupTaskAvailability Availability,
    string Message);

public sealed record WslcPrerequisiteStatus(
    WslcPrerequisiteState State,
    bool IsReady,
    string RequiredCommand,
    string DetectedVersion,
    string Message);

public sealed record DockerCliStatus(
    bool DockerCliAvailable,
    string DockerCliPath,
    bool ComposeAvailable,
    string ComposePath,
    string Message)
{
    public bool IsReady => DockerCliAvailable;
}

public sealed record DockerStaticRelease(
    string FileName,
    Version Version,
    Uri DownloadUri);

public sealed record CliToolInstallResult(
    string ToolName,
    string BinDirectory,
    IReadOnlyList<string> InstalledFiles,
    string Message);

public sealed record DaemonDiagnosticsSnapshot(
    string PipeHealth,
    string RuntimeProvider,
    string CompatibilityMatrix,
    IReadOnlyList<string> LogFiles,
    string Message);

public sealed record DaemonStopResult(
    int StoppedCount,
    IReadOnlyList<string> Warnings);

public sealed record AppSettingsSnapshot(
    string DataRoot,
    int CpuCount,
    int MemoryMB,
    string DefaultShell,
    bool PreferExternalTerminal,
    string Language,
    string RuntimeProvider = RuntimeProviderSelection.WslcCli,
    string DockerApiHost = "",
    bool AllowTcpDockerApi = false,
    bool LaunchAtLogin = false,
    string WslcHttpProxy = "",
    string WslcHttpsProxy = "",
    string WslcNoProxy = "",
    string WslcImageMirror = "",
    bool WslcRewriteImageTag = false,
    bool WslcRemoveRewrittenSourceTag = false,
    string WslcEnvironment = "");

public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);

public sealed record ShellState(
    bool IsLoading,
    bool HasError,
    string Message);
