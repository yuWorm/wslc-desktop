using System.Text.RegularExpressions;
using System.Text.Json;
using wslc_desktop.Models;

namespace wslc_desktop.Services;

public static class WslcCliOutputParser
{
    public static IReadOnlyList<ImageSummary> ParseImages(string output)
    {
        if (TryParseJsonImages(output, out var jsonImages))
        {
            return jsonImages;
        }

        var lines = GetContentLines(output);
        if (lines.Count == 0)
        {
            return [];
        }

        var header = SplitColumns(lines[0]);
        int repositoryIndex = IndexOfColumn(header, "REPOSITORY");
        int tagIndex = IndexOfColumn(header, "TAG");
        int idIndex = IndexOfColumn(header, "IMAGE ID");
        int createdIndex = IndexOfColumn(header, "CREATED");
        int sizeIndex = IndexOfColumn(header, "SIZE");

        if (repositoryIndex < 0 || tagIndex < 0 || idIndex < 0 || createdIndex < 0 || sizeIndex < 0)
        {
            return [];
        }

        var images = new List<ImageSummary>();
        foreach (string line in lines.Skip(1))
        {
            var values = SplitColumns(line);
            if (values.Count <= Math.Max(sizeIndex, Math.Max(createdIndex, idIndex)))
            {
                continue;
            }

            string repository = values[repositoryIndex];
            string tag = values[tagIndex];
            string id = values[idIndex];
            string created = values[createdIndex];
            string size = values[sizeIndex];

            if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            images.Add(new ImageSummary(id, repository, string.IsNullOrWhiteSpace(tag) ? "latest" : tag, size, created, false));
        }

        return images;
    }

    public static IReadOnlyList<ContainerSummary> ParseContainers(string output)
    {
        if (TryParseJsonContainers(output, out var jsonContainers))
        {
            return jsonContainers;
        }

        var lines = GetContentLines(output);
        if (lines.Count == 0)
        {
            return [];
        }

        int headerIndex = lines.FindIndex(IsContainerHeader);
        if (headerIndex < 0 || headerIndex == lines.Count - 1)
        {
            return [];
        }

        var columns = SplitColumns(lines[headerIndex]);
        int idIndex = IndexOfColumn(columns, "ID");
        int nameIndex = IndexOfColumn(columns, "NAME", "名称");
        int imageIndex = IndexOfColumn(columns, "IMAGE", "映像");
        int createdIndex = IndexOfColumn(columns, "CREATED", "已创建");
        int stateIndex = IndexOfColumn(columns, "STATUS", "状态");
        int portsIndex = IndexOfColumn(columns, "PORT", "端口");

        if (idIndex < 0 || nameIndex < 0 || imageIndex < 0 || createdIndex < 0 || stateIndex < 0)
        {
            return [];
        }

        var containers = new List<ContainerSummary>();
        foreach (string line in lines.Skip(headerIndex + 1))
        {
            var values = SplitColumns(line);
            if (values.Count <= Math.Max(stateIndex, Math.Max(createdIndex, imageIndex)))
            {
                continue;
            }

            string id = values[idIndex];
            string name = values[nameIndex];
            string image = values[imageIndex];
            string created = values[createdIndex];
            string state = values[stateIndex];
            string ports = portsIndex < 0 || portsIndex >= values.Count ? "-" : values[portsIndex];

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            containers.Add(new ContainerSummary(
                id,
                name,
                image,
                MapState(state),
                0,
                "-",
                created,
                state,
                string.IsNullOrWhiteSpace(ports) ? "-" : ports,
                "-",
                string.Empty));
        }

        return containers;
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

    public static IReadOnlyList<VolumeSummary> ParseVolumes(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        string trimmed = output.TrimStart();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(output);
            var volumes = new List<VolumeSummary>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                string name = GetString(item, "Name", "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string size = GetString(item, "Size", "size");
                string created = FormatEpoch(GetLong(item, "Created", "CreatedAt"));
                volumes.Add(new VolumeSummary(
                    name,
                    string.IsNullOrWhiteSpace(size) ? "-" : size,
                    "-",
                    string.IsNullOrWhiteSpace(created) ? "-" : created,
                    IsNamed: true));
            }

            return volumes;
        }

        var lines = GetContentLines(output);
        if (lines.Count <= 1)
        {
            return [];
        }

        var columns = SplitColumns(lines[0]);
        int nameIndex = IndexOfColumn(columns, "NAME", "VOLUME", "卷");
        if (nameIndex < 0)
        {
            nameIndex = 0;
        }

        return lines.Skip(1)
            .Select(SplitColumns)
            .Where(values => values.Count > nameIndex)
            .Select(values => new VolumeSummary(values[nameIndex], "-", "-", "-", IsNamed: true))
            .ToArray();
    }

