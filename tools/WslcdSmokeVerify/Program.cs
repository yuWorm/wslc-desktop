using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

const string NativePipe = "wslc-desktop";
const string DockerPipe = "wslc-desktop-docker";

string mode = args.FirstOrDefault() ?? "smoke";
string providerMode = args.Skip(1).FirstOrDefault() ?? string.Empty;
string root = FindRepositoryRoot();
string daemonProject = Path.Combine(root, "src", "wslcd", "wslcd.csproj");

if (mode.Equals("package", StringComparison.OrdinalIgnoreCase))
{
    string daemonPath = args.Skip(1).FirstOrDefault()
        ?? throw new ArgumentException("Packaged daemon path is required.");
    await VerifyPackagedDaemonAsync(root, daemonPath);
    return;
}

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
using var daemon = StartDaemon(root, daemonProject);
int exitCode = 0;

try
{
    await VerifySmokeAsync(daemon, NativePipe, DockerPipe, cts.Token);
    string successMarker = "WSLCD_SMOKE_OK";

    if (mode.Equals("runtime", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyRuntimeAsync(cts.Token);
        successMarker = "WSLCD_RUNTIME_OK";
    }
    else if (mode.Equals("full", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyRuntimeAsync(cts.Token);
        successMarker = "WSLCD_FULL_SMOKE_OK";
    }
    else if (mode.Equals("provider", StringComparison.OrdinalIgnoreCase) &&
        providerMode.Equals("docker-api", StringComparison.OrdinalIgnoreCase))
    {
        bool configured = await VerifyDockerApiProviderAsync(root, cts.Token);
        successMarker = configured ? "WSLCD_DOCKER_PROVIDER_OK" : "WSLCD_DOCKER_PROVIDER_UNCONFIGURED";
    }

    await StopDaemonAsync(daemon);
    Console.WriteLine(successMarker);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    exitCode = 1;
}
finally
{
    await CleanupDaemonAsync(daemon);
}

if (exitCode != 0)
{
    Environment.Exit(exitCode);
}

static async Task VerifyPackagedDaemonAsync(string root, string daemonPath)
{
    string fullDaemonPath = Path.GetFullPath(daemonPath);
    if (!File.Exists(fullDaemonPath))
    {
            throw new FileNotFoundException("Packaged wslcd-desktop executable was not found.", fullDaemonPath);
    }

    string suffix = Guid.NewGuid().ToString("N")[..8];
    string nativePipe = $"wslc-desktop-package-{suffix}";
    string dockerPipe = $"wslc-desktop-docker-package-{suffix}";
    string logDirectory = Path.Combine(root, "artifacts", "wslcd-package-smoke", suffix);
    Directory.CreateDirectory(logDirectory);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var daemon = StartDaemonTarget(fullDaemonPath, Path.GetDirectoryName(fullDaemonPath)!, logDirectory, nativePipe, dockerPipe);

    try
    {
        await VerifySmokeAsync(daemon, nativePipe, dockerPipe, cts.Token);
        await StopDaemonAsync(daemon, nativePipe);
        Console.WriteLine("WSLCD_PACKAGED_DAEMON_OK");
    }
    finally
    {
        await CleanupDaemonAsync(daemon, nativePipe);
    }
}

static async Task VerifySmokeAsync(Process daemon, string nativePipe, string dockerPipe, CancellationToken cancellationToken)
{
    string healthz = await WaitForTextAsync(daemon, nativePipe, "/healthz", cancellationToken);
    RequireEquals(healthz, "OK", "native /healthz did not return OK.");

    using JsonDocument healthJson = await GetJsonAsync(nativePipe, "/v1/health", cancellationToken);
    RequireJsonString(healthJson, "status", "Healthy");
    RequireJsonString(healthJson, "nativePipeName", nativePipe);
    RequireJsonString(healthJson, "dockerPipeName", dockerPipe);

    using JsonDocument versionJson = await GetJsonAsync(nativePipe, "/v1/version", cancellationToken);
    RequireJsonString(versionJson, "productName", "WSLC Desktop");
    RequireJsonString(versionJson, "daemonName", "wslcd-desktop");

    string dockerPing = await WaitForTextAsync(daemon, dockerPipe, "/_ping", cancellationToken);
    RequireEquals(dockerPing, "OK", "docker /_ping did not return OK.");

    using JsonDocument dockerVersionJson = await GetJsonAsync(dockerPipe, "/version", cancellationToken);
    RequireJsonString(dockerVersionJson, "ApiVersion", "1.54");
    RequireJsonString(dockerVersionJson, "MinAPIVersion", "1.40");
}

static async Task VerifyRuntimeAsync(CancellationToken cancellationToken)
{
    using JsonDocument runtime = await GetJsonAsync(NativePipe, "/v1/runtime", cancellationToken);
    RequireJsonString(runtime, "providerName", "wslc-cli");
    RequireJsonBoolean(runtime, "isAvailable", true);

    using JsonDocument daemonImages = await GetJsonAsync(NativePipe, "/v1/images", cancellationToken);
    using JsonDocument cliImages = RunWslcJsonArray("image list --format json");
    RequireImageParity(cliImages, daemonImages);

    using JsonDocument daemonContainers = await GetJsonAsync(NativePipe, "/v1/containers", cancellationToken);
    using JsonDocument cliContainers = RunWslcJsonArray("container list --all --format json");
    RequireContainerParity(cliContainers, daemonContainers);
}

static async Task<bool> VerifyDockerApiProviderAsync(string root, CancellationToken cancellationToken)
{
    string host = Environment.GetEnvironmentVariable("WSLCD_DOCKER_API_HOST") ?? string.Empty;
    bool allowTcp = IsTrue(Environment.GetEnvironmentVariable("WSLCD_DOCKER_API_ALLOW_TCP"));
    using JsonDocument preview = await PostJsonAsync(NativePipe, "/v1/providers/preview", new Dictionary<string, object?>
    {
        ["runtimeProviderName"] = "docker-api",
        ["dockerApiHost"] = host,
        ["allowTcpDockerApi"] = allowTcp
    }, cancellationToken);

    RequireJsonString(preview, "runtimeProviderName", "docker-api");
    if (string.IsNullOrWhiteSpace(host))
    {
        RequireJsonBoolean(preview, "isConfigured", false);
        RequireJsonBoolean(preview, "isAvailable", false);
        return false;
    }

    RequireJsonBoolean(preview, "isConfigured", true);
    RequireJsonBoolean(preview, "isAvailable", true);
    await VerifyDockerApiProviderDaemonAsync(root, host, allowTcp, cancellationToken);
    await VerifyRuntimeAsync(cancellationToken);
    return true;
}

static async Task VerifyDockerApiProviderDaemonAsync(
    string root,
    string host,
    bool allowTcp,
    CancellationToken cancellationToken)
{
    string explicitDaemonPath = Environment.GetEnvironmentVariable("WSLCD_SMOKE_DAEMON_PATH") ?? string.Empty;
    if (string.IsNullOrWhiteSpace(explicitDaemonPath))
    {
        return;
    }

    string fullDaemonPath = Path.GetFullPath(explicitDaemonPath);
    if (!File.Exists(fullDaemonPath))
    {
        throw new FileNotFoundException("Explicit WSLCD_SMOKE_DAEMON_PATH was not found.", fullDaemonPath);
    }

    string suffix = Guid.NewGuid().ToString("N")[..8];
    string providerNativePipe = $"wslc-desktop-provider-{suffix}";
    string providerDockerPipe = $"wslc-desktop-provider-docker-{suffix}";
    string logDirectory = Path.Combine(root, "artifacts", "wslcd-provider-smoke", suffix);
    Directory.CreateDirectory(logDirectory);

    using var providerDaemon = StartDaemonTarget(
        fullDaemonPath,
        Path.GetDirectoryName(fullDaemonPath)!,
        logDirectory,
        providerNativePipe,
        providerDockerPipe,
        startInfo =>
        {
            startInfo.Environment["WSLCD_RUNTIME_PROVIDER"] = "docker-api";
            startInfo.Environment["WSLCD_DOCKER_API_HOST"] = host;
            startInfo.Environment["WSLCD_DOCKER_API_ALLOW_TCP"] = allowTcp ? "1" : "0";
        });

    try
    {
        await VerifySmokeAsync(providerDaemon, providerNativePipe, providerDockerPipe, cancellationToken);

        using JsonDocument runtime = await GetJsonAsync(providerNativePipe, "/v1/runtime", cancellationToken);
        RequireJsonString(runtime, "providerName", "docker-api");
        RequireJsonBoolean(runtime, "isAvailable", true);

        using JsonDocument providerImages = await GetJsonAsync(providerNativePipe, "/v1/images", cancellationToken);
        using JsonDocument providerContainers = await GetJsonAsync(providerNativePipe, "/v1/containers", cancellationToken);

        if (host.Equals($"npipe:////./pipe/{DockerPipe}", StringComparison.OrdinalIgnoreCase))
        {
            using JsonDocument wslcImages = await GetJsonAsync(NativePipe, "/v1/images", cancellationToken);
            using JsonDocument wslcContainers = await GetJsonAsync(NativePipe, "/v1/containers", cancellationToken);
            RequireNativeImageParity(wslcImages, providerImages);
            RequireNativeContainerParity(wslcContainers, providerContainers);
        }

        await StopDaemonAsync(providerDaemon, providerNativePipe);
    }
    finally
    {
        await CleanupDaemonAsync(providerDaemon, providerNativePipe);
    }
}

static Process StartDaemon(string root, string daemonProject)
{
    string logDirectory = Path.Combine(root, "artifacts", "wslcd-smoke", "Diagnostics");
    Directory.CreateDirectory(logDirectory);

    string explicitDaemonPath = Environment.GetEnvironmentVariable("WSLCD_SMOKE_DAEMON_PATH") ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(explicitDaemonPath))
    {
        string fullDaemonPath = Path.GetFullPath(explicitDaemonPath);
        if (!File.Exists(fullDaemonPath))
        {
            throw new FileNotFoundException("Explicit WSLCD_SMOKE_DAEMON_PATH was not found.", fullDaemonPath);
        }

        return StartDaemonTarget(fullDaemonPath, Path.GetDirectoryName(fullDaemonPath)!, logDirectory, NativePipe, DockerPipe);
    }

    string daemonDll = Path.Combine(root, "src", "wslcd", "bin", "Debug", "net10.0", "wslcd-desktop.dll");
    string legacyDaemonDll = Path.Combine(root, "src", "wslcd", "bin", "Debug", "net10.0", "wslcd.dll");
    if (!File.Exists(daemonDll))
    {
        BuildDaemon(root, daemonProject);
    }

    if (!File.Exists(daemonDll) && File.Exists(legacyDaemonDll))
    {
        daemonDll = legacyDaemonDll;
    }

    if (!File.Exists(daemonDll))
    {
        throw new FileNotFoundException("Built wslcd-desktop.dll was not found.", daemonDll);
    }

    string publishedDaemon = Path.Combine(root, "artifacts", "wslcd-singlefile", "wslcd-desktop.exe");
    string legacyPublishedDaemon = Path.Combine(root, "artifacts", "wslcd-singlefile", "wslcd.exe");

    var candidates = new (string FileName, string Arguments, string PathToCheck)[]
    {
        ("dotnet", $"\"{daemonDll}\"", daemonDll),
        (publishedDaemon, string.Empty, publishedDaemon),
        (legacyPublishedDaemon, string.Empty, legacyPublishedDaemon)
    };

    Exception? lastError = null;
    foreach (var candidate in candidates.Where(candidate => File.Exists(candidate.PathToCheck)))
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = candidate.FileName,
                Arguments = candidate.Arguments,
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.Environment["WSLCD_LOG_DIRECTORY"] = logDirectory;
            startInfo.Environment["WSLCD_ENABLE_TEST_SHUTDOWN"] = "1";

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start wslcd-desktop candidate '{candidate.PathToCheck}'.");

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Console.WriteLine($"[wslcd-desktop stdout] {e.Data}");
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    Console.WriteLine($"[wslcd-desktop stderr] {e.Data}");
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            lastError = ex;
        }
    }

    throw new InvalidOperationException("Failed to start any available wslcd-desktop candidate.", lastError);
}

