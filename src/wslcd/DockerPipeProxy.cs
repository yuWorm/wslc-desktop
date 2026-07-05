using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcDesktop.Contracts;
using WslcDesktop.Runtime;

namespace Wslcd;

[SupportedOSPlatform("windows")]
internal sealed partial class DockerPipeProxy
{
    private const int BufferSize = 64 * 1024;
    private const int MaxHeaderBytes = 128 * 1024;
    private readonly string _publicPipeName;
    private readonly string _internalPipeName;
    private readonly IContainerRuntimeProvider _runtimeProvider;
    private readonly ConcurrentDictionary<Task, byte> _connections = new();

    public DockerPipeProxy(string publicPipeName, string internalPipeName, IContainerRuntimeProvider runtimeProvider)
    {
        _publicPipeName = publicPipeName;
        _internalPipeName = internalPipeName;
        _runtimeProvider = runtimeProvider;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = CreateCurrentUserPipe(_publicPipeName);
                await pipe.WaitForConnectionAsync(cancellationToken);
                Task connection = Task.Run(() => HandleConnectionAsync(pipe, cancellationToken), CancellationToken.None);
                _connections[connection] = 0;
                _ = connection.ContinueWith(task => _connections.TryRemove(task, out _), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        if (!_connections.IsEmpty)
        {
            await Task.WhenAll(_connections.Keys);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream publicPipe, CancellationToken cancellationToken)
    {
        await using (publicPipe)
        {
            RequestPrelude prelude = await ReadRequestPreludeAsync(publicPipe, cancellationToken);
            if (prelude.IsExecStartUpgrade)
            {
                await HandleExecStartUpgradeAsync(publicPipe, prelude, cancellationToken);
                return;
            }

            await ProxyHttpAsync(publicPipe, prelude, cancellationToken);
        }
    }

    private async Task ProxyHttpAsync(
        NamedPipeServerStream publicPipe,
        RequestPrelude prelude,
        CancellationToken cancellationToken)
    {
        await using var internalPipe = new NamedPipeClientStream(".", _internalPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await internalPipe.ConnectAsync(5000, cancellationToken);
        await internalPipe.WriteAsync(prelude.Bytes, cancellationToken);
        await internalPipe.FlushAsync(cancellationToken);

        Task upstream = CopyPipeAsync(publicPipe, internalPipe, cancellationToken);
        Task downstream = CopyPipeAsync(internalPipe, publicPipe, cancellationToken);
        await Task.WhenAny(upstream, downstream);
    }

    private async Task HandleExecStartUpgradeAsync(
        NamedPipeServerStream publicPipe,
        RequestPrelude prelude,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] body = await ReadBodyAsync(publicPipe, prelude, cancellationToken);
            ExecStartOptionsDto options = ReadExecStartOptions(body);
            if (options.Detach)
            {
                _ = Task.Run(async () =>
                {
                    await foreach (var _ in _runtimeProvider.StartExecAsync(prelude.ExecId, options, CancellationToken.None))
                    {
                    }
                }, CancellationToken.None);

                await WriteResponseAsync(publicPipe, 204, "No Content", "text/plain", ReadOnlyMemory<byte>.Empty, cancellationToken);
                return;
            }

            await WriteTextAsync(
                publicPipe,
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: tcp\r\n" +
                "Content-Type: application/vnd.docker.raw-stream\r\n" +
                "\r\n",
                cancellationToken);

            await foreach (ExecFrameDto frame in _runtimeProvider.StartExecAsync(prelude.ExecId, options, cancellationToken))
            {
                byte[] bytes = EncodeRawFrame(
                    frame.Stream.Equals("stderr", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
                    frame.Message.EndsWith('\n') ? frame.Message : frame.Message + "\n");
                await publicPipe.WriteAsync(bytes, cancellationToken);
                await publicPipe.FlushAsync(cancellationToken);
            }

            TryWaitForPipeDrain(publicPipe);
        }
        catch (KeyNotFoundException ex)
        {
            await WriteDockerErrorAsync(publicPipe, 404, ex.Message, cancellationToken);
        }
        catch (RuntimeCommandException ex)
        {
            await WriteDockerErrorAsync(publicPipe, 500, ex.Message, cancellationToken);
        }
    }

    private static async Task<RequestPrelude> ReadRequestPreludeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var buffer = new byte[4096];
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Docker pipe client disconnected before sending HTTP headers.");
            }

            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            if (bytes.Count > MaxHeaderBytes)
            {
                throw new InvalidOperationException("Docker pipe request headers exceeded the maximum supported size.");
            }

            headerEnd = FindHeaderEnd(bytes);
        }

