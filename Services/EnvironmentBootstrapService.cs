using wslc_desktop.Models;

namespace wslc_desktop.Services;

public interface IEnvironmentBootstrapService
{
    Task<WslcPrerequisiteStatus> CheckWslcAsync(CancellationToken cancellationToken = default);

    Task<DockerCliStatus> CheckDockerCliAsync(CancellationToken cancellationToken = default);

    void AddToolBinToProcessPath();
}

public sealed class EnvironmentBootstrapService : IEnvironmentBootstrapService
{
    private static readonly TimeSpan WslcCheckTimeout = TimeSpan.FromSeconds(5);
    private readonly ICommandProbe _probe;
    private readonly CliToolPathResolver _paths;

    public EnvironmentBootstrapService(ICommandProbe probe, CliToolPathResolver paths)
    {
        _probe = probe;
        _paths = paths;
    }

    public async Task<WslcPrerequisiteStatus> CheckWslcAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WslcCheckTimeout);

        try
        {
            CommandProbeResult wsl = await _probe.RunAsync("wsl.exe", "--version", timeout.Token);
            CommandProbeResult wslc = await _probe.RunAsync("wslc.exe", "version", timeout.Token);
            return BootstrapPrerequisiteEvaluator.EvaluateWslc(
                wsl.Exists,
                wslc.Exists && wslc.ExitCode == 0,
                MergeOutput(wsl));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BootstrapPrerequisiteEvaluator.CreateWslcCheckTimedOut(WslcCheckTimeout);
        }
    }

    public async Task<DockerCliStatus> CheckDockerCliAsync(CancellationToken cancellationToken = default)
    {
        CommandProbeResult docker = await _probe.RunAsync("docker.exe", "--version", cancellationToken);
        string composePath = FindComposePath();
        bool composeAvailable = !string.IsNullOrWhiteSpace(composePath);

        string message = docker.Exists
            ? composeAvailable
                ? "Docker CLI and Compose are available."
                : "Docker CLI is available. Docker Compose is not installed."
            : "Docker CLI is not installed.";

        return new DockerCliStatus(
            docker.Exists,
            string.IsNullOrWhiteSpace(docker.ResolvedPath) ? _probe.FindExecutable("docker.exe") : docker.ResolvedPath,
            composeAvailable,
            composePath,
            message);
    }

    public void AddToolBinToProcessPath()
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string directory in _paths.GetCandidateBinDirectories().Where(Directory.Exists))
        {
            path = PathEnvironmentEditor.AddPathSegment(path, directory);
        }

        Environment.SetEnvironmentVariable("PATH", path, EnvironmentVariableTarget.Process);
    }

    private string FindComposePath()
    {
        foreach (string directory in _paths.GetCandidateComposePluginDirectories())
        {
            string pluginPath = Path.Combine(directory, "docker-compose.exe");
            if (File.Exists(pluginPath))
            {
                return pluginPath;
            }
        }

        string standalone = _probe.FindExecutable("docker-compose.exe");
        return standalone;
    }

    private static string MergeOutput(CommandProbeResult result)
    {
        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError
            : result.StandardOutput;
    }
}
