using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

const string NativePipe = "wslc-desktop";
const string DockerPipe = "wslc-desktop-docker";
const string DockerHost = "npipe:////./pipe/wslc-desktop-docker";
const string DockerContext = "wslc-desktop";

string mode = args.FirstOrDefault() ?? "readonly";
if (!mode.Equals("readonly", StringComparison.OrdinalIgnoreCase) &&
    !mode.Equals("lifecycle", StringComparison.OrdinalIgnoreCase) &&
    !mode.Equals("resources", StringComparison.OrdinalIgnoreCase) &&
    !mode.Equals("streaming", StringComparison.OrdinalIgnoreCase) &&
    !mode.Equals("context", StringComparison.OrdinalIgnoreCase) &&
    !mode.Equals("compose", StringComparison.OrdinalIgnoreCase) &&
    !mode.Equals("docker-cli", StringComparison.OrdinalIgnoreCase) &&
    !mode.Equals("gui-lifecycle", StringComparison.OrdinalIgnoreCase) &&
    !mode.Equals("full", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"Unsupported verifier mode for this phase: {mode}");
}

string root = FindRepositoryRoot();
TimeSpan verificationTimeout = mode.Equals("full", StringComparison.OrdinalIgnoreCase) ||
    mode.Equals("docker-cli", StringComparison.OrdinalIgnoreCase) ||
    mode.Equals("compose", StringComparison.OrdinalIgnoreCase)
    ? TimeSpan.FromMinutes(4)
    : TimeSpan.FromSeconds(60);
using var cts = new CancellationTokenSource(verificationTimeout);
using var daemon = StartDaemon(root, requireReleasePackaged: mode.Equals("docker-cli", StringComparison.OrdinalIgnoreCase));

