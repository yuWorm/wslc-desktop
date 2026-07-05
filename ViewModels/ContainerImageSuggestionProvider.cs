using wslc_desktop.Models;

namespace wslc_desktop.ViewModels;

public static class ContainerImageSuggestionProvider
{
    public static IReadOnlyList<string> BuildReferences(IEnumerable<ImageSummary> images)
    {
        return images
            .Select(ToReference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> Filter(IEnumerable<string> references, string query, int limit = 20)
    {
        string[] tokens = string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return references
            .Where(reference => tokens.All(token => reference.Contains(token, StringComparison.CurrentCultureIgnoreCase)))
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private static string ToReference(ImageSummary image)
    {
        if (string.IsNullOrWhiteSpace(image.Repository) ||
            image.Repository.Equals("<none>", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string tag = string.IsNullOrWhiteSpace(image.Tag) || image.Tag.Equals("<none>", StringComparison.OrdinalIgnoreCase)
            ? "latest"
            : image.Tag.Trim();
        return $"{image.Repository.Trim()}:{tag}";
    }
}
