using System.ComponentModel;
using System.Diagnostics;

namespace wslc_desktop.Services;

public sealed record CommandProbeResult(
    bool Exists,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string ResolvedPath);

public interface ICommandProbe
{
    Task<CommandProbeResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default);

    string FindExecutable(string fileName);
}

public sealed class ProcessCommandProbe : ICommandProbe
{
    private readonly CliToolPathResolver _paths;

    public ProcessCommandProbe(CliToolPathResolver paths)
    {
        _paths = paths;
    }

    public async Task<CommandProbeResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        string resolvedPath = FindExecutable(fileName);
        string launchName = string.IsNullOrWhiteSpace(resolvedPath) ? fileName : resolvedPath;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = launchName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.Environment["PATH"] = BuildProcessPath(process.StartInfo.Environment["PATH"]);

            if (!process.Start())
            {
                return new CommandProbeResult(false, -1, string.Empty, "Process did not start.", resolvedPath);
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new CommandProbeResult(true, process.ExitCode, await stdout, await stderr, resolvedPath);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return new CommandProbeResult(false, -1, string.Empty, ex.Message, resolvedPath);
        }
    }

    public string FindExecutable(string fileName)
    {
        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
        {
            return fileName;
        }

        foreach (string directory in GetSearchDirectories())
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private string BuildProcessPath(string? currentPath)
    {
        string path = currentPath ?? string.Empty;
        foreach (string directory in _paths.GetCandidateBinDirectories().Where(Directory.Exists))
        {
            path = PathEnvironmentEditor.AddPathSegment(path, directory);
        }

        return path;
    }

    private IEnumerable<string> GetSearchDirectories()
    {
        foreach (string directory in _paths.GetCandidateBinDirectories())
        {
            yield return directory;
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return directory;
        }
    }
}