static Process StartDaemonTarget(
    string daemonPath,
    string workingDirectory,
    string logDirectory,
    string nativePipe,
    string dockerPipe,
    Action<ProcessStartInfo>? configureStartInfo = null)
{
    string fileName = daemonPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        ? "dotnet"
        : daemonPath;
    string arguments = daemonPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        ? $"\"{daemonPath}\""
        : string.Empty;

    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.Environment["WSLCD_LOG_DIRECTORY"] = logDirectory;
    startInfo.Environment["WSLCD_ENABLE_TEST_SHUTDOWN"] = "1";
    startInfo.Environment["WSLCD_NATIVE_PIPE_NAME"] = nativePipe;
    startInfo.Environment["WSLCD_DOCKER_PIPE_NAME"] = dockerPipe;
    configureStartInfo?.Invoke(startInfo);

    var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start packaged wslcd-desktop target '{daemonPath}'.");

    process.OutputDataReceived += (_, e) =>
    {
        if (e.Data is not null)
        {
            Console.WriteLine($"[packaged wslcd-desktop stdout] {e.Data}");
        }
    };
    process.ErrorDataReceived += (_, e) =>
    {
        if (e.Data is not null)
        {
            Console.WriteLine($"[packaged wslcd-desktop stderr] {e.Data}");
        }
    };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

static void BuildDaemon(string root, string daemonProject)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"build --no-restore \"{daemonProject}\"",
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start dotnet build for wslcd-desktop.");

    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    process.WaitForExit(60000);

    if (!process.HasExited)
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException("Timed out building wslcd-desktop.");
    }

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"wslcd-desktop build failed with exit code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }
}

