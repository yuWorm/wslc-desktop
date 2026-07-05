using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text.Json;
using System.Text;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public interface ICliToolInstallationService
{
    string BinDirectory { get; }

    Task<CliToolInstallResult> InstallDockerCliFromZipAsync(string zipPath, CancellationToken cancellationToken = default);

    Task<CliToolInstallResult> InstallComposeFromExeAsync(string exePath, CancellationToken cancellationToken = default);

    Task<CliToolInstallResult> InstallLatestDockerCliAsync(CancellationToken cancellationToken = default);

    Task<CliToolInstallResult> InstallLatestComposeAsync(CancellationToken cancellationToken = default);

    string AddBinToUserPath();

    Task<string> AddBinToMachinePathAsync(CancellationToken cancellationToken = default);
}

public sealed class CliToolInstallationService : ICliToolInstallationService
{
    private static readonly Uri DockerIndexUri = new("https://download.docker.com/win/static/stable/x86_64/");
    private static readonly Uri ComposeLatestReleaseUri = new("https://api.github.com/repos/docker/compose/releases/latest");

    private readonly CliToolPathResolver _paths;
    private readonly HttpClient _httpClient;

    public CliToolInstallationService(CliToolPathResolver paths)
        : this(paths, CreateHttpClient())
    {
    }

    public CliToolInstallationService(CliToolPathResolver paths, HttpClient httpClient)
    {
        _paths = paths;
        _httpClient = httpClient;
    }

    public string BinDirectory => _paths.ResolveWritableBinDirectory();

    public Task<CliToolInstallResult> InstallDockerCliFromZipAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        return CliToolArchiveInstaller.InstallDockerCliFromZipAsync(zipPath, BinDirectory, cancellationToken);
    }

    public Task<CliToolInstallResult> InstallComposeFromExeAsync(string exePath, CancellationToken cancellationToken = default)
    {
        return CliToolArchiveInstaller.InstallComposeFromExeAsync(exePath, BinDirectory, cancellationToken);
    }

    public async Task<CliToolInstallResult> InstallLatestDockerCliAsync(CancellationToken cancellationToken = default)
    {
        string html = await _httpClient.GetStringAsync(DockerIndexUri, cancellationToken);
        DockerStaticRelease release = DockerStaticReleaseIndex.FindLatestDockerZip(html, DockerIndexUri);
        string tempZip = Path.Combine(Path.GetTempPath(), release.FileName);
        await DownloadFileAsync(release.DownloadUri, tempZip, cancellationToken);
        return await InstallDockerCliFromZipAsync(tempZip, cancellationToken);
    }

    public async Task<CliToolInstallResult> InstallLatestComposeAsync(CancellationToken cancellationToken = default)
    {
        using Stream stream = await _httpClient.GetStreamAsync(ComposeLatestReleaseUri, cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement assets = document.RootElement.GetProperty("assets");
        JsonElement asset = assets.EnumerateArray()
            .FirstOrDefault(value =>
                value.TryGetProperty("name", out JsonElement name) &&
                name.GetString()?.Equals("docker-compose-windows-x86_64.exe", StringComparison.OrdinalIgnoreCase) == true);

        if (asset.ValueKind == JsonValueKind.Undefined ||
            !asset.TryGetProperty("browser_download_url", out JsonElement downloadUrlElement) ||
            string.IsNullOrWhiteSpace(downloadUrlElement.GetString()))
        {
            throw new InvalidOperationException("Could not find docker-compose-windows-x86_64.exe in the latest Docker Compose release.");
        }

        string tempExe = Path.Combine(Path.GetTempPath(), "docker-compose-windows-x86_64.exe");
        await DownloadFileAsync(new Uri(downloadUrlElement.GetString()!), tempExe, cancellationToken);
        return await InstallComposeFromExeAsync(tempExe, cancellationToken);
    }

    public string AddBinToUserPath()
    {
        string bin = BinDirectory;
        string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
        string nextPath = PathEnvironmentEditor.AddPathSegment(currentPath, bin);
        Environment.SetEnvironmentVariable("PATH", nextPath, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(
            "PATH",
            PathEnvironmentEditor.AddPathSegment(Environment.GetEnvironmentVariable("PATH") ?? string.Empty, bin),
            EnvironmentVariableTarget.Process);
        return bin;
    }

    public async Task<string> AddBinToMachinePathAsync(CancellationToken cancellationToken = default)
    {
        string bin = BinDirectory;
        string script = CreateMachinePathScript(bin);
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Failed to start elevated PowerShell for system PATH update.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"System PATH update failed with exit code {process.ExitCode}.");
        }

        Environment.SetEnvironmentVariable(
            "PATH",
            PathEnvironmentEditor.AddPathSegment(Environment.GetEnvironmentVariable("PATH") ?? string.Empty, bin),
            EnvironmentVariableTarget.Process);
        return bin;
    }

    private async Task DownloadFileAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        await using Stream input = await _httpClient.GetStreamAsync(uri, cancellationToken);
        await using FileStream output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("wslc-desktop", "1.0"));
        return client;
    }

    private static string CreateMachinePathScript(string bin)
    {
        string escapedBin = bin.Replace("'", "''", StringComparison.Ordinal);
        return $$"""
            $ErrorActionPreference = 'Stop'
            $bin = '{{escapedBin}}'
            $current = [Environment]::GetEnvironmentVariable('Path', 'Machine')
            $parts = @()
            if (-not [string]::IsNullOrWhiteSpace($current)) {
                $parts = $current -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            }
            $normalizedBin = $bin.Trim().TrimEnd('\', '/')
            $exists = $false
            foreach ($part in $parts) {
                if ($part.Trim().TrimEnd('\', '/') -ieq $normalizedBin) {
                    $exists = $true
                    break
                }
            }
            if (-not $exists) {
                $next = if ($parts.Count -eq 0) { $bin } else { ($parts + $bin) -join ';' }
                [Environment]::SetEnvironmentVariable('Path', $next, 'Machine')
            }
            """;
    }
}
