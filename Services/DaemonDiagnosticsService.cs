using WslcDesktop.Contracts;
using WslcDesktop.DaemonClient;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public sealed class DaemonDiagnosticsService : IDaemonDiagnosticsService
{
    private readonly IWslcdNativeClient _client;
    private readonly DaemonProcessManager _daemon;
    private readonly FileAppSettingsService _settingsService;

    public DaemonDiagnosticsService(
        IWslcdNativeClient client,
        DaemonProcessManager daemon,
        FileAppSettingsService settingsService)
    {
        _client = client;
        _daemon = daemon;
        _settingsService = settingsService;
    }

    public async Task<DaemonDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        return await CaptureAsync(startIfNeeded: true, cancellationToken);
    }

    public async Task<DaemonDiagnosticsSnapshot> CaptureAsync(bool startIfNeeded, CancellationToken cancellationToken = default)
    {
        string pipeHealth;
        string runtimeProvider = "-";
        string message = string.Empty;

        try
        {
            if (startIfNeeded)
            {
                await _daemon.EnsureRunningAsync(cancellationToken);
            }
            else
            {
                await _client.GetHealthAsync(cancellationToken);
            }

            WslcdDiagnosticsResponse diagnostics = await _client.GetDiagnosticsAsync(cancellationToken);
            pipeHealth = "native pipe reachable";
            runtimeProvider = diagnostics.Runtime.ProviderName;
            message = diagnostics.Runtime.Message;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or TimeoutException or HttpRequestException or IOException or WslcdClientException)
        {
            pipeHealth = "native pipe unavailable";
            message = ex.Message;
        }

        return new DaemonDiagnosticsSnapshot(
            pipeHealth,
            runtimeProvider,
            CaptureCompatibilityMatrix(),
            CaptureLogFiles(),
            message);
    }

    private IReadOnlyList<string> CaptureLogFiles()
    {
        var settings = _settingsService.LoadForStartup();
        string directory = Path.Combine(settings.DataRoot, "Diagnostics");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "wslcd-desktop-*.log")
            .Concat(Directory.EnumerateFiles(directory, "wslcd-*.log"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(10)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    private static string CaptureCompatibilityMatrix()
    {
        string? root = FindRepositoryRoot(AppContext.BaseDirectory);
        if (root is null)
        {
            return "docs/DOCKER_API_COMPATIBILITY_MATRIX.md unavailable in packaged layout";
        }

        string compatibilityMatrix = Path.Combine(root, "docs", "DOCKER_API_COMPATIBILITY_MATRIX.md");
        if (!File.Exists(compatibilityMatrix))
        {
            return "docs/DOCKER_API_COMPATIBILITY_MATRIX.md missing";
        }

        DateTime lastWriteUtc = File.GetLastWriteTimeUtc(compatibilityMatrix);
        int supportedRows = File.ReadLines(compatibilityMatrix)
            .Count(line => line.Contains("| Supported |", StringComparison.OrdinalIgnoreCase));
        return $"docs/DOCKER_API_COMPATIBILITY_MATRIX.md lastWriteUtc={lastWriteUtc:o} supportedRows={supportedRows}";
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "wslc-desktop.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

public sealed class DaemonControlService : IDaemonControlService
{
    private readonly DaemonProcessManager _daemon;

    public DaemonControlService(DaemonProcessManager daemon)
    {
        _daemon = daemon;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _daemon.EnsureRunningAsync(cancellationToken);
    }

    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        return _daemon.RestartAsync(cancellationToken);
    }

    public Task<DaemonStopResult> StopAllAsync(CancellationToken cancellationToken = default)
    {
        return _daemon.StopAllAsync(cancellationToken);
    }
}
