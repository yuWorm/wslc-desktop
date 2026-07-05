using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using WslcDesktop.Contracts;

namespace WslcDesktop.Runtime.Providers.DockerApi;

public sealed class DockerApiProvider : IContainerRuntimeProvider, IDisposable
{
    public const string ProviderLabelKey = "com.wslc-desktop.provider";
    public const string ProviderLabelValue = "docker-api";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DockerApiProviderOptions _options;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public DockerApiProvider(DockerApiProviderOptions options)
        : this(options, DockerApiHttpClientFactory.Create(options), ownsClient: true)
    {
    }

    public DockerApiProvider(DockerApiProviderOptions options, HttpClient client, bool ownsClient = false)
    {
        _options = options;
        _client = client;
        _ownsClient = ownsClient;
    }

    public string Name => RuntimeProviderSelection.DockerApi;

    public async Task<RuntimeInfoResponse> GetRuntimeInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return new RuntimeInfoResponse(Name, false, string.Empty, Capabilities, "Docker API host is not configured.");
        }

        try
        {
            using HttpResponseMessage ping = await _client.GetAsync("/_ping", cancellationToken);
            if (!ping.IsSuccessStatusCode)
            {
                return new RuntimeInfoResponse(Name, false, string.Empty, Capabilities, $"Docker API ping returned HTTP {(int)ping.StatusCode}.");
            }

            using JsonDocument version = await GetJsonAsync("/version", cancellationToken);
            string engineVersion = GetString(version.RootElement, "Version", "ApiVersion");
            return new RuntimeInfoResponse(Name, true, engineVersion, Capabilities, $"Connected to Docker API host {_options.Host}.");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TimeoutException or InvalidOperationException)
        {
            return new RuntimeInfoResponse(Name, false, string.Empty, Capabilities, ex.Message);
        }
    }

    public async Task<IReadOnlyList<ContainerSummaryDto>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetJsonAsync("/containers/json?all=1", cancellationToken);
        return document.RootElement.EnumerateArray().Select(MapContainerSummary).ToArray();
    }

    public async Task<ContainerSummaryDto> InspectContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetJsonAsync($"/containers/{Escape(idOrName)}/json", cancellationToken);
        return MapContainerInspect(document.RootElement);
    }

    public async Task<ContainerCreateResultDto> CreateContainerAsync(ContainerCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        var labels = new Dictionary<string, string>(request.Labels ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            [ProviderLabelKey] = ProviderLabelValue
        };

        var hostConfig = new Dictionary<string, object?>();
        if (request.Mounts.Count > 0)
        {
            hostConfig["Binds"] = request.Mounts.Select(mount => $"{mount.Source}:{mount.Target}{(mount.IsReadOnly ? ":ro" : string.Empty)}").ToArray();
        }

        if (request.Ports.Count > 0)
        {
            hostConfig["PortBindings"] = BuildPortBindings(request.Ports);
        }

        var body = new Dictionary<string, object?>
        {
            ["Image"] = request.Image,
            ["Cmd"] = request.Command.Count == 0 ? null : request.Command,
            ["Env"] = request.Environment.Select(pair => $"{pair.Key}={pair.Value}").ToArray(),
            ["Labels"] = labels,
            ["HostConfig"] = hostConfig
        };

        string path = string.IsNullOrWhiteSpace(request.Name)
            ? "/containers/create"
            : $"/containers/create?name={Uri.EscapeDataString(request.Name.Trim())}";

        using HttpResponseMessage response = await _client.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        using JsonDocument document = await ReadJsonAsync(response, cancellationToken);
        string id = GetString(document.RootElement, "Id", "ID");
        var warnings = ReadStringArray(document.RootElement, "Warnings");
        return new ContainerCreateResultDto(id, warnings);
    }

    public Task StartContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return PostNoContentAsync($"/containers/{Escape(idOrName)}/start", cancellationToken);
    }

    public Task StopContainerAsync(string idOrName, ContainerStopOptionsDto options, CancellationToken cancellationToken = default)
    {
        string query = options.TimeoutSeconds > 0 ? $"?t={options.TimeoutSeconds}" : string.Empty;
        return PostNoContentAsync($"/containers/{Escape(idOrName)}/stop{query}", cancellationToken);
    }

    public Task RemoveContainerAsync(string idOrName, ContainerRemoveOptionsDto options, CancellationToken cancellationToken = default)
    {
        string query = $"?force={Bool(options.Force)}&v={Bool(options.RemoveVolumes)}";
        return DeleteNoContentAsync($"/containers/{Escape(idOrName)}{query}", cancellationToken);
    }

    public async Task<IReadOnlyList<ImageSummaryDto>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetJsonAsync("/images/json", cancellationToken);
        return document.RootElement.EnumerateArray().SelectMany(MapImageSummary).ToArray();
    }

    public async IAsyncEnumerable<ImagePullProgressDto> PullImageProgressAsync(
        string reference,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string image = string.IsNullOrWhiteSpace(reference)
            ? throw new ArgumentException("Image reference is required.", nameof(reference))
            : reference.Trim();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/images/create?fromImage={Uri.EscapeDataString(image)}");
        using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessStatusAsync(response, cancellationToken);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            foreach (var frame in DockerImagePullProgressParser.Parse(line))
            {
                yield return frame;
            }
        }
    }

    public async Task<IReadOnlyList<ImagePullProgressDto>> PullImageAsync(string reference, CancellationToken cancellationToken = default)
    {
        string image = string.IsNullOrWhiteSpace(reference)
            ? throw new ArgumentException("Image reference is required.", nameof(reference))
            : reference.Trim();

        var frames = new List<ImagePullProgressDto>();
        await foreach (var frame in PullImageProgressAsync(image, cancellationToken))
        {
            frames.Add(frame);
        }

        return frames.Count == 0
            ? [ImagePullProgressDto.StatusFrame(image, "Completed")]
            : frames;
    }

    public Task RemoveImageAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return DeleteNoContentAsync($"/images/{Escape(idOrName)}", cancellationToken);
    }

    public async Task<IReadOnlyList<VolumeSummaryDto>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetJsonAsync("/volumes", cancellationToken);
        if (!TryGetProperty(document.RootElement, "Volumes", out JsonElement volumes) || volumes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return volumes.EnumerateArray().Select(volume => new VolumeSummaryDto(
            GetString(volume, "Name"),
            "-",
            "-",
            GetString(volume, "CreatedAt"),
            true,
            ReadLabels(volume))).ToArray();
    }

    public async Task<VolumeSummaryDto> CreateVolumeAsync(string name, IReadOnlyDictionary<string, string>? labels = null, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync("/volumes/create", new { Name = name, Labels = labels ?? new Dictionary<string, string>() }, JsonOptions, cancellationToken);
        using JsonDocument document = await ReadJsonAsync(response, cancellationToken);
        return new VolumeSummaryDto(GetString(document.RootElement, "Name"), "-", "-", GetString(document.RootElement, "CreatedAt"), true, ReadLabels(document.RootElement));
    }

    public Task RemoveVolumeAsync(string name, bool force, CancellationToken cancellationToken = default)
    {
        return DeleteNoContentAsync($"/volumes/{Escape(name)}?force={Bool(force)}", cancellationToken);
    }

    public async Task<IReadOnlyList<NetworkEndpointSummaryDto>> ListNetworksAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument _ = await GetJsonAsync("/networks", cancellationToken);
        return [];
    }

    public async IAsyncEnumerable<LogFrameDto> StreamLogsAsync(
        string idOrName,
        LogStreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string query = $"stdout=1&stderr=1&tail={options.Tail}&timestamps={Bool(options.Timestamps)}&follow={Bool(options.Follow)}";
        using HttpResponseMessage response = await _client.GetAsync($"/containers/{Escape(idOrName)}/logs?{query}", cancellationToken);
        byte[] bytes = await EnsureBytesAsync(response, cancellationToken);
        string text = DecodeDockerRawStream(bytes);
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return new LogFrameDto(DateTimeOffset.Now, "stdout", line);
        }
    }

    public async IAsyncEnumerable<StatsFrameDto> StreamStatsAsync(
        string idOrName,
        StatsStreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string stream = options.Stream ? "1" : "0";
        using JsonDocument document = await GetJsonAsync($"/containers/{Escape(idOrName)}/stats?stream={stream}", cancellationToken);
        yield return new StatsFrameDto(
            idOrName,
            GetString(document.RootElement, "name"),
            0,
            "-",
            DateTimeOffset.Now);
    }

    public async Task<ExecCreateResultDto> CreateExecAsync(string idOrName, ExecCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            AttachStdout = request.AttachStdout,
            AttachStderr = request.AttachStderr,
            AttachStdin = !string.IsNullOrEmpty(request.StandardInput),
            Tty = request.Tty,
            Cmd = request.Command
        };

        using HttpResponseMessage response = await _client.PostAsJsonAsync($"/containers/{Escape(idOrName)}/exec", body, JsonOptions, cancellationToken);
        using JsonDocument document = await ReadJsonAsync(response, cancellationToken);
        return new ExecCreateResultDto(GetString(document.RootElement, "Id", "ID"));
    }

    public async IAsyncEnumerable<ExecFrameDto> StartExecAsync(
        string execId,
        ExecStartOptionsDto options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync($"/exec/{Escape(execId)}/start", new { Detach = options.Detach, Tty = options.Tty }, JsonOptions, cancellationToken);
        byte[] bytes = await EnsureBytesAsync(response, cancellationToken);
        yield return new ExecFrameDto(DateTimeOffset.Now, "stdout", DecodeDockerRawStream(bytes));
    }

    public async Task<ExecDetailDto> InspectExecAsync(string execId, CancellationToken cancellationToken = default)
    {
        using JsonDocument document = await GetJsonAsync($"/exec/{Escape(execId)}/json", cancellationToken);
        return new ExecDetailDto(
            GetString(document.RootElement, "ID", "Id"),
            GetBool(document.RootElement, "Running"),
            TryGetProperty(document.RootElement, "ExitCode", out JsonElement exitCode) && exitCode.ValueKind == JsonValueKind.Number ? exitCode.GetInt32() : null,
            GetBool(document.RootElement, "Tty"));
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static RuntimeCapabilities Capabilities => new(
        ListContainers: true,
        InspectContainer: true,
        CreateContainer: true,
        StartStopContainer: true,
        RemoveContainer: true,
        NamedVolumes: true,
        BindMounts: true,
        PortPublishing: true,
        RestartPolicy: true,
        LogsBuffered: true,
        LogsFollow: true,
        StatsOneShot: true,
        StatsStreaming: true,
        ExecNonInteractive: true,
        ExecInteractiveTty: true,
        DockerNetworkCrud: true,
        ImagePull: true,
        ImagePullProgress: true,
        ImageRemove: true,
        ImagePush: false,
        ImageBuild: false);

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.GetAsync(path, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await EnsureStringAsync(response, cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    private async Task PostNoContentAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.PostAsync(path, content: null, cancellationToken);
        await EnsureStringAsync(response, cancellationToken);
    }

    private async Task DeleteNoContentAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.DeleteAsync(path, cancellationToken);
        await EnsureStringAsync(response, cancellationToken);
    }

    private static async Task<string> EnsureStringAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
            ? $"Docker API returned HTTP {(int)response.StatusCode}."
            : body);
    }

    private static async Task EnsureSuccessStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
            ? $"Docker API returned HTTP {(int)response.StatusCode}."
            : body);
    }

    private static async Task<byte[]> EnsureBytesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return bytes;
        }

        throw new InvalidOperationException(Encoding.UTF8.GetString(bytes));
    }

    private static ContainerSummaryDto MapContainerSummary(JsonElement item)
    {
        string id = NormalizeId(GetString(item, "Id", "ID"));
        string name = ReadFirstString(item, "Names").TrimStart('/');
        string portSummary = ReadDockerPorts(item);
        return new ContainerSummaryDto(
            id,
            string.IsNullOrWhiteSpace(name) ? id : name,
            GetString(item, "Image"),
            MapState(GetString(item, "State", "Status")),
            0,
            "-",
            MapUnixTime(item, "Created"),
            GetString(item, "Status"),
            portSummary,
            GetString(item, "Command"),
            item.GetRawText(),
            ReadLabels(item));
    }

    private static ContainerSummaryDto MapContainerInspect(JsonElement item)
    {
        string id = NormalizeId(GetString(item, "Id", "ID"));
        string name = GetString(item, "Name").TrimStart('/');
        JsonElement config = TryGetProperty(item, "Config", out JsonElement configElement) ? configElement : default;
        JsonElement state = TryGetProperty(item, "State", out JsonElement stateElement) ? stateElement : default;
        return new ContainerSummaryDto(
            id,
            string.IsNullOrWhiteSpace(name) ? id : name,
            config.ValueKind == JsonValueKind.Object ? GetString(config, "Image") : string.Empty,
            state.ValueKind == JsonValueKind.Object ? MapState(GetString(state, "Status")) : ContainerRuntimeState.Unknown,
            0,
            "-",
            GetString(item, "Created"),
            state.ValueKind == JsonValueKind.Object ? GetString(state, "Status") : string.Empty,
            "-",
            config.ValueKind == JsonValueKind.Object ? string.Join(" ", ReadStringArray(config, "Cmd")) : string.Empty,
            item.GetRawText(),
            config.ValueKind == JsonValueKind.Object ? ReadLabels(config) : new Dictionary<string, string>());
    }

    private static Dictionary<string, string> ReadLabels(JsonElement item)
    {
        if (!TryGetProperty(item, "Labels", out JsonElement labels) || labels.ValueKind != JsonValueKind.Object)
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

    private static IEnumerable<ImageSummaryDto> MapImageSummary(JsonElement item)
    {
        string id = NormalizeId(GetString(item, "Id", "ID"));
        string created = MapUnixTime(item, "Created");
        string size = FormatBytes(TryGetProperty(item, "Size", out JsonElement sizeElement) && sizeElement.ValueKind == JsonValueKind.Number ? sizeElement.GetInt64() : 0);
        var tags = ReadStringArray(item, "RepoTags");
        if (tags.Count == 0)
        {
            tags = ["<none>:<none>"];
        }

        foreach (string tag in tags)
        {
            int separator = tag.LastIndexOf(':');
            string repository = separator > 0 ? tag[..separator] : tag;
            string imageTag = separator > 0 ? tag[(separator + 1)..] : "latest";
            yield return new ImageSummaryDto(id, repository, imageTag, size, created, false);
        }
    }

    private static Dictionary<string, object?> BuildPortBindings(IReadOnlyList<PortMappingDto> ports)
    {
        var bindings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in ports)
        {
            bindings[$"{port.ContainerPort}/{port.Protocol}"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["HostIp"] = "0.0.0.0",
                    ["HostPort"] = port.HostPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            };
        }

        return bindings;
    }

    private static string ReadDockerPorts(JsonElement item)
    {
        if (!TryGetProperty(item, "Ports", out JsonElement ports) || ports.ValueKind != JsonValueKind.Array)
        {
            return "-";
        }

        var results = new List<string>();
        foreach (var port in ports.EnumerateArray())
        {
            string publicPort = GetString(port, "PublicPort");
            string privatePort = GetString(port, "PrivatePort");
            string type = GetString(port, "Type");
            if (!string.IsNullOrWhiteSpace(publicPort) && !string.IsNullOrWhiteSpace(privatePort))
            {
                results.Add($"{publicPort}->{privatePort}/{(string.IsNullOrWhiteSpace(type) ? "tcp" : type)}");
            }
        }

        return results.Count == 0 ? "-" : string.Join(", ", results);
    }

    private static string DecodeDockerRawStream(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        var builder = new StringBuilder();
        int offset = 0;
        while (offset + 8 <= bytes.Length)
        {
            int length = (bytes[offset + 4] << 24) | (bytes[offset + 5] << 16) | (bytes[offset + 6] << 8) | bytes[offset + 7];
            if (length < 0 || offset + 8 + length > bytes.Length)
            {
                return Encoding.UTF8.GetString(bytes);
            }

            builder.Append(Encoding.UTF8.GetString(bytes, offset + 8, length));
            offset += 8 + length;
        }

        return builder.ToString();
    }

    private static ContainerRuntimeState MapState(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "created" => ContainerRuntimeState.Created,
            "running" => ContainerRuntimeState.Running,
            "exited" => ContainerRuntimeState.Exited,
            "dead" => ContainerRuntimeState.Exited,
            "paused" => ContainerRuntimeState.Stopped,
            _ => ContainerRuntimeState.Unknown
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string ReadFirstString(JsonElement root, string propertyName)
    {
        return ReadStringArray(root, propertyName).FirstOrDefault() ?? string.Empty;
    }

    private static bool GetBool(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.True;
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

    private static string MapUnixTime(JsonElement item, string propertyName)
    {
        if (!TryGetProperty(item, propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Number)
        {
            return GetString(item, propertyName);
        }

        long seconds = property.GetInt64();
        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string NormalizeId(string id)
    {
        string normalized = id.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? id[7..] : id;
        return normalized.Length <= 12 ? normalized : normalized[..12];
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000)
        {
            return $"{bytes / 1_000_000_000d:0.##} GB";
        }

        if (bytes >= 1_000_000)
        {
            return $"{bytes / 1_000_000d:0.##} MB";
        }

        if (bytes >= 1_000)
        {
            return $"{bytes / 1_000d:0.##} KB";
        }

        return $"{bytes} B";
    }

    private static string Escape(string value)
    {
        return Uri.EscapeDataString(string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Docker API id is required.", nameof(value)) : value.Trim());
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }
}
