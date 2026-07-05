using System.IO.Pipes;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WslcDesktop.Contracts;

namespace WslcDesktop.DaemonClient;

public sealed class WslcdNativeClient : IWslcdNativeClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public WslcdNativeClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private WslcdNativeClient(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public static WslcdNativeClient CreateDefault()
    {
        return CreateForPipe(WslcdDefaults.NativePipeName);
    }

    public static WslcdNativeClient CreateForPipe(string pipeName)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(1000, cancellationToken);
                return pipe;
            }
        };

        return new WslcdNativeClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        }, ownsHttpClient: true);
    }

    public Task<WslcdHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<WslcdHealthResponse>("/v1/health", cancellationToken);
    }

    public Task<WslcdVersionResponse> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<WslcdVersionResponse>("/v1/version", cancellationToken);
    }

    public Task<RuntimeInfoResponse> GetRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<RuntimeInfoResponse>("/v1/runtime", cancellationToken);
    }

    public Task<ProviderPreviewResponse> PreviewProviderAsync(ProviderPreviewRequest request, CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<ProviderPreviewRequest, ProviderPreviewResponse>("/v1/providers/preview", request, cancellationToken);
    }

    public Task<IReadOnlyList<ContainerSummaryDto>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<IReadOnlyList<ContainerSummaryDto>>("/v1/containers", cancellationToken);
    }

    public Task<ContainerSummaryDto> InspectContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ContainerSummaryDto>($"/v1/containers/{EscapeSegment(idOrName)}", cancellationToken);
    }

    public Task<ContainerCreateResultDto> CreateContainerAsync(ContainerCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<ContainerCreateRequestDto, ContainerCreateResultDto>("/v1/containers", request, cancellationToken);
    }

    public Task StartContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return PostNoContentAsync($"/v1/containers/{EscapeSegment(idOrName)}/start", cancellationToken);
    }

    public Task StopContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return PostNoContentAsync($"/v1/containers/{EscapeSegment(idOrName)}/stop", cancellationToken);
    }

    public Task RemoveContainerAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return DeleteNoContentAsync($"/v1/containers/{EscapeSegment(idOrName)}", cancellationToken);
    }

    public Task<IReadOnlyList<ImageSummaryDto>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<IReadOnlyList<ImageSummaryDto>>("/v1/images", cancellationToken);
    }

    public Task<IReadOnlyList<ImagePullProgressDto>> PullImageAsync(ImagePullRequest request, CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<ImagePullRequest, IReadOnlyList<ImagePullProgressDto>>("/v1/images/pull", request, cancellationToken);
    }

    public async IAsyncEnumerable<ImagePullProgressDto> PullImageProgressAsync(
        ImagePullRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/images/pull/stream")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var frame = JsonSerializer.Deserialize<ImagePullProgressDto>(line, JsonOptions);
            if (frame is not null)
            {
                yield return frame;
            }
        }
    }

    public Task RemoveImageAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        return DeleteNoContentAsync($"/v1/images/{EscapeSegment(idOrName)}", cancellationToken);
    }

    public Task<IReadOnlyList<VolumeSummaryDto>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<IReadOnlyList<VolumeSummaryDto>>("/v1/volumes", cancellationToken);
    }

    public Task<VolumeSummaryDto> CreateVolumeAsync(VolumeCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<VolumeCreateRequestDto, VolumeSummaryDto>("/v1/volumes", request, cancellationToken);
    }

    public Task RemoveVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        return DeleteNoContentAsync($"/v1/volumes/{EscapeSegment(name)}", cancellationToken);
    }

    public Task<IReadOnlyList<NetworkEndpointSummaryDto>> ListNetworksAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<IReadOnlyList<NetworkEndpointSummaryDto>>("/v1/networks", cancellationToken);
    }

    public async IAsyncEnumerable<LogFrameDto> StreamLogsAsync(
        string idOrName,
        LogStreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string path = $"/v1/containers/{EscapeSegment(idOrName)}/logs?{BuildLogQuery(options)}";
        var frames = await GetJsonAsync<IReadOnlyList<LogFrameDto>>(path, cancellationToken);
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
        }
    }

    public Task<ExecCreateResultDto> CreateExecAsync(string idOrName, ExecCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<ExecCreateRequestDto, ExecCreateResultDto>($"/v1/containers/{EscapeSegment(idOrName)}/exec", request, cancellationToken);
    }

    public Task<IReadOnlyList<ExecFrameDto>> StartExecAsync(string execId, ExecStartOptionsDto options, CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<ExecStartOptionsDto, IReadOnlyList<ExecFrameDto>>($"/v1/exec/{EscapeSegment(execId)}/start", options, cancellationToken);
    }

    public Task<ExecDetailDto> InspectExecAsync(string execId, CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ExecDetailDto>($"/v1/exec/{EscapeSegment(execId)}", cancellationToken);
    }

    public Task<IReadOnlyList<OperationRecordDto>> ListOperationsAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<IReadOnlyList<OperationRecordDto>>("/v1/operations", cancellationToken);
    }

    public Task<WslcdDiagnosticsResponse> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<WslcdDiagnosticsResponse>("/v1/diagnostics", cancellationToken);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken);
        return await ReadJsonAsync<TResponse>(response, cancellationToken);
    }

    private async Task PostNoContentAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsync(path, content: null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task DeleteNoContentAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        T? result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("wslcd returned an empty JSON response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new WslcdClientException(response.StatusCode, body);
    }

    private static string BuildLogQuery(LogStreamOptions options)
    {
        var values = new List<string>
        {
            $"tail={options.Tail}",
            $"follow={Bool(options.Follow)}",
            $"timestamps={Bool(options.Timestamps)}"
        };

        if (options.Since is not null)
        {
            values.Add($"since={Uri.EscapeDataString(options.Since.Value.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))}");
        }

        if (options.Until is not null)
        {
            values.Add($"until={Uri.EscapeDataString(options.Until.Value.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))}");
        }

        return string.Join("&", values);
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string EscapeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Path segment value is required.", nameof(value));
        }

        return Uri.EscapeDataString(value.Trim());
    }
}
