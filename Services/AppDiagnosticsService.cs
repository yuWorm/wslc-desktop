using System.Text.Json;
using System.Text.Json.Serialization;

namespace wslc_desktop.Services;

public sealed class AppDiagnosticsService : IAppDiagnosticsService
{
    private readonly IWslcHostService _hostService;
    private readonly IAppSettingsService _settingsService;
    private readonly IOperationTracker _operationTracker;
    private readonly IComposePlanService _composePlanService;
    private readonly IWslcVolumeService _volumeService;
    private readonly IWslcNetworkService _networkService;
    private readonly IDaemonDiagnosticsService? _daemonDiagnosticsService;

    public AppDiagnosticsService(
        IWslcHostService hostService,
        IAppSettingsService settingsService,
        IOperationTracker operationTracker,
        IComposePlanService composePlanService,
        IWslcVolumeService volumeService,
        IWslcNetworkService networkService,
        IDaemonDiagnosticsService? daemonDiagnosticsService = null)
    {
        _hostService = hostService;
        _settingsService = settingsService;
        _operationTracker = operationTracker;
        _composePlanService = composePlanService;
        _volumeService = volumeService;
        _networkService = networkService;
        _daemonDiagnosticsService = daemonDiagnosticsService;
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        string directory = Path.Combine(settings.DataRoot, "Diagnostics");
        Directory.CreateDirectory(directory);

        var report = new Dictionary<string, object?>
        {
            ["generatedAt"] = DateTimeOffset.UtcNow,
            ["app"] = new
            {
                name = "WSLC Desktop",
                version = typeof(AppDiagnosticsService).Assembly.GetName().Version?.ToString() ?? "unknown"
            },
            ["settings"] = settings,
            ["engine"] = await CaptureAsync(() => _hostService.GetSummaryAsync(cancellationToken)),
            ["missingComponents"] = await CaptureAsync(() => _hostService.GetMissingComponentsAsync(cancellationToken)),
            ["operations"] = await CaptureAsync(() => _operationTracker.ListRecentAsync(cancellationToken)),
            ["composeProjects"] = await CaptureAsync(() => _composePlanService.ListProjectsAsync(cancellationToken)),
            ["volumes"] = await CaptureAsync(() => _volumeService.ListVolumesAsync(cancellationToken)),
            ["publishedPorts"] = await CaptureAsync(() => _networkService.ListPublishedPortsAsync(cancellationToken)),
            ["daemon"] = _daemonDiagnosticsService is null
                ? new { unavailable = "Daemon diagnostics service is not configured." }
                : await CaptureAsync(() => _daemonDiagnosticsService.CaptureAsync(cancellationToken)),
            ["compatibilityMatrix"] = CaptureCompatibilityMatrix()
        };

        string path = Path.Combine(directory, $"wslc-desktop-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, options), cancellationToken);
        return path;
    }

    private static async Task<object> CaptureAsync<T>(Func<Task<T>> capture)
    {
        try
        {
            object? result = await capture();
            return result ?? new { };
        }
        catch (Exception ex)
        {
            return new
            {
                error = ex.Message,
                type = ex.GetType().Name
            };
        }
    }

    private static object CaptureCompatibilityMatrix()
    {
        string? root = FindRepositoryRoot(AppContext.BaseDirectory);
        if (root is null)
        {
            return new { path = "docs/DOCKER_API_COMPATIBILITY_MATRIX.md", status = "unavailable in packaged layout" };
        }

        string path = Path.Combine(root, "docs", "DOCKER_API_COMPATIBILITY_MATRIX.md");
        if (!File.Exists(path))
        {
            return new { path, status = "missing" };
        }

        return new
        {
            path,
            status = "present",
            lastWriteUtc = File.GetLastWriteTimeUtc(path),
            supportedRows = File.ReadLines(path).Count(line => line.Contains("| Supported |", StringComparison.OrdinalIgnoreCase))
        };
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
