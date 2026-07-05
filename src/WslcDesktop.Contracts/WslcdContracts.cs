namespace WslcDesktop.Contracts;

public sealed record WslcdOptions(
    string NativePipeName,
    string DockerPipeName,
    string DockerContextName,
    string LogDirectory,
    int OperationRetentionCount,
    string RuntimeProviderName,
    string DockerApiHost,
    bool AllowTcpDockerApi,
    string WslcHttpProxy,
    string WslcHttpsProxy,
    string WslcNoProxy,
    string WslcImageMirror,
    bool WslcRewriteImageTag,
    bool WslcRemoveRewrittenSourceTag,
    string WslcEnvironment)
{
    public static WslcdOptions CreateDefault()
    {
        return new WslcdOptions(
            WslcdDefaults.NativePipeName,
            WslcdDefaults.DockerPipeName,
            "wslc-desktop",
            WslcdDefaults.DefaultLogDirectory,
            WslcdDefaults.DefaultOperationRetentionCount,
            WslcdDefaults.DefaultRuntimeProviderName,
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            false,
            string.Empty);
    }

    public static WslcdOptions CreateFromEnvironment()
    {
        var defaults = CreateDefault();
        return new WslcdOptions(
            GetString("WSLCD_NATIVE_PIPE_NAME", defaults.NativePipeName),
            GetString("WSLCD_DOCKER_PIPE_NAME", defaults.DockerPipeName),
            GetString("WSLCD_DOCKER_CONTEXT_NAME", defaults.DockerContextName),
            GetString("WSLCD_LOG_DIRECTORY", defaults.LogDirectory),
            GetInt("WSLCD_OPERATION_RETENTION_COUNT", defaults.OperationRetentionCount),
            GetString("WSLCD_RUNTIME_PROVIDER", defaults.RuntimeProviderName),
            GetString("WSLCD_DOCKER_API_HOST", defaults.DockerApiHost),
            GetBool("WSLCD_DOCKER_API_ALLOW_TCP", defaults.AllowTcpDockerApi),
            GetString("WSLCD_WSLC_HTTP_PROXY", defaults.WslcHttpProxy),
            GetString("WSLCD_WSLC_HTTPS_PROXY", defaults.WslcHttpsProxy),
            GetString("WSLCD_WSLC_NO_PROXY", defaults.WslcNoProxy),
            GetString("WSLCD_WSLC_IMAGE_MIRROR", defaults.WslcImageMirror).TrimEnd('/'),
            GetBool("WSLCD_WSLC_REWRITE_IMAGE_TAG", defaults.WslcRewriteImageTag),
            GetBool("WSLCD_WSLC_REMOVE_REWRITTEN_SOURCE_TAG", defaults.WslcRemoveRewrittenSourceTag),
            GetString("WSLCD_WSLC_ENVIRONMENT", defaults.WslcEnvironment));
    }

    private static string GetString(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int GetInt(string name, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
    }

    private static bool GetBool(string name, bool fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}

public static class RuntimeProviderSelection
{
    public const string WslcCli = "wslc-cli";
    public const string DockerApi = "docker-api";
}

public sealed record ProviderPreviewRequest(
    string RuntimeProviderName,
    string DockerApiHost,
    bool AllowTcpDockerApi);

public sealed record ProviderPreviewResponse(
    string RuntimeProviderName,
    string Host,
    bool IsConfigured,
    bool IsAvailable,
    string Message,
    RuntimeCapabilities Capabilities);

public sealed record WslcdHealthResponse(
    string Status,
    string DaemonName,
    string NativePipeName,
    string DockerPipeName,
    string RuntimeProviderName,
    string LogDirectory,
    int OperationRetentionCount,
    DateTimeOffset StartedAt);

public sealed record WslcdVersionResponse(
    string ProductName,
    string DaemonName,
    string NativeApiVersion,
    string DockerApiVersion,
    string DockerMinApiVersion,
    string RuntimeProviderName,
    string Os,
    string Architecture,
    string Version);

public sealed record WslcdDiagnosticsResponse(
    WslcdHealthResponse Health,
    WslcdVersionResponse Version,
    RuntimeInfoResponse Runtime,
    IReadOnlyList<OperationRecordDto> Operations,
    DateTimeOffset GeneratedAt);
