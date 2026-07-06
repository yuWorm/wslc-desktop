using wslc_desktop.Models;

namespace wslc_desktop.Services;

public static class BootstrapPrerequisiteEvaluator
{
    private static string RequiredWslSetupCommands => string.Join(Environment.NewLine, "wsl --update", "wsl --install");

    public static WslcPrerequisiteStatus EvaluateWslc(
        bool wslExists,
        bool wslcExists,
        string wslVersionOutput)
    {
        string detectedVersion = NormalizeVersionOutput(wslVersionOutput);

        if (wslcExists)
        {
            return new WslcPrerequisiteStatus(
                WslcPrerequisiteState.Ready,
                true,
                string.Empty,
                detectedVersion,
                string.IsNullOrWhiteSpace(detectedVersion)
                    ? "wslc.exe is available."
                    : $"wslc.exe is available. {detectedVersion}");
        }

        if (!wslExists)
        {
            return new WslcPrerequisiteStatus(
                WslcPrerequisiteState.MissingWsl,
                false,
                RequiredWslSetupCommands,
                detectedVersion,
                "WSL is not installed. Install WSL before using WSLC Desktop.");
        }

        return new WslcPrerequisiteStatus(
            WslcPrerequisiteState.WslUpdateRequired,
            false,
            RequiredWslSetupCommands,
            detectedVersion,
            string.IsNullOrWhiteSpace(detectedVersion)
                ? "WSL is installed, but wslc.exe is missing. Update WSL to the latest version."
                : $"WSL is installed, but wslc.exe is missing. Update WSL to the latest version. {detectedVersion}");
    }

    public static WslcPrerequisiteStatus CreateWslcCheckTimedOut(TimeSpan timeout)
    {
        return new WslcPrerequisiteStatus(
            WslcPrerequisiteState.CheckTimedOut,
            false,
            RequiredWslSetupCommands,
            string.Empty,
            $"WSLC prerequisite check timed out after {timeout.TotalSeconds:0} seconds. Run the commands manually to update or install WSL, then recheck.");
    }

    private static string NormalizeVersionOutput(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
    }
}
