namespace wslc_desktop.Services;

public static class PathEnvironmentEditor
{
    public static string AddPathSegment(string existingPath, string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return existingPath;
        }

        string normalizedSegment = Normalize(segment);
        var parts = existingPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Any(part => Normalize(part).Equals(normalizedSegment, StringComparison.OrdinalIgnoreCase)))
        {
            return string.Join(Path.PathSeparator, parts);
        }

        return string.IsNullOrWhiteSpace(existingPath)
            ? segment
            : string.Join(Path.PathSeparator, parts.Append(segment));
    }

    private static string Normalize(string value)
    {
        return value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
