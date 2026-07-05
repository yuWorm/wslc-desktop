namespace WslcDesktop.Contracts;

public static class WslcdRouteValues
{
    public static string DecodePathValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Uri.UnescapeDataString(value.Trim());
    }
}