    public static IReadOnlyList<NetworkEndpointSummary> ParsePublishedPorts(IReadOnlyList<ContainerSummary> containers)
    {
        var endpoints = new List<NetworkEndpointSummary>();

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

                int hostPort = int.Parse(match.Groups["host"].Value);
                int containerPort = int.Parse(match.Groups["container"].Value);
                string protocol = match.Groups["protocol"].Success ? match.Groups["protocol"].Value : "tcp";
                endpoints.Add(new NetworkEndpointSummary(
                    container.Name,
                    hostPort,
                    containerPort,
                    protocol,
                    protocol.Equals("udp", StringComparison.OrdinalIgnoreCase)
                        ? $"udp://localhost:{hostPort}"
                        : $"http://localhost:{hostPort}"));
            }
        }

        return endpoints;
    }

    private static bool TryParseJsonImages(string output, out IReadOnlyList<ImageSummary> images)
    {
        images = [];

        if (string.IsNullOrWhiteSpace(output) || !output.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        using var document = JsonDocument.Parse(output);
        var parsed = new List<ImageSummary>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            string repository = GetString(item, "Repository");
            string id = GetString(item, "Id", "ID");

            if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            parsed.Add(new ImageSummary(
                ShortId(id),
                repository,
                DefaultIfWhiteSpace(GetString(item, "Tag"), "latest"),
                FormatByteSize(GetUInt64(item, "Size")),
                DefaultIfWhiteSpace(FormatEpoch(GetLong(item, "Created", "CreatedAt")), "-"),
                false));
        }

        images = parsed;
        return true;
    }

    private static bool TryParseJsonContainers(string output, out IReadOnlyList<ContainerSummary> containers)
    {
        containers = [];

        if (string.IsNullOrWhiteSpace(output) || !output.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        using var document = JsonDocument.Parse(output);
        var parsed = new List<ContainerSummary>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            string id = GetString(item, "Id", "ID");
            string name = GetString(item, "Name");
            string image = GetString(item, "Image");

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            parsed.Add(new ContainerSummary(
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
                string.Empty));
        }

        containers = parsed;
        return true;
    }

    private static List<string> GetContentLines(string output)
    {
        return output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static IReadOnlyList<string> SplitColumns(string line)
    {
        return Regex.Split(line.Trim(), @"\s{2,}")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
    }

    private static bool IsContainerHeader(string line)
    {
        return (line.Contains("CONTAINER", StringComparison.OrdinalIgnoreCase) || line.Contains("容器", StringComparison.Ordinal))
            && (line.Contains("IMAGE", StringComparison.OrdinalIgnoreCase) || line.Contains("映像", StringComparison.Ordinal))
            && (line.Contains("STATUS", StringComparison.OrdinalIgnoreCase) || line.Contains("状态", StringComparison.Ordinal));
    }

    private static int IndexOfColumn(IReadOnlyList<string> columns, params string[] labels)
    {
        for (int index = 0; index < columns.Count; index++)
        {
            if (labels.Any(label => columns[index].Contains(label, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return -1;
    }

    private static ContainerRuntimeState MapState(string state)
    {
        if (state.Contains("running", StringComparison.OrdinalIgnoreCase) || state.Contains("运行", StringComparison.Ordinal))
        {
            return ContainerRuntimeState.Running;
        }

        if (state.Contains("created", StringComparison.OrdinalIgnoreCase) || state.Contains("创建", StringComparison.Ordinal))
        {
            return ContainerRuntimeState.Created;
        }

        if (state.Contains("stopped", StringComparison.OrdinalIgnoreCase) || state.Contains("停止", StringComparison.Ordinal))
        {
            return ContainerRuntimeState.Stopped;
        }

        if (state.Contains("exited", StringComparison.OrdinalIgnoreCase) || state.Contains("退出", StringComparison.Ordinal))
        {
            return ContainerRuntimeState.Exited;
        }

        return ContainerRuntimeState.Unknown;
    }

    private static ContainerRuntimeState MapState(JsonElement item)
    {
        if (item.TryGetProperty("State", out var stateProperty))
        {
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
                return MapState(stateProperty.GetString() ?? string.Empty);
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
        if (!item.TryGetProperty("StateChangedAt", out var changedAt) || !changedAt.TryGetInt64(out long value) || value <= 0)
        {
            return "-";
        }

        return FormatEpoch(value);
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
            : DateTimeOffset.FromUnixTimeSeconds(epochSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
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
        return double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;
    }

    private static string DefaultIfWhiteSpace(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

public sealed record CliContainerStats(double CpuPercent, string MemoryUsed);
