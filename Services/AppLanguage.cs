using Windows.Globalization;

namespace wslc_desktop.Services;

public static class AppLanguage
{
    public const string System = "system";
    public const string English = "en-US";
    public const string Chinese = "zh-CN";

    public static IReadOnlyList<string> SupportedSettings { get; } = [System, Chinese, English];

    public static string NormalizeSetting(string? language)
    {
        return language switch
        {
            Chinese => Chinese,
            English => English,
            _ => System
        };
    }

    public static string GetDisplayName(string language)
    {
        return NormalizeSetting(language) switch
        {
            Chinese => "中文",
            English => "English",
            _ => "System"
        };
    }

    public static string ResolveSystemLanguage(IEnumerable<string> languages)
    {
        foreach (string language in languages)
        {
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return Chinese;
            }

            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return English;
            }
        }

        return English;
    }

    public static string GetEffectiveLanguage(string setting, IEnumerable<string> systemLanguages)
    {
        return NormalizeSetting(setting) switch
        {
            Chinese => Chinese,
            English => English,
            _ => ResolveSystemLanguage(systemLanguages)
        };
    }

    public static string GetCurrentSystemLanguage()
    {
        return ResolveSystemLanguage(ApplicationLanguages.Languages);
    }
}
