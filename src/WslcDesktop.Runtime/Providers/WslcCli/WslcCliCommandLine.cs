namespace WslcDesktop.Runtime.Providers.WslcCli;

internal static class WslcCliCommandLine
{
    public static string Join(params string[] arguments)
    {
        return Join((IEnumerable<string>)arguments);
    }

    public static string Join(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Where(argument => !string.IsNullOrWhiteSpace(argument)).Select(Quote));
    }

    private static string Quote(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        bool needsQuotes = argument.Any(char.IsWhiteSpace) || argument.Contains('"', StringComparison.Ordinal);
        if (!needsQuotes)
        {
            return argument;
        }

        return "\"" + argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
