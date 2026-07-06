using System.IO.Compression;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public static class CliToolArchiveInstaller
{
    public static async Task<CliToolInstallResult> InstallDockerCliFromZipAsync(
        string zipPath,
        string binDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Docker CLI zip was not found.", zipPath);
        }

        Directory.CreateDirectory(binDirectory);
        string dockerPath = Path.Combine(binDirectory, "docker.exe");

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry dockerEntry = archive.Entries.FirstOrDefault(IsDockerCliEntry)
            ?? throw new InvalidOperationException("The selected zip does not contain docker.exe.");

        await using (Stream input = dockerEntry.Open())
        await using (FileStream output = File.Create(dockerPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        return new CliToolInstallResult(
            "Docker CLI",
            binDirectory,
            [dockerPath],
            $"Docker CLI installed to {dockerPath}.");
    }

    public static async Task<CliToolInstallResult> InstallComposeFromExeAsync(
        string exePath,
        string binDirectory,
        CancellationToken cancellationToken = default)
    {
        string pluginDirectory = new CliToolPathResolver().UserDockerCliPluginDirectory;
        return await InstallComposeFromExeAsync(exePath, binDirectory, pluginDirectory, cancellationToken);
    }

    public static async Task<CliToolInstallResult> InstallComposeFromExeAsync(
        string exePath,
        string binDirectory,
        string pluginDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("Docker Compose executable was not found.", exePath);
        }

        Directory.CreateDirectory(binDirectory);
        string standalonePath = Path.Combine(binDirectory, "docker-compose.exe");
        Directory.CreateDirectory(pluginDirectory);
        string pluginPath = Path.Combine(pluginDirectory, "docker-compose.exe");

        await CopyFileAsync(exePath, standalonePath, cancellationToken);
        await CopyFileAsync(exePath, pluginPath, cancellationToken);

        return new CliToolInstallResult(
            "Docker Compose",
            binDirectory,
            [standalonePath, pluginPath],
            $"Docker Compose installed to {standalonePath} and {pluginPath}.");
    }

    private static bool IsDockerCliEntry(ZipArchiveEntry entry)
    {
        string fileName = Path.GetFileName(entry.FullName);
        return fileName.Equals("docker.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using FileStream input = File.OpenRead(source);
        await using FileStream output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);
    }
}
