using System.Text.Json;

namespace wslc_desktop.Services;

public sealed class CliToolPathResolver
{
    public string PreferredBinDirectory { get; }

    public string UserBinDirectory { get; }

    public string DockerConfigDirectory { get; }

    public string UserDockerCliPluginDirectory { get; }

    public string SystemDockerCliPluginDirectory { get; }

    public CliToolPathResolver()
    {
        PreferredBinDirectory = Path.Combine(AppContext.BaseDirectory, "bin");
        UserBinDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WSLC Desktop",
            "bin");
        DockerConfigDirectory = ResolveDockerConfigDirectory();
        UserDockerCliPluginDirectory = Path.Combine(DockerConfigDirectory, "cli-plugins");
        SystemDockerCliPluginDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Docker",
            "cli-plugins");
    }

    public string ResolveWritableBinDirectory()
    {
        if (CanWriteDirectory(PreferredBinDirectory))
        {
            return PreferredBinDirectory;
        }

        Directory.CreateDirectory(UserBinDirectory);
        return UserBinDirectory;
    }

    public IReadOnlyList<string> GetCandidateBinDirectories()
    {
        return [PreferredBinDirectory, UserBinDirectory];
    }

    public IReadOnlyList<string> GetCandidateComposePluginDirectories()
    {
        var directories = new List<string>
        {
            UserDockerCliPluginDirectory
        };

        directories.AddRange(ReadDockerCliExtraPluginDirectories());

        if (!string.IsNullOrWhiteSpace(SystemDockerCliPluginDirectory))
        {
            directories.Add(SystemDockerCliPluginDirectory);
        }

        return directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool CanWriteDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string ResolveDockerConfigDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable("DOCKER_CONFIG");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty;
        }

        return Path.Combine(userProfile, ".docker");
    }

    private IEnumerable<string> ReadDockerCliExtraPluginDirectories()
    {
        string configPath = Path.Combine(DockerConfigDirectory, "config.json");
        if (!File.Exists(configPath))
        {
            return [];
        }

        try
        {
            using FileStream stream = File.OpenRead(configPath);
            using JsonDocument document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("cliPluginsExtraDirs", out JsonElement element) ||
                element.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return element
                .EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
