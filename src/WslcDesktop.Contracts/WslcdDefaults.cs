using System.Runtime.InteropServices;

namespace WslcDesktop.Contracts;

public static class WslcdDefaults
{
    public const string ProductName = "WSLC Desktop";
    public const string DaemonName = "wslcd-desktop";
    public const string NativePipeName = "wslc-desktop";
    public const string DockerPipeName = "wslc-desktop-docker";
    public const string DockerApiVersion = "1.54";
    public const string DockerMinApiVersion = "1.40";
    public const string NativeApiVersion = "1";
    public const string DefaultRuntimeProviderName = "wslc-cli";
    public const int DefaultOperationRetentionCount = 200;

    public static string DefaultDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductName);

    public static string DefaultLogDirectory => Path.Combine(DefaultDataRoot, "Diagnostics");

    public static string Architecture => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    public static string OperatingSystem => OperatingSystemName();

    private static string OperatingSystemName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "darwin";
        }

        return RuntimeInformation.OSDescription;
    }
}
