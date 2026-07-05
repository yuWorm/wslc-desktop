namespace wslc_desktop.Services;

public sealed record DockerContextResult(
    string ContextName,
    string DockerHost,
    bool Created);

public interface IDockerContextService
{
    Task<DockerContextResult> CreateAndUseWslcDesktopContextAsync(CancellationToken cancellationToken = default);
}

public sealed class DockerContextService : IDockerContextService
{
    public const string ContextName = "wslc-desktop";
    public const string DockerHost = "npipe:////./pipe/wslc-desktop-docker";

    private readonly ICommandProbe _probe;

    public DockerContextService(ICommandProbe probe)
    {
        _probe = probe;
    }

    public async Task<DockerContextResult> CreateAndUseWslcDesktopContextAsync(CancellationToken cancellationToken = default)
    {
        CommandProbeResult inspect = await _probe.RunAsync("docker.exe", $"context inspect {ContextName}", cancellationToken);
        EnsureDockerExists(inspect);

        bool created = false;
        if (inspect.ExitCode != 0)
        {
            CommandProbeResult create = await _probe.RunAsync(
                "docker.exe",
                $"context create {ContextName} --docker \"host={DockerHost}\"",
                cancellationToken);
            EnsureDockerExists(create);
            EnsureSuccess(create, "docker context create");
            created = true;
        }

        CommandProbeResult use = await _probe.RunAsync("docker.exe", $"context use {ContextName}", cancellationToken);
        EnsureDockerExists(use);
        EnsureSuccess(use, "docker context use");

        return new DockerContextResult(ContextName, DockerHost, created);
    }

    private static void EnsureDockerExists(CommandProbeResult result)
    {
        if (!result.Exists)
        {
            throw new InvalidOperationException("docker.exe is not installed or not available on PATH.");
        }
    }

    private static void EnsureSuccess(CommandProbeResult result, string command)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        throw new InvalidOperationException($"{command} failed: {detail.Trim()}");
    }
}