static async Task<string> WaitForTextAsync(Process daemon, string pipeName, string path, CancellationToken cancellationToken)
{
    Exception? last = null;
    while (!cancellationToken.IsCancellationRequested)
    {
        if (daemon.HasExited)
        {
            throw new InvalidOperationException($"wslcd-desktop exited before pipe '{pipeName}' became available. Exit code: {daemon.ExitCode}");
        }

        try
        {
            using HttpClient client = CreatePipeHttpClient(pipeName);
            return await client.GetStringAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or HttpRequestException)
        {
            last = ex;
            await Task.Delay(250, cancellationToken);
        }
    }

    throw new TimeoutException($"Timed out waiting for pipe '{pipeName}' path '{path}'.", last);
}

static async Task<JsonDocument> GetJsonAsync(string pipeName, string path, CancellationToken cancellationToken)
{
    using HttpClient client = CreatePipeHttpClient(pipeName);
    using HttpResponseMessage response = await client.GetAsync(path, cancellationToken);
    if (response.StatusCode != HttpStatusCode.OK)
    {
        throw new InvalidOperationException($"Expected HTTP 200 for {pipeName}{path}, got {(int)response.StatusCode}.");
    }

    string body = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonDocument.Parse(body);
}

static async Task<JsonDocument> PostJsonAsync(string pipeName, string path, object body, CancellationToken cancellationToken)
{
    using HttpClient client = CreatePipeHttpClient(pipeName);
    using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    using HttpResponseMessage response = await client.PostAsync(path, content, cancellationToken);
    if (response.StatusCode != HttpStatusCode.OK)
    {
        throw new InvalidOperationException($"Expected HTTP 200 for {pipeName}{path}, got {(int)response.StatusCode}.");
    }

    string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonDocument.Parse(responseBody);
}

