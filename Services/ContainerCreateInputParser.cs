using wslc_desktop.Models;

namespace wslc_desktop.Services;

public static class ContainerCreateInputParser
{
    public static IReadOnlyList<string> ParseCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                AddCurrentPart(parts, current);
                continue;
            }

            current.Append(ch);
        }

        if (inQuotes)
        {
            throw new ArgumentException("Command contains an unmatched quote.");
        }

        AddCurrentPart(parts, current);
        return parts;
    }

    public static IReadOnlyList<PortMapping> ParsePortMappings(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var ports = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ParsePortMapping)
            .ToArray();

        var duplicate = ports
            .GroupBy(port => $"{port.HostPort}/{NormalizeProtocol(port.Protocol)}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate host port mapping: {duplicate.Key}.");
        }

        return ports;
    }

    public static IReadOnlyList<ContainerMount> ParseMounts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseMount)
            .ToArray();
    }

    public static IReadOnlyDictionary<string, string> ParseEnvironment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Dictionary<string, string>();
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int divider = part.IndexOf('=');
            if (divider <= 0)
            {
                throw new ArgumentException($"Invalid environment variable: {part}. Use KEY=value.");
            }

            string key = part[..divider].Trim();
            string value = part[(divider + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException($"Invalid environment variable: {part}. Key is required.");
            }

            if (!environment.TryAdd(key, value))
            {
                throw new ArgumentException($"Duplicate environment variable: {key}.");
            }
        }

        return environment;
    }

    private static PortMapping ParsePortMapping(string part)
    {
        string mapping = part;
        string protocol = "tcp";
        int slashIndex = part.LastIndexOf('/');

        if (slashIndex >= 0)
        {
            mapping = part[..slashIndex].Trim();
            protocol = NormalizeProtocol(part[(slashIndex + 1)..].Trim());
        }

        var pieces = mapping.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length != 2)
        {
            throw new ArgumentException($"Invalid port mapping: {part}. Use host:container or host:container/protocol.");
        }

        return new PortMapping(
            ParsePort(pieces[0], $"host port in {part}"),
            ParsePort(pieces[1], $"container port in {part}"),
            protocol);
    }

    private static ContainerMount ParseMount(string part)
    {
        int divider = part.IndexOf("=>", StringComparison.Ordinal);
        if (divider <= 0 || divider == part.Length - 2)
        {
            throw new ArgumentException($"Invalid mount mapping: {part}. Use source=>/container/path[:ro].");
        }

        string source = part[..divider].Trim();
        string targetAndMode = part[(divider + 2)..].Trim();
        bool isReadOnly = false;

        int modeIndex = targetAndMode.LastIndexOf(':');
        if (modeIndex > 0)
        {
            string mode = targetAndMode[(modeIndex + 1)..].Trim();
            if (mode.Equals("ro", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("rw", StringComparison.OrdinalIgnoreCase))
            {
                isReadOnly = mode.Equals("ro", StringComparison.OrdinalIgnoreCase);
                targetAndMode = targetAndMode[..modeIndex].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException($"Invalid mount mapping: {part}. Source is required.");
        }

        if (!targetAndMode.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid mount target: {targetAndMode}. Container paths must be absolute Linux paths.");
        }

        return new ContainerMount(source, targetAndMode, isReadOnly, IsNamedVolumeSource(source));
    }

    private static int ParsePort(string text, string label)
    {
        if (!int.TryParse(text, out int port) || port is < 1 or > 65535)
        {
            throw new ArgumentException($"Invalid {label}: {text}. Ports must be between 1 and 65535.");
        }

        return port;
    }

    private static string NormalizeProtocol(string protocol)
    {
        return protocol.ToLowerInvariant() switch
        {
            "tcp" => "tcp",
            "udp" => "udp",
            _ => throw new ArgumentException($"Unsupported port protocol: {protocol}. Use tcp or udp.")
        };
    }

    private static bool IsNamedVolumeSource(string source)
    {
        return !source.Contains(@":\", StringComparison.Ordinal)
            && !source.Contains(":/", StringComparison.Ordinal)
            && !source.Contains('/', StringComparison.Ordinal)
            && !source.Contains('\\', StringComparison.Ordinal)
            && !source.Contains('.', StringComparison.Ordinal)
            && !source.Contains('~', StringComparison.Ordinal);
    }

    private static void AddCurrentPart(List<string> parts, System.Text.StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        parts.Add(current.ToString());
        current.Clear();
    }
}
