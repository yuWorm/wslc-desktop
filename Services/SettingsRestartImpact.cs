using wslc_desktop.Models;

namespace wslc_desktop.Services;

public static class SettingsRestartImpact
{
    public static bool RequiresDaemonRestart(AppSettingsSnapshot previous, AppSettingsSnapshot next)
    {
        return !StringEquals(RuntimeProviderSelection.Normalize(previous.RuntimeProvider), RuntimeProviderSelection.Normalize(next.RuntimeProvider))
            || !StringEquals(previous.DockerApiHost.Trim(), next.DockerApiHost.Trim())
            || previous.AllowTcpDockerApi != next.AllowTcpDockerApi
            || !StringEquals(previous.WslcHttpProxy.Trim(), next.WslcHttpProxy.Trim())
            || !StringEquals(previous.WslcHttpsProxy.Trim(), next.WslcHttpsProxy.Trim())
            || !StringEquals(previous.WslcNoProxy.Trim(), next.WslcNoProxy.Trim())
            || !StringEquals(previous.WslcImageMirror.Trim().TrimEnd('/'), next.WslcImageMirror.Trim().TrimEnd('/'))
            || previous.WslcRewriteImageTag != next.WslcRewriteImageTag
            || previous.WslcRemoveRewrittenSourceTag != next.WslcRemoveRewrittenSourceTag
            || !StringEquals(NormalizeMultiline(previous.WslcEnvironment), NormalizeMultiline(next.WslcEnvironment));
    }

    private static bool StringEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static string NormalizeMultiline(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }
}
