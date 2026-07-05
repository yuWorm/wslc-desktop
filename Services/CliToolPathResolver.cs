namespace wslc_desktop.Services;

public sealed class CliToolPathResolver
{
    public string PreferredBinDirectory { get; }

    public string UserBinDirectory { get; }

    public CliToolPathResolver()
    {
        PreferredBinDirectory = Path.Combine(AppContext.BaseDirectory, "bin");
        UserBinDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WSLC Desktop",
            "bin");
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
}
