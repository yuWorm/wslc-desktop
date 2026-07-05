using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcDesktop.Contracts;

namespace WslcDesktop.Runtime.Providers.WslcCli;

internal static class WslcCliOutputParser
{
    public static IReadOnlyList<ImageSummaryDto> ParseImages(string output)
    {
        if (string.IsNullOrWhiteSpace(output) || !output.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return [];
        }

        using var document = JsonDocument.Parse(output);
        return document.RootElement.EnumerateArray()
            .Select(item =>
            {
                string repository = GetString(item, "Repository");
                string id = GetString(item, "Id", "ID");
                return string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(id)
                    ? null
                    : new ImageSummaryDto(
                        ShortId(id),
                        repository,
                        DefaultIfWhiteSpace(GetString(item, "Tag"), "latest"),
                        FormatByteSize(GetUInt64(item, "Size")),
                        DefaultIfWhiteSpace(FormatEpoch(GetLong(item, "Created", "CreatedAt")), "-"),
                        false);
            })
            .Where(image => image is not null)
            .Select(image => image!)
            .GroupBy(image => $"{image.Repository}:{image.Tag}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(image => image.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(image => image.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ContainerSummaryDto> ParseContainers(string output)
    {
        if (string.IsNullOrWhiteSpace(output) || !output.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return [];
        }

        using var document = JsonDocument.Parse(output);
        return document.RootElement.EnumerateArray()
            .Select(item =>
            {
                string id = GetString(item, "Id", "ID");
                string name = GetString(item, "Name");
                string image = GetString(item, "Image");
                return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)
                    ? null
                    : new ContainerSummaryDto(
                        ShortId(id),
                        name,
                        image,
                        MapState(item),
                        0,
                        "-",
                        DefaultIfWhiteSpace(FormatEpoch(GetLong(item, "CreatedAt", "Created")), "-"),
                        FormatUptime(item),
                        FormatJsonPorts(item),
                        "-",
                        item.GetRawText(),
                        ReadLabels(item));
            })
            .Where(container => container is not null)
            .Select(container => container!)
            .GroupBy(container => container.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(container => container.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyDictionary<string, CliContainerStats> ParseStats(string output)
    {
        var stats = new Dictionary<string, CliContainerStats>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(output) || !output.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return stats;
        }

        using var document = JsonDocument.Parse(output);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            string id = GetString(item, "ID", "Id");
            string name = GetString(item, "Name");
            string cpu = GetString(item, "CPUPerc", "CpuPercent");
            string memory = GetString(item, "MemUsage", "MemoryUsage");
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var snapshot = new CliContainerStats(ParsePercent(cpu), string.IsNullOrWhiteSpace(memory) ? "-" : memory);
            if (!string.IsNullOrWhiteSpace(id))
            {
                stats[id] = snapshot;
                stats[ShortId(id)] = snapshot;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                stats[name] = snapshot;
            }
        }

        return stats;
    }

    public static IReadOnlyList<VolumeSummaryDto> ParseVolumes(string output)
    {
        if (string.IsNullOrWhiteSpace(output) || !output.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return [];
        }

        using var document = JsonDocument.Parse(output);
        return document.RootElement.EnumerateArray()
            .Select(item =>
            {
                string name = GetString(item, "Name", "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                string size = GetString(item, "Size", "size");
                string created = FormatEpoch(GetLong(item, "Created", "CreatedAt"));
                return new VolumeSummaryDto(
                    name,
                    string.IsNullOrWhiteSpace(size) ? "-" : size,
                    "-",
                    string.IsNullOrWhiteSpace(created) ? "-" : created,
                    true,
                    ReadLabels(item));
            })
            .Where(volume => volume is not null)
            .Select(volume => volume!)
            .OrderBy(volume => volume.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<NetworkEndpointSummaryDto> ParsePublishedPorts(IReadOnlyList<ContainerSummaryDto> containers)
    {
        var endpoints = new List<NetworkEndpointSummaryDto>();
        foreach (var container in containers)
        {
            if (string.IsNullOrWhiteSpace(container.PortSummary) || container.PortSummary == "-")
            {
                continue;
            }

            foreach (string entry in container.PortSummary.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = Regex.Match(entry, @"(?:(?:localhost|0\.0\.0\.0|127\.0\.0\.1):)?(?<host>\d+)\s*(?:->|:)\s*(?<container>\d+)(?:/(?<protocol>\w+))?", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                int hostPort = int.Parse(match.Groups["host"].Value, CultureInfo.InvariantCulture);
                int containerPort = int.Parse(match.Groups["container"].Value, CultureInfo.InvariantCulture);
                string protocol = match.Groups["protocol"].Success ? match.Groups["protocol"].Value : "tcp";
                endpoints.Add(new NetworkEndpointSummaryDto(
                    container.Name,
                    hostPort,
                    containerPort,
                    protocol,
                    protocol.Equals("udp", StringComparison.OrdinalIgnoreCase)
                        ? $"udp://localhost:{hostPort}"
                        : $"http://localhost:{hostPort}"));
            }
        }

        return endpoints
            .OrderBy(endpoint => endpoint.HostPort)
            .ThenBy(endpoint => endpoint.ContainerName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ContainerRuntimeState MapState(JsonElement item)
    {
        if (!item.TryGetProperty("State", out var stateProperty))
        {
            return ContainerRuntimeState.Unknown;
        }

        if (stateProperty.ValueKind is JsonValueKind.Number && stateProperty.TryGetInt32(out int state))
        {
            return state switch
            {
                1 => ContainerRuntimeState.Created,
                2 => ContainerRuntimeState.Running,
                3 => ContainerRuntimeState.Exited,
                _ => ContainerRuntimeState.Unknown
            };
        }

        if (stateProperty.ValueKind is JsonValueKind.String)
        {
            string value = stateProperty.GetString() ?? string.Empty;
            if (value.Contains("running", StringComparison.OrdinalIgnoreCase))
            {
                return ContainerRuntimeState.Running;
            }

            if (value.Contains("created", StringComparison.OrdinalIgnoreCase))
            {
                return ContainerRuntimeState.Created;
            }

            if (value.Contains("stopped", StringComparison.OrdinalIgnoreCase))
            {
                return ContainerRuntimeState.Stopped;
            }

            if (value.Contains("exited", StringComparison.OrdinalIgnoreCase))
            {
                return ContainerRuntimeState.Exited;
            }
        }

        return ContainerRuntimeState.Unknown;
    }

    private static string FormatJsonPorts(JsonElement item)
    {
        if (!item.TryGetProperty("Ports", out var ports) || ports.ValueKind is not JsonValueKind.Array)
        {
            return "-";
        }

        var formatted = new List<string>();
        foreach (var port in ports.EnumerateArray())
        {
            if (port.ValueKind is JsonValueKind.String)
            {
                string? value = port.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    formatted.Add(value);
                }

                continue;
            }

            if (port.ValueKind is not JsonValueKind.Object)
            {
                continue;
            }

            int host = GetInt(port, "HostPort", "hostPort", "PublicPort");
            int container = GetInt(port, "ContainerPort", "containerPort", "PrivatePort");
            string protocol = DefaultIfWhiteSpace(GetString(port, "Protocol", "Type"), "tcp");
            if (host > 0 && container > 0)
            {
                formatted.Add($"{host}->{container}/{protocol}");
            }
        }

        return formatted.Count == 0 ? "-" : string.Join(", ", formatted);
    }

    private static string FormatUptime(JsonElement item)
    {
        long value = GetLong(item, "StateChangedAt");
        return value <= 0 ? "-" : FormatEpoch(value);
    }

    private static string GetString(JsonElement item, params string[] names)
    {
        foreach (string name in names)
        {
            if (!item.TryGetProperty(name, out var property))
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

    private static Dictionary<string, string> ReadLabels(JsonElement item)
    {
        JsonElement labels = default;
        if (item.TryGetProperty("Labels", out JsonElement directLabels))
        {
            labels = directLabels;
        }
        else if (item.TryGetProperty("Config", out JsonElement config) &&
            config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("Labels", out JsonElement configLabels))
        {
            labels = configLabels;
        }

        if (labels.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels.EnumerateObject())
        {
            result[label.Name] = label.Value.ValueKind switch
            {
                JsonValueKind.String => label.Value.GetString() ?? string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => label.Value.GetRawText(),
                _ => string.Empty
            };
        }

        return result;
    }

    private static int GetInt(JsonElement item, params string[] names)
    {
        foreach (string name in names)
        {
            if (item.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.Number && property.TryGetInt32(out int value))
            {
                return value;
            }
        }

        return 0;
    }

    private static long GetLong(JsonElement item, params string[] names)
    {
        foreach (string name in names)
        {
            if (item.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.Number && property.TryGetInt64(out long value))
            {
                return value;
            }
        }

        return 0;
    }

    private static ulong GetUInt64(JsonElement item, params string[] names)
    {
        foreach (string name in names)
        {
            if (item.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.Number && property.TryGetUInt64(out ulong value))
            {
                return value;
            }
        }

        return 0;
    }

    private static string ShortId(string id)
    {
        string normalized = id.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? id[7..] : id;
        return normalized.Length <= 12 ? normalized : normalized[..12];
    }

    private static string FormatEpoch(long epochSeconds)
    {
        return epochSeconds <= 0
            ? string.Empty
            : DateTimeOffset.FromUnixTimeSeconds(epochSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string FormatByteSize(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    private static double ParsePercent(string value)
    {
        string normalized = value.Replace("%", string.Empty, StringComparison.Ordinal).Trim();
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;
    }

    private static string DefaultIfWhiteSpace(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

internal sealed record CliContainerStats(double CpuPercent, string MemoryUsed);
