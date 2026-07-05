using System.IO.Pipes;

namespace WslcDesktop.Runtime.Providers.DockerApi;

public static class DockerApiHttpClientFactory
{
    public static HttpClient Create(DockerApiProviderOptions options)
    {
        options.Validate();

        return options.IsNamedPipe
            ? CreateNamedPipeClient(options)
            : CreateTcpClient(options);
    }

    private static HttpClient CreateNamedPipeClient(DockerApiProviderOptions options)
    {
        string pipeName = ParsePipeName(options.Host);
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(1000, cancellationToken);
                return pipe;
            }
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
    }

    private static HttpClient CreateTcpClient(DockerApiProviderOptions options)
    {
        string host = options.Host.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase)
            ? "http://" + options.Host["tcp://".Length..]
            : options.Host;

        return new HttpClient
        {
            BaseAddress = new Uri(host.TrimEnd('/') + "/")
        };
    }

    private static string ParsePipeName(string host)
    {
        string normalized = host.Replace('\\', '/');
        const string marker = "/pipe/";
        int markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || markerIndex + marker.Length >= normalized.Length)
        {
            throw new InvalidOperationException($"Invalid named-pipe Docker host: {host}");
        }

        return Uri.UnescapeDataString(normalized[(markerIndex + marker.Length)..].Trim('/'));
    }
}
