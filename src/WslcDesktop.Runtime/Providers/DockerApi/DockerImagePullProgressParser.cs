using System.Text.Json;
using WslcDesktop.Contracts;

namespace WslcDesktop.Runtime.Providers.DockerApi;

public static class DockerImagePullProgressParser
{
    public static IReadOnlyList<ImagePullProgressDto> Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var frames = new List<ImagePullProgressDto>();
        foreach (string line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using JsonDocument frame = JsonDocument.Parse(line);
                frames.Add(MapFrame(frame.RootElement));
            }
            catch (JsonException)
            {
            }
        }

        return frames;
    }

    private static ImagePullProgressDto MapFrame(JsonElement root)
    {
        string id = GetString(root, "id");
        string status = GetString(root, "status", "error");

        if (TryGetProperty(root, "progressDetail", out JsonElement progressDetail) &&
            progressDetail.ValueKind == JsonValueKind.Object)
        {
            ulong current = GetUInt64(progressDetail, "current");
            ulong total = GetUInt64(progressDetail, "total");
            if (total > 0)
            {
                return ImagePullProgressDto.ProgressFrame(id, status, current, total);
            }
        }

        return ImagePullProgressDto.StatusFrame(id, status);
    }

    private static ulong GetUInt64(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        return property.TryGetUInt64(out ulong value) ? value : 0;
    }

    private static string GetString(JsonElement root, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (!TryGetProperty(root, propertyName, out JsonElement property))
            {
                continue;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() ?? string.Empty,
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement property)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in root.EnumerateObject())
            {
                if (candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }
}
