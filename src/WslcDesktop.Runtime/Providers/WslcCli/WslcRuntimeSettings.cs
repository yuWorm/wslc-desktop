using System.Collections;

namespace WslcDesktop.Runtime.Providers.WslcCli;

public sealed record WslcRuntimeSettings(
    string HttpProxy,
    string HttpsProxy,
    string NoProxy,
    string ImageMirror,
    bool RewriteImageTag,
    bool RemoveRewrittenSourceTag,
    IReadOnlyDictionary<string, string> Environment)
{
    private static readonly StringComparer EnvironmentComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> ReservedEnvironmentNames = new(EnvironmentComparer)
    {
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "NO_PROXY",
        "http_proxy",
        "https_proxy",
        "no_proxy"
    };

    public static WslcRuntimeSettings Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        RewriteImageTag: false,
        RemoveRewrittenSourceTag: false,
        new Dictionary<string, string>(EnvironmentComparer));

    public static WslcRuntimeSettings FromCurrentProcess()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in global::System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
            {
                values[key] = entry.Value?.ToString();
            }
        }

        return FromEnvironment(values);
    }

    public static WslcRuntimeSettings FromEnvironment(IReadOnlyDictionary<string, string?> values)
    {
        return FromValues(
            GetString(values, "WSLCD_WSLC_HTTP_PROXY"),
            GetString(values, "WSLCD_WSLC_HTTPS_PROXY"),
            GetString(values, "WSLCD_WSLC_NO_PROXY"),
            GetString(values, "WSLCD_WSLC_IMAGE_MIRROR"),
            GetBool(values, "WSLCD_WSLC_REWRITE_IMAGE_TAG"),
            GetBool(values, "WSLCD_WSLC_REMOVE_REWRITTEN_SOURCE_TAG"),
            GetString(values, "WSLCD_WSLC_ENVIRONMENT"));
    }

    public static WslcRuntimeSettings FromValues(
        string httpProxy,
        string httpsProxy,
        string noProxy,
        string imageMirror,
        bool rewriteImageTag,
        bool removeRewrittenSourceTag,
        string environment)
    {
        bool removeSourceTag = rewriteImageTag && removeRewrittenSourceTag;
        var parsedEnvironment = ParseEnvironment(environment);

        return new WslcRuntimeSettings(
            NormalizeText(httpProxy),
            NormalizeText(httpsProxy),
            NormalizeText(noProxy),
            NormalizeMirror(imageMirror),
            rewriteImageTag,
            removeSourceTag,
            parsedEnvironment);
    }

    public IReadOnlyDictionary<string, string> BuildProcessEnvironment()
    {
        var values = new Dictionary<string, string>(Environment, EnvironmentComparer);

        AddProxy(values, "HTTP_PROXY", "http_proxy", HttpProxy);
        AddProxy(values, "HTTPS_PROXY", "https_proxy", HttpsProxy);
        AddProxy(values, "NO_PROXY", "no_proxy", NoProxy);

        return values;
    }

    private static IReadOnlyDictionary<string, string> ParseEnvironment(string value)
    {
        var parsed = new Dictionary<string, string>(EnvironmentComparer);
        foreach (string rawLine in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            if (key.Length == 0 || ReservedEnvironmentNames.Contains(key))
            {
                continue;
            }

            parsed[key] = line[(separator + 1)..];
        }

        return parsed;
    }

    private static void AddProxy(IDictionary<string, string> values, string upperName, string lowerName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string normalized = value.Trim();
        values[upperName] = normalized;
        values[lowerName] = normalized;
    }

    private static string GetString(IReadOnlyDictionary<string, string?> values, string name)
    {
        return values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : string.Empty;
    }

    private static bool GetBool(IReadOnlyDictionary<string, string?> values, string name)
    {
        return values.TryGetValue(name, out string? value) &&
            !string.IsNullOrWhiteSpace(value) &&
            (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeMirror(string value)
    {
        return NormalizeText(value).TrimEnd('/');
    }

    private static string NormalizeText(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}
