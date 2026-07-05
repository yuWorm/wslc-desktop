using System.IO.Compression;
using wslc_desktop.Models;
using wslc_desktop.Services;

var missingWsl = BootstrapPrerequisiteEvaluator.EvaluateWslc(
    wslExists: false,
    wslcExists: false,
    wslVersionOutput: string.Empty);
Expect(missingWsl.State == WslcPrerequisiteState.MissingWsl, "Missing WSL must be classified.");
Expect(missingWsl.RequiredCommand == "wsl --install", "Missing WSL must recommend wsl --install.");
Expect(!missingWsl.IsReady, "Missing WSL must block startup.");

var oldWsl = BootstrapPrerequisiteEvaluator.EvaluateWslc(
    wslExists: true,
    wslcExists: false,
    wslVersionOutput: "WSL version: 2.5.7");
Expect(oldWsl.State == WslcPrerequisiteState.WslUpdateRequired, "Old WSL must be classified when wslc is absent.");
Expect(oldWsl.RequiredCommand == "wsl --update", "Old WSL must recommend wsl --update.");
Expect(oldWsl.DetectedVersion == "WSL version: 2.5.7", "Old WSL status must preserve detected version output.");

var readyWslc = BootstrapPrerequisiteEvaluator.EvaluateWslc(
    wslExists: true,
    wslcExists: true,
    wslVersionOutput: "WSL version: 2.6.0");
Expect(readyWslc.IsReady, "wslc presence must unblock startup.");
Expect(readyWslc.State == WslcPrerequisiteState.Ready, "wslc presence must be ready.");

var latestDocker = DockerStaticReleaseIndex.FindLatestDockerZip("""
    <a href="docker-29.5.3.zip">docker-29.5.3.zip</a>
    <a href="docker-29.6.1.zip">docker-29.6.1.zip</a>
    <a href="docker-29.4.2-2.zip">docker-29.4.2-2.zip</a>
    """, new Uri("https://download.docker.com/win/static/stable/x86_64/"));
Expect(latestDocker.FileName == "docker-29.6.1.zip", "Docker release parser must choose the highest stable version.");
Expect(latestDocker.DownloadUri.ToString() == "https://download.docker.com/win/static/stable/x86_64/docker-29.6.1.zip", "Docker release parser must return an absolute URI.");

string root = Path.Combine(Path.GetTempPath(), "wslc-bootstrap-verify", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string zipPath = Path.Combine(root, "docker.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
        var docker = archive.CreateEntry("docker/docker.exe");
        await using (var stream = docker.Open())
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync("docker-cli");
        }

        var dockerd = archive.CreateEntry("docker/dockerd.exe");
        await using (var stream = dockerd.Open())
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync("docker-daemon");
        }
    }

    string bin = Path.Combine(root, "bin");
    CliToolInstallResult dockerResult = await CliToolArchiveInstaller.InstallDockerCliFromZipAsync(zipPath, bin);
    Expect(File.Exists(Path.Combine(bin, "docker.exe")), "Docker CLI installer must copy docker.exe.");
    Expect(!File.Exists(Path.Combine(bin, "dockerd.exe")), "Docker CLI installer must not copy dockerd.exe.");
    Expect(dockerResult.InstalledFiles.SequenceEqual([Path.Combine(bin, "docker.exe")]), "Docker CLI installer must report only docker.exe.");

    string composeSource = Path.Combine(root, "compose-source.exe");
    await File.WriteAllTextAsync(composeSource, "compose");
    CliToolInstallResult composeResult = await CliToolArchiveInstaller.InstallComposeFromExeAsync(composeSource, bin);
    Expect(File.Exists(Path.Combine(bin, "docker-compose.exe")), "Compose installer must install standalone docker-compose.exe.");
    Expect(File.Exists(Path.Combine(bin, "cli-plugins", "docker-compose.exe")), "Compose installer must install Docker CLI plugin docker-compose.exe.");
    Expect(composeResult.InstalledFiles.Count == 2, "Compose installer must report both installed compose files.");

    string combinedPath = PathEnvironmentEditor.AddPathSegment(@"C:\Tools;C:\Existing", @"C:\Tools");
    Expect(combinedPath == @"C:\Tools;C:\Existing", "PATH editor must not duplicate existing path segments.");
    string appendedPath = PathEnvironmentEditor.AddPathSegment(@"C:\Existing", @"C:\Tools");
    Expect(appendedPath == @"C:\Existing;C:\Tools", "PATH editor must append missing path segments.");

    var contextProbe = new RecordingCommandProbe([
        new CommandProbeResult(true, 1, string.Empty, "context not found", "docker.exe"),
        new CommandProbeResult(true, 0, "created", string.Empty, "docker.exe"),
        new CommandProbeResult(true, 0, "wslc-desktop", string.Empty, "docker.exe")
    ]);
    var contextService = new DockerContextService(contextProbe);
    DockerContextResult contextResult = await contextService.CreateAndUseWslcDesktopContextAsync();
    Expect(contextResult.ContextName == "wslc-desktop", "Docker context service must target the wslc-desktop context.");
    Expect(contextProbe.Commands.Count == 3, "Docker context service must inspect, create, and use the context when it is missing.");
    Expect(contextProbe.Commands[0].Arguments.Contains("context inspect wslc-desktop", StringComparison.Ordinal), "Docker context service must inspect the target context first.");
    Expect(contextProbe.Commands[1].Arguments.Contains("context create wslc-desktop", StringComparison.Ordinal), "Docker context service must create a missing context.");
    Expect(contextProbe.Commands[1].Arguments.Contains("host=npipe:////./pipe/wslc-desktop-docker", StringComparison.Ordinal), "Docker context service must point at the WSLC Desktop named pipe.");
    Expect(contextProbe.Commands[2].Arguments.Contains("context use wslc-desktop", StringComparison.Ordinal), "Docker context service must set the context as default.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

Console.WriteLine("BOOTSTRAP_VERIFY_OK");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class RecordingCommandProbe : ICommandProbe
{
    private readonly Queue<CommandProbeResult> _results;

    public RecordingCommandProbe(IEnumerable<CommandProbeResult> results)
    {
        _results = new Queue<CommandProbeResult>(results);
    }

    public List<(string FileName, string Arguments)> Commands { get; } = [];

    public Task<CommandProbeResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        Commands.Add((fileName, arguments));
        return Task.FromResult(_results.Count == 0
            ? new CommandProbeResult(true, 0, string.Empty, string.Empty, fileName)
            : _results.Dequeue());
    }

    public string FindExecutable(string fileName)
    {
        return fileName;
    }
}