        string headerText = Encoding.ASCII.GetString(bytes.GetRange(0, headerEnd).ToArray());
        string[] lines = headerText.Split("\r\n", StringSplitOptions.None);
        string[] requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 3)
        {
            throw new InvalidOperationException("Invalid Docker pipe HTTP request line.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        string path = NormalizePath(requestLine[1]);
        Match match = ExecStartPathRegex().Match(path);
        bool upgrade = headers.TryGetValue("Connection", out string? connection) &&
            connection.Contains("Upgrade", StringComparison.OrdinalIgnoreCase) &&
            headers.ContainsKey("Upgrade");

        int contentLength = 0;
        if (headers.TryGetValue("Content-Length", out string? contentLengthText))
        {
            int.TryParse(contentLengthText, out contentLength);
        }

        return new RequestPrelude(
            bytes.ToArray(),
            headerEnd + 4,
            requestLine[0],
            path,
            headers,
            contentLength,
            match.Success && upgrade && requestLine[0].Equals("POST", StringComparison.OrdinalIgnoreCase),
            match.Success ? Uri.UnescapeDataString(match.Groups["id"].Value) : string.Empty);
    }

    private static async Task<byte[]> ReadBodyAsync(
        Stream stream,
        RequestPrelude prelude,
        CancellationToken cancellationToken)
    {
        if (prelude.ContentLength <= 0)
        {
            return [];
        }

        byte[] body = new byte[prelude.ContentLength];
        int copied = Math.Min(prelude.ContentLength, Math.Max(0, prelude.Bytes.Length - prelude.BodyOffset));
        if (copied > 0)
        {
            Array.Copy(prelude.Bytes, prelude.BodyOffset, body, 0, copied);
        }

        while (copied < body.Length)
        {
            int read = await stream.ReadAsync(body.AsMemory(copied), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Docker pipe client disconnected before sending the exec start body.");
            }

            copied += read;
        }

        return body;
    }

    private static ExecStartOptionsDto ReadExecStartOptions(byte[] body)
    {
        if (body.Length == 0)
        {
            return new ExecStartOptionsDto(false, false);
        }

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        return new ExecStartOptionsDto(GetJsonBool(root, "Detach"), GetJsonBool(root, "Tty"));
    }

    private static async Task CopyPipeAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    return;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static NamedPipeServerStream CreateCurrentUserPipe(string pipeName)
    {
        var security = new PipeSecurity();
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Could not determine the current Windows user for Docker pipe ACLs.");
        security.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }

    private static async Task WriteDockerErrorAsync(
        Stream stream,
        int statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["message"] = message
        }));
        await WriteResponseAsync(stream, statusCode, statusCode == 404 ? "Not Found" : "Internal Server Error", "application/json", body, cancellationToken);
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        int statusCode,
        string reason,
        string contentType,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        await WriteTextAsync(
            stream,
            $"HTTP/1.1 {statusCode} {reason}\r\nContent-Length: {body.Length}\r\nContent-Type: {contentType}\r\nConnection: close\r\n\r\n",
            cancellationToken);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private static async Task WriteTextAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static byte[] EncodeRawFrame(int streamType, string message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message);
        byte[] frame = new byte[8 + payload.Length];
        frame[0] = (byte)streamType;
        frame[4] = (byte)((payload.Length >> 24) & 0xff);
        frame[5] = (byte)((payload.Length >> 16) & 0xff);
        frame[6] = (byte)((payload.Length >> 8) & 0xff);
        frame[7] = (byte)(payload.Length & 0xff);
        Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);
        return frame;
    }

    private static int FindHeaderEnd(List<byte> bytes)
    {
        for (int i = 3; i < bytes.Count; i++)
        {
            if (bytes[i - 3] == '\r' &&
                bytes[i - 2] == '\n' &&
                bytes[i - 1] == '\r' &&
                bytes[i] == '\n')
            {
                return i - 3;
            }
        }

        return -1;
    }

    private static string NormalizePath(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
        {
            return uri.PathAndQuery;
        }

        return target;
    }

    private static bool GetJsonBool(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static void TryWaitForPipeDrain(NamedPipeServerStream pipe)
    {
        try
        {
            pipe.WaitForPipeDrain();
        }
        catch
        {
        }
    }

    [GeneratedRegex("^/(?:v[^/]+/)?exec/(?<id>[^/]+)/start(?:\\?.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecStartPathRegex();

    private sealed record RequestPrelude(
        byte[] Bytes,
        int BodyOffset,
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Headers,
        int ContentLength,
        bool IsExecStartUpgrade,
        string ExecId);
}