static HttpClient CreatePipeHttpClient(string pipeName)
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

    return new HttpClient(handler)
    {
        BaseAddress = new Uri("http://localhost")
    };
}

static void RequireJsonString(JsonDocument document, string propertyName, string expected)
{
    if (!document.RootElement.TryGetProperty(propertyName, out JsonElement property) ||
        property.ValueKind != JsonValueKind.String ||
        !string.Equals(property.GetString(), expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected JSON property '{propertyName}' to be '{expected}', got: {document.RootElement}");
    }
}

static void RequireJsonBoolean(JsonDocument document, string propertyName, bool expected)
{
    if (!document.RootElement.TryGetProperty(propertyName, out JsonElement property) ||
        property.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
        property.GetBoolean() != expected)
    {
        throw new InvalidOperationException($"Expected JSON property '{propertyName}' to be '{expected}', got: {document.RootElement}");
    }
}

static void RequireEquals(string value, string expected, string message)
{
    if (!string.Equals(value.Trim(), expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message}{Environment.NewLine}Actual: {value}");
    }
}

static JsonDocument RunWslcJsonArray(string arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "wslc",
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start wslc.");

    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    process.WaitForExit(30000);

    if (!process.HasExited)
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException($"wslc {arguments} timed out.");
    }

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"wslc {arguments} failed with exit code {process.ExitCode}.{Environment.NewLine}{stderr}");
    }

    return JsonDocument.Parse(stdout);
}

