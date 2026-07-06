using System.Text.Json;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public sealed class FileAppSettingsService : IAppSettingsService
{
    private readonly string _settingsPath;

    public FileAppSettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WSLC Desktop"))
    {
    }

    public FileAppSettingsService(string settingsRoot)
    {
        SettingsRoot = settingsRoot;
        _settingsPath = Path.Combine(settingsRoot, "settings.json");
    }

    public string SettingsRoot { get; }

    public async Task<AppSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return CreateDefault();
        }

        await using var stream = File.OpenRead(_settingsPath);
        var stored = await JsonSerializer.DeserializeAsync<StoredSettings>(stream, cancellationToken: cancellationToken);
        return stored?.ToSnapshot() ?? CreateDefault();
    }

    public async Task SaveAsync(AppSettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(SettingsRoot);
        var stored = StoredSettings.FromSnapshot(settings);
        var options = new JsonSerializerOptions { WriteIndented = true };
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, stored, options, cancellationToken);
    }

    public AppSettingsSnapshot LoadForStartup()
    {
        if (!File.Exists(_settingsPath))
        {
            return CreateDefault();
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<StoredSettings>(json)?.ToSnapshot() ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public static AppSettingsSnapshot CreateDefault()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WSLC Desktop");

        return new AppSettingsSnapshot(
            root,
            Math.Max(1, Environment.ProcessorCount / 2),
            4096,
            "/bin/sh",
            true,
            AppLanguage.System,
            RuntimeProviderSelection.WslcCli,
            string.Empty,
            false,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            false,
            string.Empty);
    }

    private sealed record StoredSettings(
        string? DataRoot,
        int CpuCount,
        int MemoryMB,
        string? DefaultShell,
        bool PreferExternalTerminal,
        string? Language,
        string? RuntimeProvider,
        string? DockerApiHost,
        bool AllowTcpDockerApi,
        bool LaunchAtLogin,
        string? WslcHttpProxy,
        string? WslcHttpsProxy,
        string? WslcNoProxy,
        string? WslcImageMirror,
        bool WslcRewriteImageTag,
        bool WslcRemoveRewrittenSourceTag,
        string? WslcEnvironment,
        bool WslcPrerequisiteInitialized)
    {
        public static StoredSettings FromSnapshot(AppSettingsSnapshot snapshot)
        {
            return new StoredSettings(
                snapshot.DataRoot,
                snapshot.CpuCount,
                snapshot.MemoryMB,
                snapshot.DefaultShell,
                snapshot.PreferExternalTerminal,
                AppLanguage.NormalizeSetting(snapshot.Language),
                RuntimeProviderSelection.Normalize(snapshot.RuntimeProvider),
                snapshot.DockerApiHost,
                snapshot.AllowTcpDockerApi,
                snapshot.LaunchAtLogin,
                snapshot.WslcHttpProxy,
                snapshot.WslcHttpsProxy,
                snapshot.WslcNoProxy,
                snapshot.WslcImageMirror,
                snapshot.WslcRewriteImageTag,
                snapshot.WslcRewriteImageTag && snapshot.WslcRemoveRewrittenSourceTag,
                snapshot.WslcEnvironment,
                snapshot.WslcPrerequisiteInitialized);
        }

        public AppSettingsSnapshot ToSnapshot()
        {
            var defaults = CreateDefault();
            return new AppSettingsSnapshot(
                string.IsNullOrWhiteSpace(DataRoot) ? defaults.DataRoot : DataRoot,
                CpuCount <= 0 ? defaults.CpuCount : CpuCount,
                MemoryMB <= 0 ? defaults.MemoryMB : MemoryMB,
                string.IsNullOrWhiteSpace(DefaultShell) ? defaults.DefaultShell : DefaultShell,
                PreferExternalTerminal,
                AppLanguage.NormalizeSetting(Language),
                RuntimeProviderSelection.Normalize(RuntimeProvider),
                string.IsNullOrWhiteSpace(DockerApiHost) ? string.Empty : DockerApiHost.Trim(),
                AllowTcpDockerApi,
                LaunchAtLogin,
                NormalizeText(WslcHttpProxy),
                NormalizeText(WslcHttpsProxy),
                NormalizeText(WslcNoProxy),
                NormalizeText(WslcImageMirror).TrimEnd('/'),
                WslcRewriteImageTag,
                WslcRewriteImageTag && WslcRemoveRewrittenSourceTag,
                NormalizeMultiline(WslcEnvironment),
                WslcPrerequisiteInitialized);
        }

        private static string NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeMultiline(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        }
    }
}
