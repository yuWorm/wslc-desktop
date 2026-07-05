namespace WslcDesktop.Runtime.Providers.WslcCli;

public static class WslcImageReferencePolicy
{
    public static string ApplyMirror(string reference, WslcRuntimeSettings settings)
    {
        string normalized = NormalizeReference(reference);
        if (string.IsNullOrWhiteSpace(settings.ImageMirror) || IsQualifiedReference(normalized))
        {
            return normalized;
        }

        return $"{settings.ImageMirror.TrimEnd('/')}/{normalized}";
    }

    public static string GetRewriteTarget(string reference, WslcRuntimeSettings settings)
    {
        string normalized = NormalizeReference(reference);
        if (!settings.RewriteImageTag || !IsQualifiedReference(normalized) || IsDockerHubQualifiedReference(normalized))
        {
            return string.Empty;
        }

        int slash = normalized.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && slash < normalized.Length - 1
            ? normalized[(slash + 1)..]
            : string.Empty;
    }

    private static string NormalizeReference(string reference)
    {
        return string.IsNullOrWhiteSpace(reference)
            ? string.Empty
            : reference.Trim();
    }

    private static bool IsQualifiedReference(string reference)
    {
        int slash = reference.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return false;
        }

        string firstComponent = reference[..slash];
        return firstComponent.Contains('.', StringComparison.Ordinal) ||
            firstComponent.Contains(':', StringComparison.Ordinal) ||
            firstComponent.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDockerHubQualifiedReference(string reference)
    {
        int slash = reference.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return false;
        }

        string host = reference[..slash];
        return host.Equals("docker.io", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("registry-1.docker.io", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("index.docker.io", StringComparison.OrdinalIgnoreCase);
    }
}