try
{
    if (mode.Equals("lifecycle", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyLifecycleAsync(cts.Token);
        Console.WriteLine("DOCKER_API_LIFECYCLE_OK");
    }
    else if (mode.Equals("full", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyReadOnlyAsync(cts.Token);
        await VerifyLifecycleAsync(cts.Token);
        await VerifyResourcesAsync(cts.Token);
        await VerifyStreamingAsync(cts.Token);
        Console.WriteLine("DOCKER_API_COMPAT_FULL_OK");
    }
    else if (mode.Equals("resources", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyResourcesAsync(cts.Token);
        Console.WriteLine("DOCKER_API_RESOURCES_OK");
    }
    else if (mode.Equals("streaming", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyStreamingAsync(cts.Token);
        Console.WriteLine("DOCKER_API_STREAMING_OK");
    }
    else if (mode.Equals("context", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyContextAsync(root, cts.Token);
        Console.WriteLine("DOCKER_API_CONTEXT_OK");
    }
    else if (mode.Equals("compose", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyComposeAsync(root, cts.Token);
        Console.WriteLine("DOCKER_API_COMPOSE_OK");
    }
    else if (mode.Equals("docker-cli", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyDockerCliAsync(root, cts.Token);
        Console.WriteLine("DOCKER_API_DOCKER_CLI_OK");
    }
    else if (mode.Equals("gui-lifecycle", StringComparison.OrdinalIgnoreCase))
    {
        await VerifyGuiLifecycleAsync(root, cts.Token);
        Console.WriteLine("PHASE18_GUI_LIFECYCLE_OK");
    }
    else
    {
        await VerifyReadOnlyAsync(cts.Token);
        Console.WriteLine("DOCKER_API_READONLY_OK");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
finally
{
    await CleanupDaemonAsync(daemon);
}

static async Task VerifyStreamingAsync(CancellationToken cancellationToken)
{
    await VerifyReadOnlyAsync(cancellationToken);

    string name = "wslcd_streaming_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    string? id = null;

    try
    {
        using JsonDocument created = await PostJsonAsync(
            DockerPipe,
            "/v1.54/containers/create?name=" + Uri.EscapeDataString(name),
            new
            {
                Image = "ubuntu",
                Cmd = new[] { "sh", "-c", "echo phase16-log && sleep 30" },
                HostConfig = new { AutoRemove = false }
            },
            cancellationToken);
        id = NormalizeId(GetString(created.RootElement, "Id"));
        using HttpResponseMessage start = await PostEmptyAsync(DockerPipe, $"/v1.54/containers/{id}/start", cancellationToken);
        RequireStatus(start, HttpStatusCode.NoContent, "Streaming fixture start");

        await WaitForContainerStateAsync(name, "running", cancellationToken);

        string logs = DecodeRawStream(await GetBytesAsync(DockerPipe, $"/v1.54/containers/{id}/logs?stdout=1&stderr=1&tail=20", cancellationToken));
        if (!logs.Contains("phase16-log", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected logs output to contain phase16-log.");
        }

        using JsonDocument stats = await GetJsonAsync(DockerPipe, $"/v1.54/containers/{id}/stats?stream=false", cancellationToken);
        RequireJsonProperty(stats, "cpu_stats");
        RequireJsonProperty(stats, "memory_stats");

        using HttpResponseMessage streamStats = await SendAsync(DockerPipe, $"/v1.54/containers/{id}/stats", cancellationToken);
        RequireStatus(streamStats, HttpStatusCode.NotImplemented, "Stats default stream unsupported");

        using JsonDocument execCreate = await PostJsonAsync(
            DockerPipe,
            $"/v1.54/containers/{id}/exec",
            new
            {
                AttachStdout = true,
                AttachStderr = true,
                Tty = false,
                Cmd = new[] { "sh", "-lc", "echo wslcd-exec" }
            },
            cancellationToken);
        string execId = GetString(execCreate.RootElement, "Id");
        if (string.IsNullOrWhiteSpace(execId))
        {
            throw new InvalidOperationException("Exec create did not return Id.");
        }

        string execOutput = DecodeRawStream(await PostJsonBytesAsync(DockerPipe, $"/v1.54/exec/{execId}/start", new { Detach = false, Tty = false }, cancellationToken));
        if (!execOutput.Contains("wslcd-exec", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Exec output did not contain wslcd-exec.");
        }

        using JsonDocument execInspect = await GetJsonAsync(DockerPipe, $"/v1.54/exec/{execId}/json", cancellationToken);
        if (execInspect.RootElement.TryGetProperty("ExitCode", out JsonElement exitCode) && exitCode.GetInt32() != 0)
        {
            throw new InvalidOperationException($"Expected exec exit code 0, got {exitCode.GetInt32()}.");
        }

        using HttpResponseMessage resize = await PostEmptyAsync(DockerPipe, $"/v1.54/exec/{execId}/resize?h=24&w=80", cancellationToken);
        if (resize.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.Created))
        {
            throw new InvalidOperationException($"Exec resize returned {(int)resize.StatusCode}.");
        }

        using HttpResponseMessage ttyExecCreate = await PostJsonResponseAsync(
            DockerPipe,
            $"/v1.54/containers/{id}/exec",
            new
            {
                AttachStdout = true,
                AttachStderr = true,
                Tty = true,
                Cmd = new[] { "sh" }
            },
            cancellationToken);
        RequireStatus(ttyExecCreate, HttpStatusCode.NotImplemented, "TTY exec create unsupported");

        using HttpResponseMessage missingResize = await PostEmptyAsync(DockerPipe, "/v1.54/exec/missing-phase16-exec/resize?h=24&w=80", cancellationToken);
        RequireStatus(missingResize, HttpStatusCode.NotFound, "Missing exec resize");
    }
    finally
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            using HttpResponseMessage _ = await DeleteAsync(DockerPipe, $"/v1.54/containers/{id}?force=1&v=1", CancellationToken.None);
        }
    }
}

static async Task VerifyContextAsync(string root, CancellationToken cancellationToken)
{
    await VerifyReadOnlyAsync(cancellationToken);

    var inspect = await RunDockerAsync(root, ["context", "inspect", DockerContext], cancellationToken);
    RequireExitCode(inspect, 0, "docker context inspect wslc-desktop");
    if (!inspect.StandardOutput.Contains(DockerHost, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Docker context 'wslc-desktop' does not point to npipe:////./pipe/wslc-desktop-docker.");
    }

    var ps = await RunDockerAsync(root, ["--context", DockerContext, "ps", "-a"], cancellationToken);
    RequireExitCode(ps, 0, "docker --context wslc-desktop ps -a");
}

static async Task VerifyComposeAsync(string root, CancellationToken cancellationToken)
{
    await VerifyContextAsync(root, cancellationToken);

    string composePath = Path.Combine(root, "tools", "DockerApiCompatVerify", "fixtures", "compose-basic", "compose.yaml");
    if (!File.Exists(composePath))
    {
        throw new FileNotFoundException("Compose fixture was not found.", composePath);
    }

    var config = await RunDockerComposeAsync(root, composePath, ["config"], cancellationToken);
    RequireExitCode(config, 0, "docker --context wslc-desktop compose config");

    bool upSucceeded = false;
    try
    {
        var up = await RunDockerComposeAsync(root, composePath, ["up", "-d"], cancellationToken);
        if (up.ExitCode != 0)
        {
            string combined = up.StandardOutput + Environment.NewLine + up.StandardError;
            if (combined.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("not supported", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Compose up returned documented unsupported behavior:");
                Console.WriteLine(combined);
                return;
            }

            RequireExitCode(up, 0, "docker --context wslc-desktop compose up -d");
        }

        upSucceeded = true;
        using JsonDocument containers = RunWslcJsonArray("container list --all --format json");
        bool hasPhase17Container = containers.RootElement.EnumerateArray()
            .Any(container => GetString(container, "Name").Contains("compose", StringComparison.OrdinalIgnoreCase) ||
                GetString(container, "Name").Contains("phase17", StringComparison.OrdinalIgnoreCase));
        if (!hasPhase17Container)
        {
            throw new InvalidOperationException("Compose up completed, but no visible Phase 17 WSLC container was found.");
        }
    }
    finally
    {
        if (upSucceeded)
        {
            var down = await RunDockerComposeAsync(root, composePath, ["down", "--volumes"], CancellationToken.None);
            RequireExitCode(down, 0, "docker --context wslc-desktop compose down --volumes");
        }
    }
}

static async Task VerifyDockerCliAsync(string root, CancellationToken cancellationToken)
{
    await VerifyReadOnlyAsync(cancellationToken);

    var version = await RunDockerAsync(root, ["-H", DockerHost, "version"], cancellationToken);
    RequireExitCode(version, 0, "docker -H npipe version");

    var info = await RunDockerAsync(root, ["-H", DockerHost, "info"], cancellationToken);
    RequireExitCode(info, 0, "docker -H npipe info");

    var ps = await RunDockerAsync(root, ["-H", DockerHost, "ps", "-a", "--format", "{{.Names}}\t{{.Image}}"], cancellationToken);
    RequireExitCode(ps, 0, "docker -H npipe ps -a");
    RequireDockerPsMatchesWslc(ps.StandardOutput);

    var images = await RunDockerAsync(root, ["-H", DockerHost, "images", "--format", "{{.Repository}}:{{.Tag}}"], cancellationToken);
    RequireExitCode(images, 0, "docker -H npipe images");
    RequireDockerImagesMatchWslc(images.StandardOutput);

    var pull = await RunDockerAsync(root, ["-H", DockerHost, "pull", "hello-world:latest"], cancellationToken);
    RequireExitCode(pull, 0, "docker -H npipe pull hello-world:latest");
    RequireImagePresent("hello-world", "latest");

    var volumeLs = await RunDockerAsync(root, ["-H", DockerHost, "volume", "ls"], cancellationToken);
    RequireExitCode(volumeLs, 0, "docker -H npipe volume ls");

    await VerifyDockerCliStreamingAsync(root, cancellationToken);

    string containerName = "wslcd-cli-smoke-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    try
    {
        var create = await RunDockerAsync(root, ["-H", DockerHost, "create", "--name", containerName, "hello-world:latest"], cancellationToken);
        RequireExitCode(create, 0, "docker -H npipe create --name wslcd-cli-smoke hello-world");
        if (string.IsNullOrWhiteSpace(create.StandardOutput))
        {
            throw new InvalidOperationException("Docker CLI create did not print a container id.");
        }

        RequireContainerPresent(containerName, true);

        var start = await RunDockerAsync(root, ["-H", DockerHost, "start", containerName], cancellationToken);
        RequireExitCode(start, 0, "docker -H npipe start wslcd-cli-smoke");

        var remove = await RunDockerAsync(root, ["-H", DockerHost, "rm", "-f", containerName], cancellationToken);
        RequireExitCode(remove, 0, "docker -H npipe rm -f wslcd-cli-smoke");
        RequireContainerPresent(containerName, false);
    }
    finally
    {
        await RunDockerAsync(root, ["-H", DockerHost, "rm", "-f", containerName], CancellationToken.None);
    }

    await EnsureDockerContextAsync(root, cancellationToken);
    await VerifyContextAsync(root, cancellationToken);
    await VerifyComposeAsync(root, cancellationToken);
}

static async Task VerifyGuiLifecycleAsync(string root, CancellationToken cancellationToken)
{
    await VerifyReadOnlyAsync(cancellationToken);

    string containerName = "wslcd-gui-lifecycle-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    string? id = null;
    try
    {
        using JsonDocument created = await PostJsonAsync(
            NativePipe,
            "/v1/containers",
            new
            {
                Name = containerName,
                Image = "ubuntu:latest",
                Command = new[] { "sleep", "60" },
                Ports = Array.Empty<object>(),
                Mounts = Array.Empty<object>(),
                Environment = new Dictionary<string, string>(),
                AutoRemove = false,
                Labels = new Dictionary<string, string>
                {
                    ["com.wslc.desktop.verify"] = "phase18"
                }
            },
            cancellationToken);
        id = NormalizeId(GetString(created.RootElement, "id", "Id", "ID"));
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Native container create did not return an id.");
        }

        await RequireDockerContainerStateAsync(containerName, "created", cancellationToken);
        await RequireDockerCliContainerStateAsync(root, containerName, "created", cancellationToken);

        using HttpResponseMessage start = await PostEmptyAsync(NativePipe, $"/v1/containers/{id}/start", cancellationToken);
        RequireStatus(start, HttpStatusCode.NoContent, "Native container start");
        await WaitForDockerContainerStateAsync(containerName, "running", cancellationToken);
        await RequireDockerCliContainerStateAsync(root, containerName, "running", cancellationToken);

        using HttpResponseMessage stop = await PostEmptyAsync(NativePipe, $"/v1/containers/{id}/stop", cancellationToken);
        RequireStatus(stop, HttpStatusCode.NoContent, "Native container stop");
        await WaitForDockerContainerStateAsync(containerName, "exited", cancellationToken);
        await RequireDockerCliContainerStateAsync(root, containerName, "exited", cancellationToken);

        using HttpResponseMessage remove = await DeleteAsync(NativePipe, $"/v1/containers/{id}", cancellationToken);
        RequireStatus(remove, HttpStatusCode.NoContent, "Native container remove");
        await WaitForDockerContainerAbsentAsync(containerName, cancellationToken);
        await RequireDockerCliContainerAbsentAsync(root, containerName, cancellationToken);
    }
    finally
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            using HttpResponseMessage _ = await DeleteAsync(NativePipe, $"/v1/containers/{id}", CancellationToken.None);
        }
    }
}

static async Task VerifyDockerCliStreamingAsync(string root, CancellationToken cancellationToken)
{
    string containerName = "wslcd-cli-streaming-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    try
    {
        var create = await RunDockerAsync(
            root,
            [
                "-H", DockerHost,
                "create",
                "--name", containerName,
                "ubuntu:latest",
                "sh", "-c", "echo phase16-cli-log && sleep 60"
            ],
            cancellationToken);
        RequireExitCode(create, 0, "docker -H npipe create --name wslcd-cli-streaming ubuntu");

        var start = await RunDockerAsync(root, ["-H", DockerHost, "start", containerName], cancellationToken);
        RequireExitCode(start, 0, "docker -H npipe start wslcd-cli-streaming");

        await WaitForContainerStateAsync(containerName, "running", cancellationToken);

        var logs = await RunDockerAsync(root, ["-H", DockerHost, "logs", containerName], cancellationToken);
        RequireExitCode(logs, 0, "docker -H npipe logs wslcd-cli-streaming");
        if (!logs.StandardOutput.Contains("phase16-cli-log", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Docker CLI logs did not contain phase16-cli-log.");
        }

        var stats = await RunDockerAsync(root, ["-H", DockerHost, "stats", "--no-stream", containerName], cancellationToken);
        RequireExitCode(stats, 0, "docker -H npipe stats --no-stream wslcd-cli-streaming");
        if (!stats.StandardOutput.Contains(containerName, StringComparison.OrdinalIgnoreCase) ||
            !stats.StandardOutput.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
            !stats.StandardOutput.Contains("MEM", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Docker CLI stats --no-stream did not include the expected container, CPU, and memory columns." +
                Environment.NewLine + stats.StandardOutput);
        }

        var exec = await RunDockerAsync(
            root,
            ["-H", DockerHost, "exec", containerName, "sh", "-lc", "echo phase16-cli-exec"],
            cancellationToken);
        RequireExitCode(exec, 0, "docker -H npipe exec wslcd-cli-streaming");
        if (!exec.StandardOutput.Contains("phase16-cli-exec", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Docker CLI exec did not contain phase16-cli-exec.");
        }
    }
    finally
    {
        await RunDockerAsync(root, ["-H", DockerHost, "rm", "-f", containerName], CancellationToken.None);
    }
}

static async Task RequireDockerContainerStateAsync(string name, string expectedState, CancellationToken cancellationToken)
{
    using JsonDocument containers = await GetJsonAsync(DockerPipe, "/v1.54/containers/json?all=1", cancellationToken);
    JsonElement? match = FindDockerContainerByName(containers, name);
    if (match is not JsonElement container)
    {
        throw new InvalidOperationException($"Docker pipe did not list container '{name}'.");
    }

    string actual = GetString(container, "State");
    if (!actual.Equals(expectedState, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected Docker pipe container '{name}' state '{expectedState}', got '{actual}'.");
    }
}

static async Task WaitForDockerContainerStateAsync(string name, string expectedState, CancellationToken cancellationToken)
{
    Exception? last = null;
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await RequireDockerContainerStateAsync(name, expectedState, cancellationToken);
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException)
        {
            last = ex;
            await Task.Delay(250, cancellationToken);
        }
    }

    throw new TimeoutException($"Timed out waiting for Docker pipe container '{name}' to reach '{expectedState}'.", last);
}

static async Task WaitForDockerContainerAbsentAsync(string name, CancellationToken cancellationToken)
{
    Exception? last = null;
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            using JsonDocument containers = await GetJsonAsync(DockerPipe, "/v1.54/containers/json?all=1", cancellationToken);
            if (FindDockerContainerByName(containers, name) is null)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            last = ex;
        }

        await Task.Delay(250, cancellationToken);
    }

    throw new TimeoutException($"Timed out waiting for Docker pipe container '{name}' to be removed.", last);
}

static JsonElement? FindDockerContainerByName(JsonDocument containers, string name)
{
    return containers.RootElement.EnumerateArray().Cast<JsonElement?>().FirstOrDefault(container =>
        container is JsonElement item &&
        item.TryGetProperty("Names", out JsonElement names) &&
        names.ValueKind == JsonValueKind.Array &&
        names.EnumerateArray().Any(value => value.GetString()?.TrimStart('/').Equals(name, StringComparison.OrdinalIgnoreCase) == true));
}

static async Task RequireDockerCliContainerStateAsync(string root, string name, string expectedState, CancellationToken cancellationToken)
{
    var ps = await RunDockerAsync(root, ["-H", DockerHost, "ps", "-a", "--filter", "name=" + name, "--format", "{{.Names}}\t{{.State}}"], cancellationToken);
    RequireExitCode(ps, 0, "docker -H npipe ps -a --filter name");
    string[] lines = SplitOutputLines(ps.StandardOutput);
    string? line = lines.FirstOrDefault(item => item.StartsWith(name + "\t", StringComparison.OrdinalIgnoreCase));
    if (line is null)
    {
        throw new InvalidOperationException($"Docker CLI did not list container '{name}'. Output:{Environment.NewLine}{ps.StandardOutput}");
    }

    string actual = line.Split('\t').LastOrDefault() ?? string.Empty;
    if (!actual.Equals(expectedState, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected Docker CLI container '{name}' state '{expectedState}', got '{actual}'.");
    }
}

static async Task RequireDockerCliContainerAbsentAsync(string root, string name, CancellationToken cancellationToken)
{
    var ps = await RunDockerAsync(root, ["-H", DockerHost, "ps", "-a", "--filter", "name=" + name, "--format", "{{.Names}}"], cancellationToken);
    RequireExitCode(ps, 0, "docker -H npipe ps -a --filter name");
    if (SplitOutputLines(ps.StandardOutput).Any(item => item.Equals(name, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException($"Docker CLI still listed removed container '{name}'.");
    }
}

static async Task EnsureDockerContextAsync(string root, CancellationToken cancellationToken)
{
    var inspect = await RunDockerAsync(root, ["context", "inspect", DockerContext], cancellationToken);
    if (inspect.ExitCode == 0)
    {
        if (!inspect.StandardOutput.Contains(DockerHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Docker context '{DockerContext}' exists but does not point to {DockerHost}.");
        }

        return;
    }

    var create = await RunDockerAsync(root, ["context", "create", DockerContext, "--docker", "host=" + DockerHost], cancellationToken);
    RequireExitCode(create, 0, "docker context create wslc-desktop");
}

static void RequireDockerPsMatchesWslc(string dockerOutput)
{
    var actual = SplitOutputLines(dockerOutput)
        .Select(line => line.Split('\t').FirstOrDefault() ?? string.Empty)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    using JsonDocument containers = RunWslcJsonArray("container list --all --format json");
    var expected = containers.RootElement.EnumerateArray()
        .Select(container => GetString(container, "Name"))
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    RequireSequenceEqual(expected, actual, "Docker CLI container list parity failed.");
}

static void RequireDockerImagesMatchWslc(string dockerOutput)
{
    var actual = SplitOutputLines(dockerOutput)
        .Where(reference => !string.IsNullOrWhiteSpace(reference) && !reference.Equals("<none>:<none>", StringComparison.OrdinalIgnoreCase))
        .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    using JsonDocument images = RunWslcJsonArray("image list --format json");
    var expected = images.RootElement.EnumerateArray()
        .Select(image => $"{GetString(image, "Repository")}:{DefaultIfWhiteSpace(GetString(image, "Tag"), "latest")}")
        .Where(reference => !reference.StartsWith("<none>:", StringComparison.OrdinalIgnoreCase))
        .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    RequireSequenceEqual(expected, actual, "Docker CLI image list parity failed.");
}

static string[] SplitOutputLines(string output)
{
    return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static async Task VerifyResourcesAsync(CancellationToken cancellationToken)
{
    await VerifyReadOnlyAsync(cancellationToken);

    string pull = await PostJsonTextAsync(
        DockerPipe,
        "/v1.54/images/create?fromImage=hello-world&tag=latest",
        new { },
        cancellationToken);
    string[] pullMessages = SplitOutputLines(pull);
    if (pullMessages.Length == 0)
    {
        throw new InvalidOperationException("Image pull did not return newline-delimited JSON progress messages.");
    }

    foreach (string message in pullMessages)
    {
        using JsonDocument document = JsonDocument.Parse(message);
        RequireJsonProperty(document, "status");
    }

    using JsonDocument images = RunWslcJsonArray("image list --format json");
    bool hasHelloWorld = images.RootElement.EnumerateArray()
        .Any(image => GetString(image, "Repository").Equals("hello-world", StringComparison.OrdinalIgnoreCase) &&
            DefaultIfWhiteSpace(GetString(image, "Tag"), "latest").Equals("latest", StringComparison.OrdinalIgnoreCase));
    if (!hasHelloWorld)
    {
        throw new InvalidOperationException("hello-world:latest did not appear in wslc image list after pull.");
    }

    string deleteFixtureTag = "phase15-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    string deleteFixtureImage = "wslcd-delete-fixture:" + deleteFixtureTag;
    RunWslcCommand("image tag hello-world:latest " + deleteFixtureImage);
    RequireImagePresent("wslcd-delete-fixture", deleteFixtureTag);
    try
    {
        using HttpResponseMessage deleteImage = await DeleteAsync(
            DockerPipe,
            "/v1.54/images/" + Uri.EscapeDataString(deleteFixtureImage) + "?force=1&noprune=1",
            cancellationToken);
        RequireStatus(deleteImage, HttpStatusCode.OK, "Image tag remove");
        RequireImageAbsent("wslcd-delete-fixture", deleteFixtureTag);
        RequireImagePresent("hello-world", "latest");
    }
    finally
    {
        RunWslcCommand("image remove -f " + deleteFixtureImage, allowFailure: true);
    }

    string scopedFixtureImage = "wslcd/scope-fixture:" + deleteFixtureTag;
    RunWslcCommand("image tag hello-world:latest " + scopedFixtureImage);
    RequireImagePresent("wslcd/scope-fixture", deleteFixtureTag);
    try
    {
        using JsonDocument scopedImage = await GetJsonAsync(
            DockerPipe,
            "/v1.54/images/" + scopedFixtureImage + "/json",
            cancellationToken);
        RequireJsonString(scopedImage, "Id", "sha256:" + NormalizeImageId("hello-world", "latest"));

        using HttpResponseMessage deleteScopedImage = await DeleteAsync(
            DockerPipe,
            "/v1.54/images/" + scopedFixtureImage + "?force=1&noprune=1",
            cancellationToken);
        RequireStatus(deleteScopedImage, HttpStatusCode.OK, "Scoped image tag remove");
        RequireImageAbsent("wslcd/scope-fixture", deleteFixtureTag);
        RequireImagePresent("hello-world", "latest");
    }
    finally
    {
        RunWslcCommand("image remove -f " + scopedFixtureImage, allowFailure: true);
    }

    string registryFixtureImage = "localhost:5000/wslcd/registry-fixture:" + deleteFixtureTag;
    RunWslcCommand("image tag hello-world:latest " + registryFixtureImage);
    RequireImagePresent("localhost:5000/wslcd/registry-fixture", deleteFixtureTag);
    try
    {
        using JsonDocument registryImage = await GetJsonAsync(
            DockerPipe,
            "/v1.54/images/" + registryFixtureImage + "/json",
            cancellationToken);
        RequireJsonString(registryImage, "Id", "sha256:" + NormalizeImageId("hello-world", "latest"));

        using HttpResponseMessage deleteRegistryImage = await DeleteAsync(
            DockerPipe,
            "/v1.54/images/" + registryFixtureImage + "?force=1&noprune=1",
            cancellationToken);
        RequireStatus(deleteRegistryImage, HttpStatusCode.OK, "Registry image tag remove");
        RequireImageAbsent("localhost:5000/wslcd/registry-fixture", deleteFixtureTag);
        RequireImagePresent("hello-world", "latest");
    }
    finally
    {
        RunWslcCommand("image remove -f " + registryFixtureImage, allowFailure: true);
    }

    using JsonDocument volumesBefore = await GetJsonAsync(DockerPipe, "/v1.54/volumes", cancellationToken);
    RequireJsonProperty(volumesBefore, "Volumes");

    using HttpResponseMessage imageSearch = await SendAsync(DockerPipe, "/v1.54/images/search?term=hello-world", cancellationToken);
    RequireStatus(imageSearch, HttpStatusCode.NotImplemented, "Image search unsupported");

    string volumeName = "wslcd-volume-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    using JsonDocument createdVolume = await PostJsonAsync(
        DockerPipe,
        "/v1.54/volumes/create",
        new
        {
            Name = volumeName,
            Driver = "local",
            Labels = new Dictionary<string, string>
            {
                ["com.wslc.desktop.verify"] = "phase15"
            }
        },
        cancellationToken);
    RequireJsonString(createdVolume, "Name", volumeName);

    using JsonDocument inspectedVolume = await GetJsonAsync(DockerPipe, "/v1.54/volumes/" + Uri.EscapeDataString(volumeName), cancellationToken);
    RequireJsonString(inspectedVolume, "Name", volumeName);

    using HttpResponseMessage deleteVolume = await DeleteAsync(DockerPipe, "/v1.54/volumes/" + Uri.EscapeDataString(volumeName) + "?force=1", cancellationToken);
    RequireStatus(deleteVolume, HttpStatusCode.NoContent, "Volume remove");

    using JsonDocument networks = await GetJsonAsync(DockerPipe, "/v1.54/networks", cancellationToken);
    if (networks.RootElement.ValueKind != JsonValueKind.Array || networks.RootElement.GetArrayLength() == 0)
    {
        throw new InvalidOperationException("Network list should return at least the default wslc network.");
    }

    using JsonDocument inspectedNetwork = await GetJsonAsync(DockerPipe, "/v1.54/networks/wslc", cancellationToken);
    RequireJsonString(inspectedNetwork, "Name", "wslc");

    string composeNetworkName = "wslcd-compose-default-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    using JsonDocument networkCreate = await PostJsonAsync(
        DockerPipe,
        "/v1.54/networks/create",
        new
        {
            Name = composeNetworkName,
            Driver = "bridge",
            Labels = new Dictionary<string, string>
            {
                ["com.docker.compose.project"] = "wslcd-verify",
                ["com.docker.compose.network"] = "default"
            }
        },
        cancellationToken);
    RequireJsonString(networkCreate, "Id", composeNetworkName);

    using JsonDocument inspectedComposeNetwork = await GetJsonAsync(DockerPipe, "/v1.54/networks/" + Uri.EscapeDataString(composeNetworkName), cancellationToken);
    RequireJsonString(inspectedComposeNetwork, "Name", composeNetworkName);

    string composeNetworkFilter = Uri.EscapeDataString("{\"label\":[\"com.docker.compose.project=wslcd-verify\",\"com.docker.compose.network=default\"]}");
    using JsonDocument filteredComposeNetworks = await GetJsonAsync(DockerPipe, "/v1.54/networks?filters=" + composeNetworkFilter, cancellationToken);
    if (filteredComposeNetworks.RootElement.ValueKind != JsonValueKind.Array ||
        !filteredComposeNetworks.RootElement.EnumerateArray().Any(network => GetString(network, "Name").Equals(composeNetworkName, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Network label filter did not return the compose compatibility network.");
    }

    using HttpResponseMessage networkConnect = await PostJsonResponseAsync(
        DockerPipe,
        "/v1.54/networks/" + Uri.EscapeDataString(composeNetworkName) + "/connect",
        new
        {
            Container = "wslcd-network-fixture",
            EndpointConfig = new { }
        },
        cancellationToken);
    RequireStatus(networkConnect, HttpStatusCode.NoContent, "Network connect no-op");

    using HttpResponseMessage networkDisconnect = await PostJsonResponseAsync(
        DockerPipe,
        "/v1.54/networks/" + Uri.EscapeDataString(composeNetworkName) + "/disconnect",
        new
        {
            Container = "wslcd-network-fixture",
            Force = true
        },
        cancellationToken);
    RequireStatus(networkDisconnect, HttpStatusCode.NoContent, "Network disconnect no-op");

    using HttpResponseMessage networkDelete = await DeleteAsync(DockerPipe, "/v1.54/networks/" + Uri.EscapeDataString(composeNetworkName), cancellationToken);
    RequireStatus(networkDelete, HttpStatusCode.NoContent, "Network delete no-op");

    using JsonDocument networkPrune = await PostJsonAsync(DockerPipe, "/v1.54/networks/prune", new { }, cancellationToken);
    RequireJsonProperty(networkPrune, "NetworksDeleted");
    RequireJsonProperty(networkPrune, "SpaceReclaimed");
}

static async Task VerifyLifecycleAsync(CancellationToken cancellationToken)
{
    await VerifyReadOnlyAsync(cancellationToken);

    string name = "wslcd_lifecycle_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    string? id = null;

    try
    {
        using JsonDocument created = await PostJsonAsync(
            DockerPipe,
            "/v1.54/containers/create?name=" + Uri.EscapeDataString(name),
            new
            {
                Image = "ubuntu",
                Cmd = new[] { "sleep", "60" },
                Env = new[] { "WSLCD_PHASE=14" },
                Labels = new Dictionary<string, string>
                {
                    ["com.wslc.desktop.verify"] = "phase14"
                },
                HostConfig = new
                {
                    AutoRemove = false
                }
            },
            cancellationToken);

        id = NormalizeId(GetString(created.RootElement, "Id"));
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Container create did not return an Id.");
        }

        RequireContainerPresent(name, true);

        using JsonDocument inspectedCreated = await GetJsonAsync(DockerPipe, $"/v1.54/containers/{id}/json", cancellationToken);
        RequireJsonString(inspectedCreated, "Name", "/" + name);

        using HttpResponseMessage start = await PostEmptyAsync(DockerPipe, $"/v1.54/containers/{id}/start", cancellationToken);
        RequireStatus(start, HttpStatusCode.NoContent, "Container start");
        RequireContainerState(name, "running");

        using HttpResponseMessage restart = await PostEmptyAsync(DockerPipe, $"/v1.54/containers/{id}/restart?t=1", cancellationToken);
        RequireStatus(restart, HttpStatusCode.NoContent, "Container restart");
        RequireContainerState(name, "running");

        using HttpResponseMessage stop = await PostEmptyAsync(DockerPipe, $"/v1.54/containers/{id}/stop?t=2", cancellationToken);
        RequireStatus(stop, HttpStatusCode.NoContent, "Container stop");
        RequireContainerNotRunning(name);

        using HttpResponseMessage remove = await DeleteAsync(DockerPipe, $"/v1.54/containers/{id}?force=1&v=1", cancellationToken);
        RequireStatus(remove, HttpStatusCode.NoContent, "Container remove");
        RequireContainerPresent(name, false);
    }
    finally
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            using HttpResponseMessage _ = await DeleteAsync(DockerPipe, $"/v1.54/containers/{id}?force=1&v=1", CancellationToken.None);
        }
    }
}

static async Task VerifyReadOnlyAsync(CancellationToken cancellationToken)
{
    string ping = await WaitForTextAsync(DockerPipe, "/_ping", cancellationToken);
    RequireEquals(ping, "OK", "Docker _ping did not return OK.");

    using JsonDocument version = await GetJsonAsync(DockerPipe, "/v1.54/version", cancellationToken);
    RequireJsonString(version, "ApiVersion", "1.54");
    RequireJsonString(version, "MinAPIVersion", "1.40");

    using JsonDocument info = await GetJsonAsync(DockerPipe, "/v1.54/info", cancellationToken);
    RequireJsonString(info, "OSType", "windows");
    RequireJsonString(info, "Driver", "wslc");

    using JsonDocument cliContainers = RunWslcJsonArray("container list --all --format json");
    using JsonDocument dockerContainers = await GetJsonAsync(DockerPipe, "/v1.54/containers/json?all=1", cancellationToken);
    RequireContainerListParity(cliContainers, dockerContainers);

    JsonElement? firstContainer = cliContainers.RootElement.EnumerateArray().Cast<JsonElement?>().FirstOrDefault();
    if (firstContainer is JsonElement container)
    {
        string containerId = NormalizeId(GetString(container, "Id", "ID"));
        using JsonDocument inspected = await GetJsonAsync(DockerPipe, $"/v1.54/containers/{containerId}/json", cancellationToken);
        RequireJsonString(inspected, "Name", "/" + GetString(container, "Name"));

        string nameFilter = Uri.EscapeDataString(JsonSerializer.Serialize(new Dictionary<string, string[]>
        {
            ["name"] = [GetString(container, "Name")]
        }));
        using JsonDocument filtered = await GetJsonAsync(DockerPipe, $"/v1.54/containers/json?all=1&filters={nameFilter}", cancellationToken);
        if (filtered.RootElement.GetArrayLength() != 1)
        {
            throw new InvalidOperationException("Container name filter did not return exactly one matching container.");
        }
    }

    using JsonDocument cliImages = RunWslcJsonArray("image list --format json");
    using JsonDocument dockerImages = await GetJsonAsync(DockerPipe, "/v1.54/images/json", cancellationToken);
    RequireImageListParity(cliImages, dockerImages);

    JsonElement? firstImage = cliImages.RootElement.EnumerateArray().Cast<JsonElement?>().FirstOrDefault();
    if (firstImage is JsonElement image)
    {
        string reference = $"{GetString(image, "Repository")}:{DefaultIfWhiteSpace(GetString(image, "Tag"), "latest")}";
        using JsonDocument inspectedImage = await GetJsonAsync(DockerPipe, $"/v1.54/images/{Uri.EscapeDataString(reference)}/json", cancellationToken);
        RequireJsonString(inspectedImage, "Id", "sha256:" + NormalizeId(GetString(image, "Id", "ID")));
    }

    JsonElement? firstNamespacedImage = cliImages.RootElement.EnumerateArray()
        .Cast<JsonElement?>()
        .FirstOrDefault(item => item is JsonElement candidate &&
            GetString(candidate, "Repository").Contains('/', StringComparison.Ordinal));
    if (firstNamespacedImage is JsonElement namespacedImage)
    {
        string repository = GetString(namespacedImage, "Repository");
        using JsonDocument inspectedNamespacedImage = await GetJsonAsync(DockerPipe, $"/v1.54/images/{repository}/json", cancellationToken);
        RequireJsonString(inspectedNamespacedImage, "Id", "sha256:" + NormalizeId(GetString(namespacedImage, "Id", "ID")));
    }

    using HttpResponseMessage missing = await SendAsync(DockerPipe, "/v1.54/containers/not-found/json", cancellationToken);
    if (missing.StatusCode != HttpStatusCode.NotFound)
    {
        throw new InvalidOperationException($"Missing container inspect returned {(int)missing.StatusCode}, expected 404.");
    }

    string missingBody = await missing.Content.ReadAsStringAsync(cancellationToken);
    using JsonDocument missingJson = JsonDocument.Parse(missingBody);
    RequireJsonProperty(missingJson, "message");
}

static Process StartDaemon(string root, bool requireReleasePackaged = false)
{
    string publishedDaemon = Path.Combine(root, "artifacts", "wslcd-singlefile", "wslcd-desktop.exe");
    string releaseLayoutDaemon = Path.Combine(root, "bin", "x64", "Release", "net10.0-windows10.0.26100.0", "win-x64", "wslcd", "wslcd-desktop.exe");
    string releasePackagedDaemon = Path.Combine(root, "bin", "x64", "Release", "net10.0-windows10.0.26100.0", "win-x64", "AppX", "wslcd", "wslcd-desktop.exe");
    string debugPackagedDaemon = Path.Combine(root, "bin", "Debug", "net10.0-windows10.0.26100.0", "win-x64", "AppX", "wslcd", "wslcd-desktop.exe");
    string daemonDll = Path.Combine(root, "src", "wslcd", "bin", "Debug", "net10.0", "wslcd-desktop.dll");
    string legacyPublishedDaemon = Path.Combine(root, "artifacts", "wslcd-singlefile", "wslcd.exe");
    string legacyReleaseLayoutDaemon = Path.Combine(root, "bin", "x64", "Release", "net10.0-windows10.0.26100.0", "win-x64", "wslcd", "wslcd.exe");
    string legacyReleasePackagedDaemon = Path.Combine(root, "bin", "x64", "Release", "net10.0-windows10.0.26100.0", "win-x64", "AppX", "wslcd", "wslcd.exe");
    string legacyDebugPackagedDaemon = Path.Combine(root, "bin", "Debug", "net10.0-windows10.0.26100.0", "win-x64", "AppX", "wslcd", "wslcd.exe");
    string legacyDaemonDll = Path.Combine(root, "src", "wslcd", "bin", "Debug", "net10.0", "wslcd.dll");

    var candidates = new (string FileName, string Arguments, string WorkingDirectory, string PathToCheck)[]
    {
        (releaseLayoutDaemon, string.Empty, Path.GetDirectoryName(releaseLayoutDaemon) ?? root, releaseLayoutDaemon),
        (releasePackagedDaemon, string.Empty, Path.GetDirectoryName(releasePackagedDaemon) ?? root, releasePackagedDaemon),
        (debugPackagedDaemon, string.Empty, Path.GetDirectoryName(debugPackagedDaemon) ?? root, debugPackagedDaemon),
        (publishedDaemon, string.Empty, Path.GetDirectoryName(publishedDaemon) ?? root, publishedDaemon),
        (legacyReleaseLayoutDaemon, string.Empty, Path.GetDirectoryName(legacyReleaseLayoutDaemon) ?? root, legacyReleaseLayoutDaemon),
        ("dotnet", $"\"{daemonDll}\"", root, daemonDll),
        (legacyReleasePackagedDaemon, string.Empty, Path.GetDirectoryName(legacyReleasePackagedDaemon) ?? root, legacyReleasePackagedDaemon),
        (legacyDebugPackagedDaemon, string.Empty, Path.GetDirectoryName(legacyDebugPackagedDaemon) ?? root, legacyDebugPackagedDaemon),
        (legacyPublishedDaemon, string.Empty, Path.GetDirectoryName(legacyPublishedDaemon) ?? root, legacyPublishedDaemon),
        ("dotnet", $"\"{legacyDaemonDll}\"", root, legacyDaemonDll)
    };

    var existingCandidates = (requireReleasePackaged ? candidates.Take(1) : candidates)
        .Where(candidate => File.Exists(candidate.PathToCheck))
        .ToArray();
    if (existingCandidates.Length == 0)
    {
        throw new FileNotFoundException(
            requireReleasePackaged
                ? "The Release layout wslcd-desktop daemon was not found. Build wslc-desktop.csproj for x64 Release before running Docker CLI release smoke gates."
                : "No usable wslcd-desktop daemon candidate was found. Build wslc-desktop or src/wslcd first.",
            requireReleasePackaged ? releaseLayoutDaemon : daemonDll);
    }

    string logDirectory = Path.Combine(root, "artifacts", "docker-api-compat", "Diagnostics");
    Directory.CreateDirectory(logDirectory);

    Exception? lastError = null;
    foreach (var candidate in existingCandidates)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = candidate.FileName,
                Arguments = candidate.Arguments,
                WorkingDirectory = candidate.WorkingDirectory,
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

static async Task<string> WaitForTextAsync(string pipeName, string path, CancellationToken cancellationToken)
{
    Exception? last = null;
    while (!cancellationToken.IsCancellationRequested)
    {
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

static async Task<ProcessResult> RunDockerAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
{
    try
    {
        return await RunProcessAsync("docker", arguments, root, cancellationToken);
    }
    catch (Win32Exception ex)
    {
        throw new InvalidOperationException("Docker CLI is not installed or is not on PATH. Install Docker CLI before running context or compose compatibility gates.", ex);
    }
}

static async Task<ProcessResult> RunDockerComposeAsync(string root, string composePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
{
    var dockerComposeArguments = new List<string> { "--context", DockerContext, "compose", "-f", composePath };
    dockerComposeArguments.AddRange(arguments);
    ProcessResult dockerComposePlugin = await RunDockerAsync(root, dockerComposeArguments, cancellationToken);
    if (!LooksLikeMissingDockerComposePlugin(dockerComposePlugin))
    {
        return dockerComposePlugin;
    }

    var standaloneArguments = new List<string> { "--context", DockerContext, "-f", composePath };
    standaloneArguments.AddRange(arguments);
    try
    {
        return await RunProcessAsync("docker-compose", standaloneArguments, root, cancellationToken);
    }
    catch (Win32Exception ex)
    {
        throw new InvalidOperationException(
            "Docker Compose is not available as either 'docker compose' or 'docker-compose'. Install Docker Compose before running compose compatibility gates.",
            ex);
    }
}

static bool LooksLikeMissingDockerComposePlugin(ProcessResult result)
{
    if (result.ExitCode == 0)
    {
        return false;
    }

    string output = result.StandardOutput + Environment.NewLine + result.StandardError;
    return output.Contains("unknown shorthand flag", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("not a docker command", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("is not a docker command", StringComparison.OrdinalIgnoreCase);
}

static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = ResolveProcessFileName(fileName),
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    AddDefaultToolPaths(startInfo);

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start {fileName}.");

    string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
    string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
    await process.WaitForExitAsync(cancellationToken);
    return new ProcessResult(process.ExitCode, stdout, stderr);
}

static string ResolveProcessFileName(string fileName)
{
    if (Path.IsPathFullyQualified(fileName) || fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
    {
        return fileName;
    }

    foreach (string directory in GetToolSearchPaths())
    {
        foreach (string candidateName in GetExecutableCandidateNames(fileName))
        {
            string candidate = Path.Combine(directory, candidateName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    return fileName;
}

static IEnumerable<string> GetToolSearchPaths()
{
    var paths = new List<string>();
    string? localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (!string.IsNullOrWhiteSpace(localAppData))
    {
        paths.Add(Path.Combine(localAppData, "Microsoft", "WinGet", "Links"));
    }

    string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    paths.AddRange(currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    return paths
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase);
}

static IEnumerable<string> GetExecutableCandidateNames(string fileName)
{
    if (Path.HasExtension(fileName))
    {
        yield return fileName;
        yield break;
    }

    yield return fileName + ".exe";
    yield return fileName + ".cmd";
    yield return fileName + ".bat";
    yield return fileName;
}

static void AddDefaultToolPaths(ProcessStartInfo startInfo)
{
    string? localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(localAppData))
    {
        return;
    }

    string winGetLinks = Path.Combine(localAppData, "Microsoft", "WinGet", "Links");
    if (!Directory.Exists(winGetLinks))
    {
        return;
    }

    string currentPath = startInfo.Environment.TryGetValue("PATH", out string? path)
        ? path ?? string.Empty
        : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    if (currentPath.Split(Path.PathSeparator).Any(entry => string.Equals(entry.TrimEnd('\\'), winGetLinks.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
    {
        return;
    }

    startInfo.Environment["PATH"] = winGetLinks + Path.PathSeparator + currentPath;
}

static void RequireExitCode(ProcessResult result, int exitCode, string label)
{
    if (result.ExitCode != exitCode)
    {
        throw new InvalidOperationException(
            $"{label} exited with code {result.ExitCode}, expected {exitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}");
    }
}

static async Task<JsonDocument> GetJsonAsync(string pipeName, string path, CancellationToken cancellationToken)
{
    using HttpResponseMessage response = await SendAsync(pipeName, path, cancellationToken);
    if (response.StatusCode != HttpStatusCode.OK)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Expected HTTP 200 for {pipeName}{path}, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
    }

    string json = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonDocument.Parse(json);
}

static async Task<JsonDocument> PostJsonAsync(string pipeName, string path, object body, CancellationToken cancellationToken)
{
    using HttpResponseMessage response = await PostJsonResponseAsync(pipeName, path, body, cancellationToken);
    if (response.StatusCode != HttpStatusCode.Created && response.StatusCode != HttpStatusCode.OK)
    {
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Expected HTTP 201/200 for POST {pipeName}{path}, got {(int)response.StatusCode}.{Environment.NewLine}{responseBody}");
    }

    string json = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonDocument.Parse(json);
}

static async Task<string> PostJsonTextAsync(string pipeName, string path, object body, CancellationToken cancellationToken)
{
    using HttpResponseMessage response = await PostJsonResponseAsync(pipeName, path, body, cancellationToken);
    if (response.StatusCode != HttpStatusCode.Created && response.StatusCode != HttpStatusCode.OK)
    {
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Expected HTTP 201/200 for POST {pipeName}{path}, got {(int)response.StatusCode}.{Environment.NewLine}{responseBody}");
    }

    return await response.Content.ReadAsStringAsync(cancellationToken);
}

static async Task<HttpResponseMessage> PostJsonResponseAsync(string pipeName, string path, object body, CancellationToken cancellationToken)
{
    using HttpClient client = CreatePipeHttpClient(pipeName);
    return await client.PostAsJsonAsync(path, body, cancellationToken);
}

static async Task<HttpResponseMessage> PostEmptyAsync(string pipeName, string path, CancellationToken cancellationToken)
{
    using HttpClient client = CreatePipeHttpClient(pipeName);
    return await client.PostAsync(path, null, cancellationToken);
}

static async Task<byte[]> GetBytesAsync(string pipeName, string path, CancellationToken cancellationToken)
{
    using HttpClient client = CreatePipeHttpClient(pipeName);
    return await client.GetByteArrayAsync(path, cancellationToken);
}

static async Task<byte[]> PostJsonBytesAsync(string pipeName, string path, object body, CancellationToken cancellationToken)
{
    using HttpClient client = CreatePipeHttpClient(pipeName);
    using HttpResponseMessage response = await client.PostAsJsonAsync(path, body, cancellationToken);
    if (response.StatusCode != HttpStatusCode.OK)
    {
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Expected HTTP 200 for POST {pipeName}{path}, got {(int)response.StatusCode}.{Environment.NewLine}{responseBody}");
    }

    return await response.Content.ReadAsByteArrayAsync(cancellationToken);
}

static async Task<HttpResponseMessage> DeleteAsync(string pipeName, string path, CancellationToken cancellationToken)
{
    using HttpClient client = CreatePipeHttpClient(pipeName);
    return await client.DeleteAsync(path, cancellationToken);
}

static async Task<HttpResponseMessage> SendAsync(string pipeName, string path, CancellationToken cancellationToken)
{
    using HttpClient client = CreatePipeHttpClient(pipeName);
    return await client.GetAsync(path, cancellationToken);
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
        process.Kill();
        throw new TimeoutException($"wslc {arguments} timed out.");
    }

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"wslc {arguments} failed with exit code {process.ExitCode}.{Environment.NewLine}{stderr}");
    }

    return JsonDocument.Parse(stdout);
}

static RuntimeCommandResult RunWslcCommand(string arguments, bool allowFailure = false)
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
        process.Kill();
        throw new TimeoutException($"wslc {arguments} timed out.");
    }

    if (process.ExitCode != 0 && !allowFailure)
    {
        throw new InvalidOperationException($"wslc {arguments} failed with exit code {process.ExitCode}.{Environment.NewLine}{stderr}");
    }

    return new RuntimeCommandResult(process.ExitCode, stdout, stderr);
}

static void RequireImagePresent(string repository, string tag)
{
    RequireImageState(repository, tag, expected: true);
}

static void RequireImageAbsent(string repository, string tag)
{
    RequireImageState(repository, tag, expected: false);
}

static void RequireImageState(string repository, string tag, bool expected)
{
    using JsonDocument images = RunWslcJsonArray("image list --format json");
    bool exists = images.RootElement.EnumerateArray()
        .Any(image => GetString(image, "Repository").Equals(repository, StringComparison.OrdinalIgnoreCase) &&
            DefaultIfWhiteSpace(GetString(image, "Tag"), "latest").Equals(tag, StringComparison.OrdinalIgnoreCase));

    if (exists != expected)
    {
        throw new InvalidOperationException(expected
            ? $"Expected WSLC image '{repository}:{tag}' to exist."
            : $"Expected WSLC image '{repository}:{tag}' to be removed.");
    }
}

static string NormalizeImageId(string repository, string tag)
{
    using JsonDocument images = RunWslcJsonArray("image list --format json");
    JsonElement image = images.RootElement.EnumerateArray()
        .FirstOrDefault(item => GetString(item, "Repository").Equals(repository, StringComparison.OrdinalIgnoreCase) &&
            DefaultIfWhiteSpace(GetString(item, "Tag"), "latest").Equals(tag, StringComparison.OrdinalIgnoreCase));

    if (image.ValueKind == JsonValueKind.Undefined)
    {
        throw new InvalidOperationException($"Expected WSLC image '{repository}:{tag}' to exist.");
    }

    return NormalizeId(GetString(image, "Id", "ID"));
}

static void RequireContainerPresent(string name, bool expected)
{
    using JsonDocument containers = RunWslcJsonArray("container list --all --format json");
    bool exists = containers.RootElement.EnumerateArray()
        .Any(container => GetString(container, "Name").Equals(name, StringComparison.OrdinalIgnoreCase));

    if (exists != expected)
    {
        throw new InvalidOperationException(expected
            ? $"Expected WSLC container '{name}' to exist."
            : $"Expected WSLC container '{name}' to be removed.");
    }
}

static void RequireContainerState(string name, string expectedDockerState)
{
    using JsonDocument containers = RunWslcJsonArray("container list --all --format json");
    JsonElement container = containers.RootElement.EnumerateArray()
        .FirstOrDefault(item => GetString(item, "Name").Equals(name, StringComparison.OrdinalIgnoreCase));

    if (container.ValueKind == JsonValueKind.Undefined)
    {
        throw new InvalidOperationException($"Expected WSLC container '{name}' to exist.");
    }

    string actual = DockerStateFromWslc(container);
    if (!actual.Equals(expectedDockerState, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected WSLC container '{name}' state '{expectedDockerState}', got '{actual}'.");
    }
}

static async Task WaitForContainerStateAsync(string name, string expectedDockerState, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        using JsonDocument containers = RunWslcJsonArray("container list --all --format json");
        JsonElement container = containers.RootElement.EnumerateArray()
            .FirstOrDefault(item => GetString(item, "Name").Equals(name, StringComparison.OrdinalIgnoreCase));

        if (container.ValueKind != JsonValueKind.Undefined &&
            DockerStateFromWslc(container).Equals(expectedDockerState, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await Task.Delay(500, cancellationToken);
    }
}

static void RequireContainerNotRunning(string name)
{
    using JsonDocument containers = RunWslcJsonArray("container list --all --format json");
    JsonElement container = containers.RootElement.EnumerateArray()
        .FirstOrDefault(item => GetString(item, "Name").Equals(name, StringComparison.OrdinalIgnoreCase));

    if (container.ValueKind == JsonValueKind.Undefined)
    {
        throw new InvalidOperationException($"Expected WSLC container '{name}' to exist.");
    }

    string actual = DockerStateFromWslc(container);
    if (actual.Equals("running", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected WSLC container '{name}' to be stopped or exited, got running.");
    }
}

static string DockerStateFromWslc(JsonElement container)
{
    if (!container.TryGetProperty("State", out JsonElement state))
    {
        return "unknown";
    }

    if (state.ValueKind == JsonValueKind.Number && state.TryGetInt32(out int numeric))
    {
        return numeric switch
        {
            1 => "created",
            2 => "running",
            3 => "exited",
            _ => "unknown"
        };
    }

    string text = state.GetString() ?? string.Empty;
    if (text.Contains("running", StringComparison.OrdinalIgnoreCase))
    {
        return "running";
    }

    if (text.Contains("created", StringComparison.OrdinalIgnoreCase))
    {
        return "created";
    }

    if (text.Contains("exited", StringComparison.OrdinalIgnoreCase) || text.Contains("stopped", StringComparison.OrdinalIgnoreCase))
    {
        return "exited";
    }

    return "unknown";
}

static string DecodeRawStream(byte[] bytes)
{
    var output = new StringBuilder();
    int offset = 0;
    while (offset < bytes.Length)
    {
        if (bytes.Length - offset < 8)
        {
            output.Append(Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset));
            break;
        }

        int length = (bytes[offset + 4] << 24) |
            (bytes[offset + 5] << 16) |
            (bytes[offset + 6] << 8) |
            bytes[offset + 7];
        offset += 8;

        if (length < 0 || offset + length > bytes.Length)
        {
            output.Append(Encoding.UTF8.GetString(bytes, offset - 8, bytes.Length - (offset - 8)));
            break;
        }

        output.Append(Encoding.UTF8.GetString(bytes, offset, length));
        offset += length;
    }

    return output.ToString();
}

static void RequireContainerListParity(JsonDocument cliContainers, JsonDocument dockerContainers)
{
    var expected = cliContainers.RootElement.EnumerateArray()
        .Select(container => $"{NormalizeId(GetString(container, "Id", "ID"))}|/{GetString(container, "Name")}|{GetString(container, "Image")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var actual = dockerContainers.RootElement.EnumerateArray()
        .Select(container =>
        {
            string name = container.GetProperty("Names").EnumerateArray().First().GetString() ?? string.Empty;
            return $"{NormalizeId(GetString(container, "Id"))}|{name}|{GetString(container, "Image")}";
        })
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    RequireSequenceEqual(expected, actual, "Docker container list parity failed.");
}

static void RequireImageListParity(JsonDocument cliImages, JsonDocument dockerImages)
{
    var expected = cliImages.RootElement.EnumerateArray()
        .Select(image => $"{NormalizeId(GetString(image, "Id", "ID"))}|{GetString(image, "Repository")}:{DefaultIfWhiteSpace(GetString(image, "Tag"), "latest")}")
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var actual = dockerImages.RootElement.EnumerateArray()
        .Select(image =>
        {
            string tag = image.GetProperty("RepoTags").EnumerateArray().First().GetString() ?? string.Empty;
            return $"{NormalizeId(GetString(image, "Id"))}|{tag}";
        })
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    RequireSequenceEqual(expected, actual, "Docker image list parity failed.");
}

static void RequireSequenceEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string message)
{
    if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"{message}{Environment.NewLine}Expected:{Environment.NewLine}{string.Join(Environment.NewLine, expected)}{Environment.NewLine}Actual:{Environment.NewLine}{string.Join(Environment.NewLine, actual)}");
    }
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

static void RequireJsonProperty(JsonDocument document, string propertyName)
{
    if (!document.RootElement.TryGetProperty(propertyName, out _))
    {
        throw new InvalidOperationException($"Expected JSON property '{propertyName}', got: {document.RootElement}");
    }
}

static void RequireStatus(HttpResponseMessage response, HttpStatusCode expected, string operation)
{
    if (response.StatusCode != expected)
    {
        string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        throw new InvalidOperationException($"{operation} returned {(int)response.StatusCode}, expected {(int)expected}.{Environment.NewLine}{body}");
    }
}

static void RequireEquals(string value, string expected, string message)
{
    if (!string.Equals(value.Trim(), expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message}{Environment.NewLine}Actual: {value}");
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

static async Task CleanupDaemonAsync(Process daemon)
{
    if (daemon.HasExited)
    {
        return;
    }

    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using HttpClient client = CreatePipeHttpClient(NativePipe);
        await client.PostAsync("/__shutdown", null, timeout.Token);
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

internal sealed record RuntimeCommandResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
