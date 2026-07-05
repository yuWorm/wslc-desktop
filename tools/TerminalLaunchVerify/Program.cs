using wslc_desktop.Models;
using wslc_desktop.Services;

var request = new TerminalConnectRequest("abc123", "demo-web", ["/bin/bash", "/bin/sh"]);

var wslcLauncher = new RecordingLauncher("wt.exe");
var wslcService = new WslcCliTerminalService(
    new StaticSettings(DefaultShell: "/bin/sh", RuntimeProvider: RuntimeProviderSelection.WslcCli, DockerApiHost: string.Empty),
    wslcLauncher);

await wslcService.OpenExternalAsync(request);
Expect(wslcLauncher.Attempts.Count == 1, "WSLC launch should use the first available terminal.");
Expect(wslcLauncher.Attempts[0].FileName == "wt.exe", "WSLC launch should prefer Windows Terminal.");
ExpectSequence(
    wslcLauncher.Attempts[0].Arguments,
    ["new-tab", "--title", "demo-web", "wslc.exe", "exec", "-it", "abc123", "/bin/sh"],
    "Windows Terminal should directly launch wslc exec with argument-list semantics.");
ExpectNoShellSeparators(wslcLauncher.Attempts[0].Arguments, "WSLC Windows Terminal arguments must not contain shell fallback separators.");

var fallbackLauncher = new RecordingLauncher("cmd.exe");
var fallbackService = new WslcCliTerminalService(
    new StaticSettings(DefaultShell: "/bin/sh", RuntimeProvider: RuntimeProviderSelection.WslcCli, DockerApiHost: string.Empty),
    fallbackLauncher);

await fallbackService.OpenExternalAsync(request);
Expect(fallbackLauncher.Attempts.Count == 2, "CMD fallback should run after Windows Terminal is unavailable.");
Expect(fallbackLauncher.Attempts[1].FileName == "cmd.exe", "Fallback launch should use cmd.exe.");
ExpectSequence(
    fallbackLauncher.Attempts[1].Arguments,
    ["/k", "wslc.exe", "exec", "-it", "abc123", "/bin/sh"],
    "CMD fallback should run wslc exec directly without a fallback script.");
ExpectNoShellSeparators(fallbackLauncher.Attempts[1].Arguments, "WSLC CMD fallback arguments must not contain shell fallback separators.");

var dockerLauncher = new RecordingLauncher("wt.exe");
var dockerService = new WslcCliTerminalService(
    new StaticSettings(DefaultShell: "/bin/sh", RuntimeProvider: RuntimeProviderSelection.DockerApi, DockerApiHost: "npipe:////./pipe/docker_engine"),
    dockerLauncher);

await dockerService.OpenExternalAsync(request);
ExpectSequence(
    dockerLauncher.Attempts[0].Arguments,
    ["new-tab", "--title", "demo-web", "docker.exe", "-H", "npipe:////./pipe/docker_engine", "exec", "-it", "abc123", "/bin/sh"],
    "Docker provider launch should directly run docker exec against the configured host.");

Console.WriteLine("TERMINAL_LAUNCH_VERIFY_OK");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void ExpectSequence(IReadOnlyList<string> actual, IReadOnlyList<string> expected, string message)
{
    if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
    {
        throw new InvalidOperationException($"{message} Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }
}

static void ExpectNoShellSeparators(IReadOnlyList<string> arguments, string message)
{
    if (arguments.Any(argument => argument.Contains(';', StringComparison.Ordinal) || argument.Contains(" fi", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException(message);
    }

    if (arguments.Contains("cmd.exe", StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Windows Terminal launch must not wrap the exec command in cmd.exe.");
    }
}

sealed class StaticSettings : IAppSettingsService
{
    private readonly AppSettingsSnapshot _snapshot;

    public StaticSettings(string DefaultShell, string RuntimeProvider, string DockerApiHost)
    {
        _snapshot = new AppSettingsSnapshot(
            DataRoot: string.Empty,
            CpuCount: 1,
            MemoryMB: 1024,
            DefaultShell,
            PreferExternalTerminal: true,
            Language: "system",
            RuntimeProvider,
            DockerApiHost,
            AllowTcpDockerApi: false,
            LaunchAtLogin: false);
    }

    public Task<AppSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_snapshot);

    public Task SaveAsync(AppSettingsSnapshot settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class RecordingLauncher : ITerminalProcessLauncher
{
    private readonly string _successFileName;

    public RecordingLauncher(string successFileName)
    {
        _successFileName = successFileName;
    }

    public List<(string FileName, IReadOnlyList<string> Arguments)> Attempts { get; } = [];

    public bool TryStart(string fileName, IReadOnlyList<string> arguments)
    {
        Attempts.Add((fileName, arguments.ToArray()));
        return fileName.Equals(_successFileName, StringComparison.OrdinalIgnoreCase);
    }
}
