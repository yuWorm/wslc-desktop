using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using WslcDesktop.DaemonClient;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public sealed class DaemonProcessManager
{
    private readonly IWslcdNativeClient _client;
    private readonly FileAppSettingsService? _settingsService;
    private readonly object _syncRoot = new();
    private readonly object _outputSyncRoot = new();
    private Process? _process;
    private DaemonTarget? _currentTarget;
    private readonly Queue<string> _outputTail = new();
    private static readonly string[] DaemonProcessNames = ["wslcd-desktop", "wslcd"];

    public DaemonProcessManager(IWslcdNativeClient client, FileAppSettingsService? settingsService = null)
    {
        _client = client;
        _settingsService = settingsService;
    }

    public async Task EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        if (await CanConnectAsync(cancellationToken))
        {
            return;
        }

        StartDaemonIfNeeded();

        Exception? lastError = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"wslcd-desktop exited before it became ready. Exit code: {_process.ExitCode}. Target: {_currentTarget?.DisplayPath ?? "unknown"}. Output: {FormatOutputTail()}");
            }

            try
            {
                await _client.GetHealthAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException)
            {
                lastError = ex;
                await Task.Delay(250, cancellationToken);
            }
        }

        throw new TimeoutException("Timed out waiting for wslcd-desktop to become ready.", lastError);
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAllAsync(cancellationToken);
        await EnsureRunningAsync(cancellationToken);
    }

    public Task<DaemonStopResult> StopAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();
        int stoppedCount = StopOwnedProcess(warnings) ? 1 : 0;

        foreach (DaemonProcessInfo target in EnumerateDaemonProcesses(warnings))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using Process process = Process.GetProcessById(target.ProcessId);
                if (process.HasExited)
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                {
                    warnings.Add($"Timed out waiting for PID {target.ProcessId} ({target.Name}) to exit.");
                }

                stoppedCount++;
            }
            catch (ArgumentException)
            {
                // The process exited between enumeration and termination.
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                warnings.Add($"Failed to stop PID {target.ProcessId} ({target.Name}): {ex.Message}");
            }
        }

        return Task.FromResult(new DaemonStopResult(stoppedCount, warnings));
    }

    private async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetHealthAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException)
        {
            return false;
        }
    }

    private void StartDaemonIfNeeded()
    {
        lock (_syncRoot)
        {
            if (_process is { HasExited: false })
            {
                return;
            }

            Exception? lastError = null;
            foreach (var target in FindDaemonTargets())
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = target.FileName,
                        Arguments = target.Arguments,
                        WorkingDirectory = target.WorkingDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    ApplyRuntimeProviderEnvironment(startInfo);

                    _process = Process.Start(startInfo)
                        ?? throw new InvalidOperationException($"Failed to start wslcd-desktop candidate '{target.DisplayPath}'.");
                    _currentTarget = target;
                    ClearOutputTail();
                    _process.OutputDataReceived += (_, eventArgs) => AppendOutput("stdout", eventArgs.Data);
                    _process.ErrorDataReceived += (_, eventArgs) => AppendOutput("stderr", eventArgs.Data);
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();
                    return;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    lastError = ex;
                }
            }

            throw new InvalidOperationException("Failed to start any available wslcd-desktop candidate.", lastError);
        }
    }

    private bool StopOwnedProcess(ICollection<string>? warnings = null)
    {
        lock (_syncRoot)
        {
            if (_process is not { HasExited: false } process)
            {
                _process = null;
                return false;
            }

            bool stopped = false;
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                stopped = true;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                warnings?.Add($"Failed to stop owned daemon PID {process.Id}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
                _process = null;
                _currentTarget = null;
                ClearOutputTail();
            }

            return stopped;
        }
    }

    private void ClearOutputTail()
    {
        lock (_outputSyncRoot)
        {
            _outputTail.Clear();
        }
    }

    private void AppendOutput(string stream, string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        lock (_outputSyncRoot)
        {
            _outputTail.Enqueue($"{stream}: {data}");
            while (_outputTail.Count > 20)
            {
                _outputTail.Dequeue();
            }
        }
    }

    private string FormatOutputTail()
    {
        lock (_outputSyncRoot)
        {
            return _outputTail.Count == 0
                ? "<none>"
                : string.Join(" | ", _outputTail);
        }
    }

    private void ApplyRuntimeProviderEnvironment(ProcessStartInfo startInfo)
    {
        AppSettingsSnapshot settings = _settingsService?.LoadForStartup() ?? FileAppSettingsService.CreateDefault();
        startInfo.Environment["WSLCD_RUNTIME_PROVIDER"] = RuntimeProviderSelection.Normalize(settings.RuntimeProvider);
        startInfo.Environment["WSLCD_DOCKER_API_HOST"] = settings.DockerApiHost.Trim();
        startInfo.Environment["WSLCD_DOCKER_API_ALLOW_TCP"] = settings.AllowTcpDockerApi ? "1" : "0";
        startInfo.Environment["WSLCD_WSLC_HTTP_PROXY"] = settings.WslcHttpProxy.Trim();
        startInfo.Environment["WSLCD_WSLC_HTTPS_PROXY"] = settings.WslcHttpsProxy.Trim();
        startInfo.Environment["WSLCD_WSLC_NO_PROXY"] = settings.WslcNoProxy.Trim();
        startInfo.Environment["WSLCD_WSLC_IMAGE_MIRROR"] = settings.WslcImageMirror.Trim().TrimEnd('/');
        startInfo.Environment["WSLCD_WSLC_REWRITE_IMAGE_TAG"] = settings.WslcRewriteImageTag ? "1" : "0";
        startInfo.Environment["WSLCD_WSLC_REMOVE_REWRITTEN_SOURCE_TAG"] =
            settings.WslcRewriteImageTag && settings.WslcRemoveRewrittenSourceTag ? "1" : "0";
        startInfo.Environment["WSLCD_WSLC_ENVIRONMENT"] = settings.WslcEnvironment;
    }

    private static IReadOnlyList<DaemonTarget> FindDaemonTargets()
    {
        string baseDirectory = AppContext.BaseDirectory;
        var candidates = new List<DaemonTarget>();

        foreach (string appDirectory in EnumerateAppBaseDirectories(baseDirectory))
        {
            string packagedDirectory = Path.Combine(appDirectory, "wslcd");
            string packagedSelfContainedExe = Path.Combine(packagedDirectory, "wslcd-desktop.exe");
            candidates.Add(new DaemonTarget(packagedSelfContainedExe, string.Empty, packagedDirectory));

            string packagedSelfContainedDll = Path.Combine(packagedDirectory, "wslcd-desktop.dll");
            candidates.Add(new DaemonTarget("dotnet", Quote(packagedSelfContainedDll), packagedDirectory, packagedSelfContainedDll));

            string packagedExe = Path.Combine(appDirectory, "wslcd-desktop.exe");
            candidates.Add(new DaemonTarget(packagedExe, string.Empty, appDirectory));

            string packagedDll = Path.Combine(appDirectory, "wslcd-desktop.dll");
            candidates.Add(new DaemonTarget("dotnet", Quote(packagedDll), appDirectory, packagedDll));

            string legacyPackagedSelfContainedExe = Path.Combine(packagedDirectory, "wslcd.exe");
            candidates.Add(new DaemonTarget(legacyPackagedSelfContainedExe, string.Empty, packagedDirectory));

            string legacyPackagedSelfContainedDll = Path.Combine(packagedDirectory, "wslcd.dll");
            candidates.Add(new DaemonTarget("dotnet", Quote(legacyPackagedSelfContainedDll), packagedDirectory, legacyPackagedSelfContainedDll));
        }

        string? root = FindRepositoryRoot(baseDirectory);
        if (root is not null)
        {
            string developmentDll = Path.Combine(root, "src", "wslcd", "bin", "Debug", "net10.0", "wslcd-desktop.dll");
            candidates.Add(new DaemonTarget("dotnet", Quote(developmentDll), root, developmentDll));

            string publishedExe = Path.Combine(root, "artifacts", "wslcd-singlefile", "wslcd-desktop.exe");
            candidates.Add(new DaemonTarget(publishedExe, string.Empty, root));

            string legacyDevelopmentDll = Path.Combine(root, "src", "wslcd", "bin", "Debug", "net10.0", "wslcd.dll");
            candidates.Add(new DaemonTarget("dotnet", Quote(legacyDevelopmentDll), root, legacyDevelopmentDll));

            string legacyPublishedExe = Path.Combine(root, "artifacts", "wslcd-singlefile", "wslcd.exe");
            candidates.Add(new DaemonTarget(legacyPublishedExe, string.Empty, root));
        }

        var existing = candidates
            .Where(candidate => File.Exists(candidate.PathToCheck ?? candidate.FileName))
            .ToArray();

        return existing.Length > 0
            ? existing
            : throw new FileNotFoundException("Could not find wslcd-desktop.exe or wslcd-desktop.dll. Build src\\wslcd first or include the daemon with the app package.");
    }

    private static IReadOnlyList<DaemonProcessInfo> EnumerateDaemonProcesses(ICollection<string> warnings)
    {
        var targets = new Dictionary<int, DaemonProcessInfo>();
        int currentProcessId = Environment.ProcessId;

        foreach (string processName in DaemonProcessNames)
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (process.Id == currentProcessId || process.HasExited)
                    {
                        continue;
                    }

                    targets[process.Id] = new DaemonProcessInfo(process.Id, process.ProcessName, "process name match");
                }
            }
        }

        foreach (DaemonProcessInfo process in EnumerateDaemonProcessesFromCim(warnings))
        {
            if (process.ProcessId != currentProcessId)
            {
                targets[process.ProcessId] = process;
            }
        }

        return targets.Values
            .OrderBy(target => target.ProcessId)
            .ToArray();
    }

    private static IReadOnlyList<DaemonProcessInfo> EnumerateDaemonProcessesFromCim(ICollection<string> warnings)
    {
        string command = """
            $ErrorActionPreference = 'Stop'
            Get-CimInstance Win32_Process |
                Where-Object {
                    $_.Name -in @('wslcd-desktop.exe', 'wslcd.exe') -or
                    ([string]$_.CommandLine -match '(?i)(^|[\\/\s])(wslcd-desktop|wslcd)\.dll(\s|$)')
                } |
                ForEach-Object {
                    ([string]$_.ProcessId) + "`t" + ([string]$_.Name) + "`t" + (([string]$_.CommandLine) -replace "`r|`n", " ")
                }
            """;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                warnings.Add("Timed out inspecting daemon process command lines.");
                return [];
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (process.ExitCode != 0)
            {
                warnings.Add("Could not inspect daemon process command lines. Run the app elevated to catch dotnet-hosted daemon processes. " + stderr.Trim());
                return [];
            }

            return stdout.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseDaemonProcessInfo)
                .Where(info => info is not null)
                .Select(info => info!)
                .ToArray();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            warnings.Add("Could not inspect daemon process command lines. Run the app elevated to catch dotnet-hosted daemon processes. " + ex.Message);
            return [];
        }
    }

    private static DaemonProcessInfo? ParseDaemonProcessInfo(string line)
    {
        string[] parts = line.Split('\t', 3);
        if (parts.Length < 2 || !int.TryParse(parts[0], out int processId))
        {
            return null;
        }

        string name = parts[1];
        string reason = parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2])
            ? parts[2]
            : "CIM match";
        return new DaemonProcessInfo(processId, name, reason);
    }

    private static IEnumerable<string> EnumerateAppBaseDirectories(string baseDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (seen.Add(baseDirectory))
        {
            yield return baseDirectory;
        }

        string? parent = Directory.GetParent(baseDirectory)?.FullName;
        if (parent is not null && seen.Add(parent))
        {
            yield return parent;
        }
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

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record DaemonTarget(
        string FileName,
        string Arguments,
        string WorkingDirectory,
        string? PathToCheck = null)
    {
        public string DisplayPath => PathToCheck ?? FileName;
    }

    private sealed record DaemonProcessInfo(
        int ProcessId,
        string Name,
        string Reason);
}
