using System.Diagnostics;

namespace WslcDesktop.Runtime.Providers.WslcCli;

public sealed class WslcCommandRunner
{
    private readonly WslcRuntimeSettings _settings;

    public WslcCommandRunner()
        : this(WslcRuntimeSettings.Empty)
    {
    }

    public WslcCommandRunner(WslcRuntimeSettings settings)
    {
        _settings = settings;
    }

    public async Task<RuntimeCommandResult> RunAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "wslc",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        foreach (var pair in _settings.BuildProcessEnvironment())
        {
            process.StartInfo.Environment[pair.Key] = pair.Value;
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start wslc.");
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new RuntimeCommandResult(
            process.ExitCode,
            (await outputTask).TrimEnd(),
            (await errorTask).TrimEnd(),
            DateTimeOffset.UtcNow - startedAt,
            $"wslc {arguments}");
    }
}