static void RequireImageParity(JsonDocument cliImages, JsonDocument daemonImages)
{
    var expected = cliImages.RootElement.EnumerateArray()
        .Select(image => $"{NormalizeId(GetString(image, "Id", "ID"))}|{GetString(image, "Repository")}|{DefaultIfWhiteSpace(GetString(image, "Tag"), "latest")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var actual = daemonImages.RootElement.EnumerateArray()
        .Select(image => $"{NormalizeId(GetString(image, "id"))}|{GetString(image, "repository")}|{DefaultIfWhiteSpace(GetString(image, "tag"), "latest")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    RequireSequenceEqual(expected, actual, "Image parity failed.");
}

static void RequireContainerParity(JsonDocument cliContainers, JsonDocument daemonContainers)
{
    var expected = cliContainers.RootElement.EnumerateArray()
        .Select(container => $"{NormalizeId(GetString(container, "Id", "ID"))}|{GetString(container, "Name")}|{GetString(container, "Image")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var actual = daemonContainers.RootElement.EnumerateArray()
        .Select(container => $"{NormalizeId(GetString(container, "id"))}|{GetString(container, "name")}|{GetString(container, "image")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    RequireSequenceEqual(expected, actual, "Container parity failed.");
}

static void RequireNativeImageParity(JsonDocument expectedImages, JsonDocument actualImages)
{
    var expected = expectedImages.RootElement.EnumerateArray()
        .Select(image => $"{NormalizeId(GetString(image, "id"))}|{GetString(image, "repository")}|{DefaultIfWhiteSpace(GetString(image, "tag"), "latest")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var actual = actualImages.RootElement.EnumerateArray()
        .Select(image => $"{NormalizeId(GetString(image, "id"))}|{GetString(image, "repository")}|{DefaultIfWhiteSpace(GetString(image, "tag"), "latest")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    RequireSequenceEqual(expected, actual, "Docker API provider image parity failed.");
}

static void RequireNativeContainerParity(JsonDocument expectedContainers, JsonDocument actualContainers)
{
    var expected = expectedContainers.RootElement.EnumerateArray()
        .Select(container => $"{NormalizeId(GetString(container, "id"))}|{GetString(container, "name")}|{GetString(container, "image")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var actual = actualContainers.RootElement.EnumerateArray()
        .Select(container => $"{NormalizeId(GetString(container, "id"))}|{GetString(container, "name")}|{GetString(container, "image")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    RequireSequenceEqual(expected, actual, "Docker API provider container parity failed.");
}

static void RequireSequenceEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string message)
{
    if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"{message}{Environment.NewLine}Expected:{Environment.NewLine}{string.Join(Environment.NewLine, expected)}{Environment.NewLine}Actual:{Environment.NewLine}{string.Join(Environment.NewLine, actual)}");
    }
}

static string GetString(JsonElement item, params string[] names)
{
    foreach (string name in names)
    {
        if (!item.TryGetProperty(name, out JsonElement property))
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

static string NormalizeId(string id)
{
    string normalized = id.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? id[7..] : id;
    return normalized.Length <= 12 ? normalized : normalized[..12];
}

static string DefaultIfWhiteSpace(string value, string fallback)
{
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
}

static bool IsTrue(string? value)
{
    return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}

static async Task StopDaemonAsync(Process daemon, string pipeName = NativePipe)
{
    if (daemon.HasExited)
    {
        throw new InvalidOperationException($"wslcd-desktop exited before verification completed with code {daemon.ExitCode}.");
    }

    await CleanupDaemonAsync(daemon, pipeName);
}

static async Task CleanupDaemonAsync(Process daemon, string pipeName = NativePipe)
{
    if (daemon.HasExited)
    {
        return;
    }

    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using HttpClient client = CreatePipeHttpClient(pipeName);
        await client.PostAsync("/__shutdown", content: null, timeout.Token);
    }
    catch
    {
    }

    if (daemon.WaitForExit(5000))
    {
        return;
    }

    try
    {
        daemon.Kill();
        daemon.WaitForExit(5000);
    }
    catch
    {
    }
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "wslc-desktop.csproj")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not find repository root from verifier base directory.");
}
