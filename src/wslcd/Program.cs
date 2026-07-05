using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using WslcDesktop.Contracts;
using WslcDesktop.Runtime;
using WslcDesktop.Runtime.Providers.DockerApi;
using WslcDesktop.Runtime.Providers.WslcCli;
using Wslcd;

[assembly: SupportedOSPlatform("windows")]

var options = WslcdOptions.CreateFromEnvironment();
Directory.CreateDirectory(options.LogDirectory);
string logPath = Path.Combine(options.LogDirectory, $"{WslcdDefaults.DaemonName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
var log = new DaemonLog(logPath);
DateTimeOffset startedAt = DateTimeOffset.UtcNow;
IContainerRuntimeProvider runtimeProvider = CreateRuntimeProvider(options);
var operationTracker = new OperationTracker(runtimeProvider.Name, options.OperationRetentionCount);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    if (!shutdown.IsCancellationRequested)
    {
        shutdown.Cancel();
    }
};

string internalDockerPipeName = InternalDockerPipeName(options.DockerPipeName);
log.Write($"starting nativePipe={options.NativePipeName} dockerPipe={options.DockerPipeName} internalDockerPipe={internalDockerPipeName} provider={runtimeProvider.Name}");

WebApplication nativeApp = CreateApp(args, options.NativePipeName);
MapNativeApi(nativeApp, options, startedAt, shutdown, runtimeProvider, operationTracker);

WebApplication dockerApp = CreateApp(args, internalDockerPipeName);
MapDockerApi(dockerApp, runtimeProvider);
var dockerPipeProxy = new DockerPipeProxy(options.DockerPipeName, internalDockerPipeName, runtimeProvider);

Task nativeTask = nativeApp.RunAsync(shutdown.Token);
Task dockerTask = dockerApp.RunAsync(shutdown.Token);
Task dockerProxyTask = dockerPipeProxy.RunAsync(shutdown.Token);

try
{
    await Task.WhenAll(nativeTask, dockerTask, dockerProxyTask);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}
finally
{
    log.Write("stopped");
}

static WebApplication CreateApp(string[] args, string pipeName)
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });

    builder.Services.Configure<JsonOptions>(jsonOptions =>
    {
        jsonOptions.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

    builder.Services.Configure<NamedPipeTransportOptions>(pipeOptions =>
    {
        pipeOptions.CurrentUserOnly = true;
    });

    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        kestrel.AddServerHeader = false;
        kestrel.ListenNamedPipe(pipeName, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1;
        });
    });

    return builder.Build();
}

static string InternalDockerPipeName(string publicDockerPipeName)
{
    return $"{publicDockerPipeName}-internal-{Environment.ProcessId}";
}

static void MapNativeApi(
    WebApplication app,
    WslcdOptions options,
    DateTimeOffset startedAt,
    CancellationTokenSource shutdown,
    IContainerRuntimeProvider runtimeProvider,
    OperationTracker operationTracker)
{
    app.MapGet("/healthz", () => Results.Text("OK", "text/plain"));

    app.MapGet("/v1/health", () => new WslcdHealthResponse(
        "Healthy",
        WslcdDefaults.DaemonName,
        options.NativePipeName,
        options.DockerPipeName,
        runtimeProvider.Name,
        options.LogDirectory,
        options.OperationRetentionCount,
        startedAt));

    app.MapGet("/v1/version", () => new WslcdVersionResponse(
        WslcdDefaults.ProductName,
        WslcdDefaults.DaemonName,
        WslcdDefaults.NativeApiVersion,
        WslcdDefaults.DockerApiVersion,
        WslcdDefaults.DockerMinApiVersion,
        runtimeProvider.Name,
        WslcdDefaults.OperatingSystem,
        WslcdDefaults.Architecture,
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"));

    app.MapGet("/v1/runtime", (CancellationToken cancellationToken) =>
        NativeResult(() => runtimeProvider.GetRuntimeInfoAsync(cancellationToken)));

    app.MapPost("/v1/providers/preview", (ProviderPreviewRequest request, CancellationToken cancellationToken) =>
        NativeResult(() => PreviewProviderAsync(request, cancellationToken)));

    app.MapGet("/v1/containers", (CancellationToken cancellationToken) =>
        NativeResult(() => runtimeProvider.ListContainersAsync(cancellationToken)));

    app.MapGet("/v1/containers/{id}", (string id, CancellationToken cancellationToken) =>
        NativeResult(() => runtimeProvider.InspectContainerAsync(id, cancellationToken)));

    app.MapPost("/v1/containers", (ContainerCreateRequestDto request, CancellationToken cancellationToken) =>
        NativeResult(() => operationTracker.TrackResultAsync("container", request.Name, "create", () => runtimeProvider.CreateContainerAsync(request, cancellationToken))));

    app.MapPost("/v1/containers/{id}/start", (string id, CancellationToken cancellationToken) =>
        NativeNoContent(() => operationTracker.TrackAsync("container", id, "start", () => runtimeProvider.StartContainerAsync(id, cancellationToken))));

    app.MapPost("/v1/containers/{id}/stop", (string id, CancellationToken cancellationToken) =>
        NativeNoContent(() => operationTracker.TrackAsync("container", id, "stop", () => runtimeProvider.StopContainerAsync(id, new ContainerStopOptionsDto(10), cancellationToken))));

    app.MapDelete("/v1/containers/{id}", (string id, CancellationToken cancellationToken) =>
        NativeNoContent(() => operationTracker.TrackAsync("container", id, "remove", () => runtimeProvider.RemoveContainerAsync(id, new ContainerRemoveOptionsDto(true, true), cancellationToken))));

    app.MapGet("/v1/containers/{id}/logs", (string id, HttpContext context, CancellationToken cancellationToken) =>
        NativeResult(() => CollectAsync(runtimeProvider.StreamLogsAsync(id, new LogStreamOptions(
            QueryInt(context, "tail"),
            QueryBool(context, "follow"),
            QueryBool(context, "timestamps"),
            QueryTimestamp(context, "since"),
            QueryTimestamp(context, "until")), cancellationToken), cancellationToken)));

    app.MapPost("/v1/containers/{id}/exec", (string id, ExecCreateRequestDto request, CancellationToken cancellationToken) =>
        NativeResult(() => operationTracker.TrackResultAsync("exec", id, "create", () => runtimeProvider.CreateExecAsync(id, request, cancellationToken))));

    app.MapPost("/v1/exec/{id}/start", (string id, ExecStartOptionsDto request, CancellationToken cancellationToken) =>
        NativeResult(() => operationTracker.TrackResultAsync("exec", id, "start", () => CollectAsync(runtimeProvider.StartExecAsync(id, request, cancellationToken), cancellationToken))));

    app.MapGet("/v1/exec/{id}", (string id, CancellationToken cancellationToken) =>
        NativeResult(() => runtimeProvider.InspectExecAsync(id, cancellationToken)));

    app.MapGet("/v1/images", (CancellationToken cancellationToken) =>
        NativeResult(() => runtimeProvider.ListImagesAsync(cancellationToken)));

    app.MapPost("/v1/images/pull", (ImagePullRequest request, CancellationToken cancellationToken) =>
        NativeResult(() => operationTracker.TrackResultAsync("image", request.Reference, "pull", () => runtimeProvider.PullImageAsync(request.Reference, cancellationToken))));

    app.MapPost("/v1/images/pull/stream", (ImagePullRequest request, HttpContext context, CancellationToken cancellationToken) =>
        NativeImagePullStreamAsync(runtimeProvider, operationTracker, request, context, cancellationToken));

    app.MapDelete("/v1/images/{**idOrName}", (string idOrName, CancellationToken cancellationToken) =>
    {
        string imageReference = WslcdRouteValues.DecodePathValue(idOrName);
        return NativeNoContent(() => operationTracker.TrackAsync("image", imageReference, "remove", () => runtimeProvider.RemoveImageAsync(imageReference, cancellationToken)));
    });

    app.MapGet("/v1/volumes", (CancellationToken cancellationToken) =>
        NativeResult(() => runtimeProvider.ListVolumesAsync(cancellationToken)));

    app.MapPost("/v1/volumes", (VolumeCreateRequestDto request, CancellationToken cancellationToken) =>
        NativeResult(() => operationTracker.TrackResultAsync("volume", request.Name, "create", () => runtimeProvider.CreateVolumeAsync(request.Name, request.Labels, cancellationToken))));

    app.MapDelete("/v1/volumes/{name}", (string name, CancellationToken cancellationToken) =>
        NativeNoContent(() => operationTracker.TrackAsync("volume", name, "remove", () => runtimeProvider.RemoveVolumeAsync(name, force: true, cancellationToken))));

    app.MapGet("/v1/networks", (CancellationToken cancellationToken) =>
        NativeResult(() => runtimeProvider.ListNetworksAsync(cancellationToken)));

    app.MapGet("/v1/operations", () => Results.Json(operationTracker.List()));

    app.MapGet("/v1/diagnostics", (CancellationToken cancellationToken) =>
        NativeResult(() => NativeDiagnosticsAsync(options, runtimeProvider, operationTracker, cancellationToken)));

    if (Environment.GetEnvironmentVariable("WSLCD_ENABLE_TEST_SHUTDOWN") == "1")
    {
        app.MapPost("/__shutdown", () =>
        {
            _ = Task.Run(shutdown.Cancel);
            return Results.Text("OK", "text/plain");
        });
    }
}

static async Task<IReadOnlyList<T>> CollectAsync<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
{
    var results = new List<T>();
    await foreach (T item in source.WithCancellation(cancellationToken))
    {
        results.Add(item);
    }

    return results;
}

static IContainerRuntimeProvider CreateRuntimeProvider(WslcdOptions options)
{
    if (options.RuntimeProviderName.Equals(RuntimeProviderSelection.DockerApi, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(options.DockerApiHost))
    {
        try
        {
            return new DockerApiProvider(DockerApiProviderOptions.Create(options.DockerApiHost, options.AllowTcpDockerApi));
        }
        catch (InvalidOperationException)
        {
        }
    }

    WslcRuntimeSettings wslcSettings = WslcSettingsFromOptions(options);
    return new WslcCliProvider(new WslcCommandRunner(wslcSettings), wslcSettings);
}

static async Task<ProviderPreviewResponse> PreviewProviderAsync(ProviderPreviewRequest request, CancellationToken cancellationToken)
{
    string providerName = string.IsNullOrWhiteSpace(request.RuntimeProviderName)
        ? RuntimeProviderSelection.WslcCli
        : request.RuntimeProviderName.Trim();

    if (providerName.Equals(RuntimeProviderSelection.DockerApi, StringComparison.OrdinalIgnoreCase))
    {
        var dockerOptions = DockerApiProviderOptions.Create(request.DockerApiHost ?? string.Empty, request.AllowTcpDockerApi);
        if (!dockerOptions.IsConfigured)
        {
            return new ProviderPreviewResponse(
                RuntimeProviderSelection.DockerApi,
                string.Empty,
                false,
                false,
                "Docker API host is not configured.",
                EmptyCapabilities());
        }

        try
        {
            using var provider = new DockerApiProvider(dockerOptions);
            RuntimeInfoResponse runtime = await provider.GetRuntimeInfoAsync(cancellationToken);
            return new ProviderPreviewResponse(
                RuntimeProviderSelection.DockerApi,
                dockerOptions.Host,
                true,
                runtime.IsAvailable,
                runtime.Message,
                runtime.Capabilities);
        }
        catch (InvalidOperationException ex)
        {
            return new ProviderPreviewResponse(
                RuntimeProviderSelection.DockerApi,
                dockerOptions.Host,
                true,
                false,
                ex.Message,
                EmptyCapabilities());
        }
    }

    WslcRuntimeSettings settings = WslcRuntimeSettings.FromCurrentProcess();
    var wslcProvider = new WslcCliProvider(new WslcCommandRunner(settings), settings);
    RuntimeInfoResponse wslcRuntime = await wslcProvider.GetRuntimeInfoAsync(cancellationToken);
    return new ProviderPreviewResponse(
        RuntimeProviderSelection.WslcCli,
        "wslc.exe",
        true,
        wslcRuntime.IsAvailable,
        wslcRuntime.Message,
        wslcRuntime.Capabilities);
}

static WslcRuntimeSettings WslcSettingsFromOptions(WslcdOptions options)
{
    return WslcRuntimeSettings.FromValues(
        options.WslcHttpProxy,
        options.WslcHttpsProxy,
        options.WslcNoProxy,
        options.WslcImageMirror,
        options.WslcRewriteImageTag,
        options.WslcRemoveRewrittenSourceTag,
        options.WslcEnvironment);
}

static RuntimeCapabilities EmptyCapabilities()
{
    return new RuntimeCapabilities(
        ListContainers: false,
        InspectContainer: false,
        CreateContainer: false,
        StartStopContainer: false,
        RemoveContainer: false,
        NamedVolumes: false,
        BindMounts: false,
        PortPublishing: false,
        RestartPolicy: false,
        LogsBuffered: false,
        LogsFollow: false,
        StatsOneShot: false,
        StatsStreaming: false,
        ExecNonInteractive: false,
        ExecInteractiveTty: false,
        DockerNetworkCrud: false,
        ImagePull: false,
        ImagePullProgress: false,
        ImageRemove: false,
        ImagePush: false,
        ImageBuild: false);
}

static async Task<WslcdDiagnosticsResponse> NativeDiagnosticsAsync(
    WslcdOptions options,
    IContainerRuntimeProvider runtimeProvider,
    OperationTracker operationTracker,
    CancellationToken cancellationToken)
{
    var health = new WslcdHealthResponse(
        "Healthy",
        WslcdDefaults.DaemonName,
        options.NativePipeName,
        options.DockerPipeName,
        runtimeProvider.Name,
        options.LogDirectory,
        options.OperationRetentionCount,
        DateTimeOffset.UtcNow);

    var version = new WslcdVersionResponse(
        WslcdDefaults.ProductName,
        WslcdDefaults.DaemonName,
        WslcdDefaults.NativeApiVersion,
        WslcdDefaults.DockerApiVersion,
        WslcdDefaults.DockerMinApiVersion,
        runtimeProvider.Name,
        WslcdDefaults.OperatingSystem,
        WslcdDefaults.Architecture,
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");

    RuntimeInfoResponse runtime = await runtimeProvider.GetRuntimeInfoAsync(cancellationToken);
    return new WslcdDiagnosticsResponse(health, version, runtime, operationTracker.List(), DateTimeOffset.UtcNow);
}

static async Task<IResult> NativeResult<T>(Func<Task<T>> action)
{
    try
    {
        return Results.Json(await action());
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new NativeErrorResponse("resource.not_found", ex.Message, WslcdDefaults.DefaultRuntimeProviderName, string.Empty, null, string.Empty, string.Empty));
    }
    catch (RuntimeCommandException ex)
    {
        return Results.Problem(
            title: "Provider command failed.",
            detail: ex.Message,
            statusCode: 500,
            extensions: new Dictionary<string, object?>
            {
                ["error"] = new NativeErrorResponse(
                    "provider.command_failed",
                    ex.Message,
                    ex.Provider,
                    ex.Result.Command,
                    ex.Result.ExitCode,
                    Tail(ex.Result.StandardOutput),
                    Tail(ex.Result.StandardError))
            });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new NativeErrorResponse("request.invalid", ex.Message, WslcdDefaults.DefaultRuntimeProviderName, string.Empty, null, string.Empty, string.Empty));
    }
}

static async Task<IResult> NativeNoContent(Func<Task> action)
{
    try
    {
        await action();
        return Results.NoContent();
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new NativeErrorResponse("resource.not_found", ex.Message, WslcdDefaults.DefaultRuntimeProviderName, string.Empty, null, string.Empty, string.Empty));
    }
    catch (RuntimeCommandException ex)
    {
        return Results.Problem(
            title: "Provider command failed.",
            detail: ex.Message,
            statusCode: 500,
            extensions: new Dictionary<string, object?>
            {
                ["error"] = new NativeErrorResponse(
                    "provider.command_failed",
                    ex.Message,
                    ex.Provider,
                    ex.Result.Command,
                    ex.Result.ExitCode,
                    Tail(ex.Result.StandardOutput),
                    Tail(ex.Result.StandardError))
            });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new NativeErrorResponse("request.invalid", ex.Message, WslcdDefaults.DefaultRuntimeProviderName, string.Empty, null, string.Empty, string.Empty));
    }
}

static string Tail(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    return value.Length <= 4096 ? value : value[^4096..];
}

static void MapDockerApi(WebApplication app, IContainerRuntimeProvider runtimeProvider)
{
    app.MapGet("/_ping", () => Results.Text("OK", "text/plain"));
    app.MapGet("/v{apiVersion}/_ping", () => Results.Text("OK", "text/plain"));

    app.MapGet("/version", () => DockerVersion());
    app.MapGet("/v{apiVersion}/version", () => DockerVersion());
    app.MapGet("/info", (CancellationToken cancellationToken) => DockerResult(() => DockerInfo(runtimeProvider, cancellationToken)));
    app.MapGet("/v{apiVersion}/info", (CancellationToken cancellationToken) => DockerResult(() => DockerInfo(runtimeProvider, cancellationToken)));
    app.MapGet("/containers/json", (HttpContext context, CancellationToken cancellationToken) => DockerResult(() => DockerContainers(runtimeProvider, context, cancellationToken)));
    app.MapGet("/v{apiVersion}/containers/json", (HttpContext context, CancellationToken cancellationToken) => DockerResult(() => DockerContainers(runtimeProvider, context, cancellationToken)));
    app.MapPost("/containers/create", (HttpContext context, CancellationToken cancellationToken) => DockerCreateContainer(runtimeProvider, context, cancellationToken));
    app.MapPost("/v{apiVersion}/containers/create", (HttpContext context, CancellationToken cancellationToken) => DockerCreateContainer(runtimeProvider, context, cancellationToken));
    app.MapGet("/containers/{id}/json", (string id, CancellationToken cancellationToken) => DockerResult(() => DockerContainerInspect(runtimeProvider, id, cancellationToken)));
    app.MapGet("/v{apiVersion}/containers/{id}/json", (string id, CancellationToken cancellationToken) => DockerResult(() => DockerContainerInspect(runtimeProvider, id, cancellationToken)));
    app.MapPost("/containers/{id}/start", (string id, CancellationToken cancellationToken) => DockerNoContent(() => runtimeProvider.StartContainerAsync(id, cancellationToken)));
    app.MapPost("/v{apiVersion}/containers/{id}/start", (string id, CancellationToken cancellationToken) => DockerNoContent(() => runtimeProvider.StartContainerAsync(id, cancellationToken)));
    app.MapPost("/containers/{id}/stop", (string id, HttpContext context, CancellationToken cancellationToken) => DockerNoContent(() => runtimeProvider.StopContainerAsync(id, new ContainerStopOptionsDto(QueryInt(context, "t")), cancellationToken)));
    app.MapPost("/v{apiVersion}/containers/{id}/stop", (string id, HttpContext context, CancellationToken cancellationToken) => DockerNoContent(() => runtimeProvider.StopContainerAsync(id, new ContainerStopOptionsDto(QueryInt(context, "t")), cancellationToken)));
    app.MapPost("/containers/{id}/restart", (string id, HttpContext context, CancellationToken cancellationToken) => DockerRestartContainer(runtimeProvider, id, context, cancellationToken));
    app.MapPost("/v{apiVersion}/containers/{id}/restart", (string id, HttpContext context, CancellationToken cancellationToken) => DockerRestartContainer(runtimeProvider, id, context, cancellationToken));
    app.MapPost("/containers/{id}/kill", (string id, HttpContext context, CancellationToken cancellationToken) => DockerKillContainer(runtimeProvider, id, context, cancellationToken));
    app.MapPost("/v{apiVersion}/containers/{id}/kill", (string id, HttpContext context, CancellationToken cancellationToken) => DockerKillContainer(runtimeProvider, id, context, cancellationToken));
    app.MapDelete("/containers/{id}", (string id, HttpContext context, CancellationToken cancellationToken) => DockerNoContent(() => runtimeProvider.RemoveContainerAsync(id, new ContainerRemoveOptionsDto(QueryBool(context, "force"), QueryBool(context, "v")), cancellationToken)));
    app.MapDelete("/v{apiVersion}/containers/{id}", (string id, HttpContext context, CancellationToken cancellationToken) => DockerNoContent(() => runtimeProvider.RemoveContainerAsync(id, new ContainerRemoveOptionsDto(QueryBool(context, "force"), QueryBool(context, "v")), cancellationToken)));
    app.MapGet("/containers/{id}/logs", (string id, HttpContext context, CancellationToken cancellationToken) => DockerLogs(runtimeProvider, id, context, cancellationToken));
    app.MapGet("/v{apiVersion}/containers/{id}/logs", (string id, HttpContext context, CancellationToken cancellationToken) => DockerLogs(runtimeProvider, id, context, cancellationToken));
    app.MapGet("/containers/{id}/stats", (string id, HttpContext context, CancellationToken cancellationToken) => DockerStats(runtimeProvider, id, context, cancellationToken));
    app.MapGet("/v{apiVersion}/containers/{id}/stats", (string id, HttpContext context, CancellationToken cancellationToken) => DockerStats(runtimeProvider, id, context, cancellationToken));
    app.MapPost("/containers/{id}/exec", (string id, JsonElement body, CancellationToken cancellationToken) => DockerResult(() => DockerExecCreate(runtimeProvider, id, body, cancellationToken)));
    app.MapPost("/v{apiVersion}/containers/{id}/exec", (string id, JsonElement body, CancellationToken cancellationToken) => DockerResult(() => DockerExecCreate(runtimeProvider, id, body, cancellationToken)));
    app.MapPost("/exec/{id}/start", (HttpContext context, string id, CancellationToken cancellationToken) => DockerExecStart(runtimeProvider, context, id, cancellationToken));
    app.MapPost("/v{apiVersion}/exec/{id}/start", (HttpContext context, string id, CancellationToken cancellationToken) => DockerExecStart(runtimeProvider, context, id, cancellationToken));
    app.MapGet("/exec/{id}/json", (string id, CancellationToken cancellationToken) => DockerResult(() => DockerExecInspect(runtimeProvider, id, cancellationToken)));
    app.MapGet("/v{apiVersion}/exec/{id}/json", (string id, CancellationToken cancellationToken) => DockerResult(() => DockerExecInspect(runtimeProvider, id, cancellationToken)));
    app.MapPost("/exec/{id}/resize", (string id, CancellationToken cancellationToken) => DockerExecResize(runtimeProvider, id, cancellationToken));
    app.MapPost("/v{apiVersion}/exec/{id}/resize", (string id, CancellationToken cancellationToken) => DockerExecResize(runtimeProvider, id, cancellationToken));
    app.MapGet("/images/json", (CancellationToken cancellationToken) => DockerResult(() => DockerImages(runtimeProvider, cancellationToken)));
    app.MapGet("/v{apiVersion}/images/json", (CancellationToken cancellationToken) => DockerResult(() => DockerImages(runtimeProvider, cancellationToken)));
    app.MapGet("/images/search", () => DockerImageSearchUnsupported());
    app.MapGet("/v{apiVersion}/images/search", () => DockerImageSearchUnsupported());
    app.MapPost("/images/create", (HttpContext context, CancellationToken cancellationToken) => DockerImageCreate(runtimeProvider, context, cancellationToken));
    app.MapPost("/v{apiVersion}/images/create", (HttpContext context, CancellationToken cancellationToken) => DockerImageCreate(runtimeProvider, context, cancellationToken));
    app.MapGet("/images/{**name}", (string name, CancellationToken cancellationToken) => DockerImageGet(runtimeProvider, name, cancellationToken));
    app.MapGet("/v{apiVersion}/images/{**name}", (string name, CancellationToken cancellationToken) => DockerImageGet(runtimeProvider, name, cancellationToken));
    app.MapDelete("/images/{**name}", (string name, HttpContext context, CancellationToken cancellationToken) => DockerResult(() => DockerImageRemove(runtimeProvider, name, context, cancellationToken)));
    app.MapDelete("/v{apiVersion}/images/{**name}", (string name, HttpContext context, CancellationToken cancellationToken) => DockerResult(() => DockerImageRemove(runtimeProvider, name, context, cancellationToken)));
    app.MapGet("/volumes", (CancellationToken cancellationToken) => DockerResult(() => DockerVolumes(runtimeProvider, cancellationToken)));
    app.MapGet("/v{apiVersion}/volumes", (CancellationToken cancellationToken) => DockerResult(() => DockerVolumes(runtimeProvider, cancellationToken)));
    app.MapPost("/volumes/create", (JsonElement body, CancellationToken cancellationToken) => DockerResult(() => DockerVolumeCreate(runtimeProvider, body, cancellationToken)));
    app.MapPost("/v{apiVersion}/volumes/create", (JsonElement body, CancellationToken cancellationToken) => DockerResult(() => DockerVolumeCreate(runtimeProvider, body, cancellationToken)));
    app.MapGet("/volumes/{name}", (string name, CancellationToken cancellationToken) => DockerResult(() => DockerVolumeInspect(runtimeProvider, name, cancellationToken)));
    app.MapGet("/v{apiVersion}/volumes/{name}", (string name, CancellationToken cancellationToken) => DockerResult(() => DockerVolumeInspect(runtimeProvider, name, cancellationToken)));
    app.MapDelete("/volumes/{name}", (string name, HttpContext context, CancellationToken cancellationToken) => DockerNoContent(() => runtimeProvider.RemoveVolumeAsync(name, QueryBool(context, "force"), cancellationToken)));
    app.MapDelete("/v{apiVersion}/volumes/{name}", (string name, HttpContext context, CancellationToken cancellationToken) => DockerNoContent(() => runtimeProvider.RemoveVolumeAsync(name, QueryBool(context, "force"), cancellationToken)));
    app.MapGet("/networks", (HttpContext context, CancellationToken cancellationToken) => DockerResult(() => DockerNetworks(runtimeProvider, context, cancellationToken)));
    app.MapGet("/v{apiVersion}/networks", (HttpContext context, CancellationToken cancellationToken) => DockerResult(() => DockerNetworks(runtimeProvider, context, cancellationToken)));
    app.MapGet("/networks/{id}", (string id, CancellationToken cancellationToken) => DockerResult(() => DockerNetworkInspect(runtimeProvider, id, cancellationToken)));
    app.MapGet("/v{apiVersion}/networks/{id}", (string id, CancellationToken cancellationToken) => DockerResult(() => DockerNetworkInspect(runtimeProvider, id, cancellationToken)));
    app.MapPost("/networks/create", (JsonElement body) => DockerNetworkCreate(body));
    app.MapPost("/v{apiVersion}/networks/create", (JsonElement body) => DockerNetworkCreate(body));
    app.MapPost("/networks/{id}/connect", (string id) => DockerNetworkConnect(id));
    app.MapPost("/v{apiVersion}/networks/{id}/connect", (string id) => DockerNetworkConnect(id));
    app.MapPost("/networks/{id}/disconnect", (string id) => DockerNetworkDisconnect(id));
    app.MapPost("/v{apiVersion}/networks/{id}/disconnect", (string id) => DockerNetworkDisconnect(id));
    app.MapPost("/networks/prune", (HttpContext context) => DockerNetworkPrune(context));
    app.MapPost("/v{apiVersion}/networks/prune", (HttpContext context) => DockerNetworkPrune(context));
    app.MapDelete("/networks/{id}", (string id) => DockerNetworkDelete(id));
    app.MapDelete("/v{apiVersion}/networks/{id}", (string id) => DockerNetworkDelete(id));
}

static async Task<IResult> DockerCreateContainer(IContainerRuntimeProvider runtimeProvider, HttpContext context, CancellationToken cancellationToken)
{
    try
    {
        using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
        string name = context.Request.Query["name"].ToString();
        ContainerCreateRequestDto request = await MapDockerCreateRequest(runtimeProvider, name, document.RootElement, cancellationToken);
        var result = await runtimeProvider.CreateContainerAsync(request, cancellationToken);
        return Results.Json(new Dictionary<string, object?>
        {
            ["Id"] = result.Id,
            ["Warnings"] = result.Warnings
        }, statusCode: StatusCodes.Status201Created);
    }
    catch (DockerApiException ex)
    {
        return DockerError(ex.StatusCode, ex.Message);
    }
    catch (RuntimeCommandException ex)
    {
        return DockerError(MapCommandStatus(ex), ex.Message);
    }
    catch (DirectoryNotFoundException ex)
    {
        return DockerError(400, ex.Message);
    }
    catch (ArgumentException ex)
    {
        return DockerError(400, ex.Message);
    }
}

static async Task<ContainerCreateRequestDto> MapDockerCreateRequest(IContainerRuntimeProvider runtimeProvider, string name, JsonElement root, CancellationToken cancellationToken)
{
    string image = GetJsonString(root, "Image");
    if (string.IsNullOrWhiteSpace(image))
    {
        throw new DockerApiException(400, "No command specified");
    }

    var images = await runtimeProvider.ListImagesAsync(cancellationToken);
    if (!images.Any(candidate =>
            candidate.Repository.Equals(image, StringComparison.OrdinalIgnoreCase) ||
            $"{candidate.Repository}:{candidate.Tag}".Equals(image, StringComparison.OrdinalIgnoreCase)))
    {
        throw new DockerApiException(404, $"No such image: {image}");
    }

    if (!string.IsNullOrWhiteSpace(name))
    {
        var containers = await runtimeProvider.ListContainersAsync(cancellationToken);
        if (containers.Any(container => container.Name.Equals(name.TrimStart('/'), StringComparison.OrdinalIgnoreCase)))
        {
            throw new DockerApiException(409, $"Conflict. The container name \"{name}\" is already in use.");
        }
    }

    var command = new List<string>();
    command.AddRange(ReadStringOrArray(root, "Entrypoint"));
    command.AddRange(ReadStringOrArray(root, "Cmd"));

    var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (string entry in ReadStringOrArray(root, "Env"))
    {
        int separator = entry.IndexOf('=', StringComparison.Ordinal);
        if (separator > 0)
        {
            environment[entry[..separator]] = entry[(separator + 1)..];
        }
    }

    var labels = ReadLabels(root);
    var ports = new List<PortMappingDto>();
    var mounts = new List<ContainerMountDto>();
    bool autoRemove = false;

    if (TryGetJsonProperty(root, "HostConfig", out JsonElement hostConfig) && hostConfig.ValueKind == JsonValueKind.Object)
    {
        autoRemove = GetJsonBool(hostConfig, "AutoRemove");
        mounts.AddRange(ReadBinds(hostConfig));
        ports.AddRange(ReadPortBindings(hostConfig));
    }

    return new ContainerCreateRequestDto(
        name.TrimStart('/'),
        image,
        command,
        ports,
        mounts,
        environment,
        autoRemove,
        labels);
}

static async Task<IResult> DockerRestartContainer(IContainerRuntimeProvider runtimeProvider, string id, HttpContext context, CancellationToken cancellationToken)
{
    int timeout = QueryInt(context, "t");
    IResult stopped = await DockerNoContent(() => runtimeProvider.StopContainerAsync(id, new ContainerStopOptionsDto(timeout), cancellationToken));
    if (stopped is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status204NoContent })
    {
        return stopped;
    }

    return await DockerNoContent(() => runtimeProvider.StartContainerAsync(id, cancellationToken));
}

static Task<IResult> DockerKillContainer(IContainerRuntimeProvider runtimeProvider, string id, HttpContext context, CancellationToken cancellationToken)
{
    string signal = context.Request.Query["signal"].ToString();
    if (!string.IsNullOrWhiteSpace(signal) &&
        !signal.Equals("KILL", StringComparison.OrdinalIgnoreCase) &&
        !signal.Equals("SIGKILL", StringComparison.OrdinalIgnoreCase))
    {
        return Task.FromResult(DockerError(400, $"unsupported signal: {signal}"));
    }

    return DockerNoContent(() => runtimeProvider.StopContainerAsync(id, new ContainerStopOptionsDto(0), cancellationToken));
}

static IResult DockerVersion()
{
    return Results.Json(new Dictionary<string, object?>
    {
        ["Platform"] = new Dictionary<string, object?>
        {
            ["Name"] = WslcdDefaults.ProductName
        },
        ["Components"] = new object[]
        {
            new Dictionary<string, object?>
            {
                ["Name"] = WslcdDefaults.DaemonName,
                ["Version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                ["Details"] = new Dictionary<string, object?>
                {
                    ["Provider"] = WslcdDefaults.DefaultRuntimeProviderName
                }
            }
        },
        ["Version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
        ["ApiVersion"] = WslcdDefaults.DockerApiVersion,
        ["MinAPIVersion"] = WslcdDefaults.DockerMinApiVersion,
        ["GitCommit"] = "dev",
        ["GoVersion"] = "",
        ["Os"] = WslcdDefaults.OperatingSystem,
        ["Arch"] = WslcdDefaults.Architecture,
        ["KernelVersion"] = Environment.OSVersion.VersionString,
        ["Experimental"] = true,
        ["BuildTime"] = ""
    });
}

static async Task<object> DockerInfo(IContainerRuntimeProvider runtimeProvider, CancellationToken cancellationToken)
{
    var runtime = await runtimeProvider.GetRuntimeInfoAsync(cancellationToken);
    var containers = await runtimeProvider.ListContainersAsync(cancellationToken);
    var images = await runtimeProvider.ListImagesAsync(cancellationToken);

    return new Dictionary<string, object?>
    {
        ["ID"] = "wslc-desktop",
        ["Containers"] = containers.Count,
        ["ContainersRunning"] = containers.Count(container => container.State == ContainerRuntimeState.Running),
        ["ContainersPaused"] = 0,
        ["ContainersStopped"] = containers.Count(container => container.State != ContainerRuntimeState.Running),
        ["Images"] = images.Count,
        ["Driver"] = "wslc",
        ["DriverStatus"] = new object[] { new[] { "Provider", runtime.ProviderName }, new[] { "Available", runtime.IsAvailable ? "true" : "false" } },
        ["DockerRootDir"] = WslcdDefaults.DefaultDataRoot,
        ["Plugins"] = new Dictionary<string, object?>
        {
            ["Volume"] = Array.Empty<string>(),
            ["Network"] = Array.Empty<string>(),
            ["Authorization"] = Array.Empty<string>(),
            ["Log"] = new[] { "json-file" }
        },
        ["MemoryLimit"] = true,
        ["SwapLimit"] = false,
        ["KernelMemory"] = false,
        ["CpuCfsPeriod"] = false,
        ["CpuCfsQuota"] = false,
        ["CPUShares"] = true,
        ["CPUSet"] = false,
        ["PidsLimit"] = false,
        ["IPv4Forwarding"] = true,
        ["BridgeNfIptables"] = false,
        ["BridgeNfIp6tables"] = false,
        ["Debug"] = false,
        ["NFd"] = 0,
        ["OomKillDisable"] = false,
        ["NGoroutines"] = 0,
        ["SystemTime"] = DateTimeOffset.UtcNow.ToString("O"),
        ["LoggingDriver"] = "json-file",
        ["CgroupDriver"] = "wslc",
        ["NEventsListener"] = 0,
        ["KernelVersion"] = Environment.OSVersion.VersionString,
        ["OperatingSystem"] = WslcdDefaults.ProductName,
        ["OSVersion"] = Environment.OSVersion.VersionString,
        ["OSType"] = WslcdDefaults.OperatingSystem,
        ["Architecture"] = WslcdDefaults.Architecture,
        ["IndexServerAddress"] = "https://index.docker.io/v1/",
        ["RegistryConfig"] = new Dictionary<string, object?>(),
        ["NCPU"] = Environment.ProcessorCount,
        ["MemTotal"] = 0,
        ["GenericResources"] = Array.Empty<object>(),
        ["HttpProxy"] = "",
        ["HttpsProxy"] = "",
        ["NoProxy"] = "",
        ["Name"] = Environment.MachineName,
        ["Labels"] = Array.Empty<string>(),
        ["ExperimentalBuild"] = true,
        ["ServerVersion"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
    };
}

static async Task<object> DockerContainers(IContainerRuntimeProvider runtimeProvider, HttpContext context, CancellationToken cancellationToken)
{
    var containers = await runtimeProvider.ListContainersAsync(cancellationToken);
    bool all = QueryBool(context, "all");
    int limit = QueryInt(context, "limit");
    var filters = QueryFilters(context);

    IEnumerable<ContainerSummaryDto> query = containers;
    if (!all)
    {
        query = query.Where(container => container.State == ContainerRuntimeState.Running);
    }

    query = query.Where(container => MatchesContainerFilters(container, filters));
    if (limit > 0)
    {
        query = query.Take(limit);
    }

    return query.Select(DockerContainerSummary).ToArray();
}

static async Task<object> DockerContainerInspect(IContainerRuntimeProvider runtimeProvider, string id, CancellationToken cancellationToken)
{
    try
    {
        var container = await runtimeProvider.InspectContainerAsync(id, cancellationToken);
        return new Dictionary<string, object?>
        {
            ["Id"] = container.Id,
            ["Created"] = container.Created,
            ["Path"] = container.Command,
            ["Args"] = Array.Empty<string>(),
            ["State"] = new Dictionary<string, object?>
            {
                ["Status"] = DockerState(container.State),
                ["Running"] = container.State == ContainerRuntimeState.Running,
                ["Paused"] = false,
                ["Restarting"] = false,
                ["OOMKilled"] = false,
                ["Dead"] = false,
                ["Pid"] = 0,
                ["ExitCode"] = container.State == ContainerRuntimeState.Exited ? 0 : null,
                ["Error"] = "",
                ["StartedAt"] = "",
                ["FinishedAt"] = ""
            },
            ["Image"] = container.Image,
            ["Name"] = "/" + container.Name,
            ["RestartCount"] = 0,
            ["Driver"] = "wslc",
            ["Platform"] = WslcdDefaults.OperatingSystem,
            ["HostConfig"] = new Dictionary<string, object?>
            {
                ["NetworkMode"] = "default",
                ["RestartPolicy"] = new Dictionary<string, object?> { ["Name"] = "no", ["MaximumRetryCount"] = 0 }
            },
            ["Config"] = new Dictionary<string, object?>
            {
                ["Hostname"] = container.Name,
                ["Image"] = container.Image,
                ["Labels"] = ContainerLabels(container),
                ["Env"] = Array.Empty<string>(),
                ["Cmd"] = string.IsNullOrWhiteSpace(container.Command) || container.Command == "-" ? Array.Empty<string>() : new[] { container.Command }
            },
            ["NetworkSettings"] = new Dictionary<string, object?>
            {
                ["Ports"] = DockerPortMap(container.PortSummary),
                ["Networks"] = new Dictionary<string, object?>
                {
                    ["wslc"] = new Dictionary<string, object?>
                    {
                        ["NetworkID"] = "wslc",
                        ["EndpointID"] = container.Id,
                        ["IPAddress"] = "",
                        ["Gateway"] = ""
                    }
                }
            }
        };
    }
    catch (KeyNotFoundException)
    {
        throw new DockerApiException(404, $"No such container: {id}");
    }
}

static async Task<IResult> DockerLogs(IContainerRuntimeProvider runtimeProvider, string id, HttpContext context, CancellationToken cancellationToken)
{
    try
    {
        var options = new LogStreamOptions(
            QueryInt(context, "tail"),
            QueryBool(context, "follow"),
            QueryBool(context, "timestamps"),
            QueryTimestamp(context, "since"),
            QueryTimestamp(context, "until"));
        bool includeStdout = QueryBool(context, "stdout");
        bool includeStderr = QueryBool(context, "stderr");
        if (!includeStdout && !includeStderr)
        {
            includeStdout = true;
        }

        var frames = new List<byte>();
        await foreach (var frame in runtimeProvider.StreamLogsAsync(id, options, cancellationToken))
        {
            if (options.Since is not null && frame.Timestamp < options.Since)
            {
                continue;
            }

            if (options.Until is not null && frame.Timestamp > options.Until)
            {
                continue;
            }

            if (frame.Stream.Equals("stderr", StringComparison.OrdinalIgnoreCase) && !includeStderr)
            {
                continue;
            }

            if (!frame.Stream.Equals("stderr", StringComparison.OrdinalIgnoreCase) && !includeStdout)
            {
                continue;
            }

            string message = options.Timestamps ? $"{frame.Timestamp:O} {frame.Message}" : frame.Message;
            frames.AddRange(EncodeRawFrame(frame.Stream.Equals("stderr", StringComparison.OrdinalIgnoreCase) ? 2 : 1, message.EndsWith('\n') ? message : message + "\n"));
        }

        return Results.File(frames.ToArray(), "application/vnd.docker.raw-stream");
    }
    catch (KeyNotFoundException ex)
    {
        return DockerError(404, ex.Message);
    }
    catch (RuntimeCommandException ex)
    {
        return DockerError(MapCommandStatus(ex), ex.Message);
    }
}

static async Task<IResult> DockerStats(IContainerRuntimeProvider runtimeProvider, string id, HttpContext context, CancellationToken cancellationToken)
{
    try
    {
        bool oneShot = QueryBool(context, "one-shot");
        bool stream = QueryBoolDefault(context, "stream", true) && !oneShot;
        if (stream)
        {
            var runtime = await runtimeProvider.GetRuntimeInfoAsync(cancellationToken);
            if (!runtime.Capabilities.StatsStreaming)
            {
                return DockerError(501, "Stats streaming is not supported by the active WSLC runtime provider. Use stream=false.");
            }

            return Results.Stream(async body =>
            {
                await foreach (var item in runtimeProvider.StreamStatsAsync(id, new StatsStreamOptions(true), cancellationToken))
                {
                    string line = JsonSerializer.Serialize(BuildDockerStatsPayload(item, id)) + "\n";
                    byte[] bytes = Encoding.UTF8.GetBytes(line);
                    await body.WriteAsync(bytes, cancellationToken);
                    await body.FlushAsync(cancellationToken);
                }
            }, "application/json");
        }

        StatsFrameDto? frame = null;
        await foreach (var item in runtimeProvider.StreamStatsAsync(id, new StatsStreamOptions(false), cancellationToken))
        {
            frame = item;
            break;
        }

        return Results.Json(BuildDockerStatsPayload(frame, id));
    }
    catch (KeyNotFoundException ex)
    {
        return DockerError(404, ex.Message);
    }
    catch (RuntimeCommandException ex)
    {
        return DockerError(MapCommandStatus(ex), ex.Message);
    }
}

static Dictionary<string, object?> BuildDockerStatsPayload(StatsFrameDto? frame, string fallbackId)
{
    return new Dictionary<string, object?>
    {
        ["read"] = DateTimeOffset.UtcNow.ToString("O"),
        ["name"] = frame?.ContainerName ?? fallbackId,
        ["id"] = frame?.ContainerId ?? fallbackId,
        ["cpu_stats"] = new Dictionary<string, object?>
        {
            ["cpu_usage"] = new Dictionary<string, object?>
            {
                ["total_usage"] = (long)((frame?.CpuPercent ?? 0) * 1000),
                ["percpu_usage"] = Array.Empty<long>(),
                ["usage_in_kernelmode"] = 0,
                ["usage_in_usermode"] = 0
            },
            ["system_cpu_usage"] = 0,
            ["online_cpus"] = Environment.ProcessorCount,
            ["throttling_data"] = new Dictionary<string, object?>
            {
                ["periods"] = 0,
                ["throttled_periods"] = 0,
                ["throttled_time"] = 0
            }
        },
        ["precpu_stats"] = new Dictionary<string, object?>(),
        ["memory_stats"] = new Dictionary<string, object?>
        {
            ["usage"] = ParseMemoryBytes(frame?.MemoryUsed ?? "0"),
            ["max_usage"] = 0,
            ["stats"] = new Dictionary<string, object?>(),
            ["limit"] = 0
        },
        ["networks"] = new Dictionary<string, object?>(),
        ["blkio_stats"] = new Dictionary<string, object?>(),
        ["pids_stats"] = new Dictionary<string, object?>()
    };
}

static async Task<object> DockerExecCreate(IContainerRuntimeProvider runtimeProvider, string id, JsonElement body, CancellationToken cancellationToken)
{
    bool tty = GetJsonBool(body, "Tty");
    if (tty)
    {
        var runtime = await runtimeProvider.GetRuntimeInfoAsync(cancellationToken);
        if (!runtime.Capabilities.ExecInteractiveTty)
        {
            throw new DockerApiException(501, "Interactive TTY exec is not supported by the active WSLC runtime provider.");
        }
    }

    var request = new ExecCreateRequestDto(
        ReadStringOrArray(body, "Cmd"),
        string.Empty,
        GetJsonBool(body, "AttachStdout"),
        GetJsonBool(body, "AttachStderr"),
        tty);
    var result = await runtimeProvider.CreateExecAsync(id, request, cancellationToken);
    return new Dictionary<string, object?>
    {
        ["Id"] = result.ExecId
    };
}

static async Task<IResult> DockerExecStart(IContainerRuntimeProvider runtimeProvider, HttpContext context, string id, CancellationToken cancellationToken)
{
    try
    {
        bool upgradeRequested = IsDockerUpgradeRequest(context);
        var upgradeFeature = context.Features.Get<IHttpUpgradeFeature>();
        bool canUpgrade = upgradeRequested && upgradeFeature?.IsUpgradableRequest == true;

        using JsonDocument document = await ReadJsonBodyAsync(context, cancellationToken);
        JsonElement body = document.RootElement;
        var options = new ExecStartOptionsDto(GetJsonBool(body, "Detach"), GetJsonBool(body, "Tty"));
        if (options.Detach)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var _ in runtimeProvider.StartExecAsync(id, options, CancellationToken.None))
                {
                }
            }, CancellationToken.None);
            return Results.NoContent();
        }

        if (upgradeRequested)
        {
            if (canUpgrade && upgradeFeature is not null)
            {
                await using Stream stream = await upgradeFeature.UpgradeAsync();
                await WriteDockerExecRawStreamAsync(stream, runtimeProvider, id, options, cancellationToken);
                return Results.Empty;
            }

            await WriteDockerExecManualUpgradeAsync(context, runtimeProvider, id, options, cancellationToken);
            return Results.Empty;
        }

        var bytes = new List<byte>();
        await foreach (var frame in runtimeProvider.StartExecAsync(id, options, cancellationToken))
        {
            bytes.AddRange(EncodeRawFrame(frame.Stream.Equals("stderr", StringComparison.OrdinalIgnoreCase) ? 2 : 1, frame.Message.EndsWith('\n') ? frame.Message : frame.Message + "\n"));
        }

        return Results.File(bytes.ToArray(), "application/vnd.docker.raw-stream");
    }
    catch (KeyNotFoundException ex)
    {
        return DockerError(404, ex.Message);
    }
    catch (RuntimeCommandException ex)
    {
        return DockerError(MapCommandStatus(ex), ex.Message);
    }
}

static async Task<JsonDocument> ReadJsonBodyAsync(HttpContext context, CancellationToken cancellationToken)
{
    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    string text = await reader.ReadToEndAsync(cancellationToken);
    return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
}

static async Task WriteDockerExecManualUpgradeAsync(
    HttpContext context,
    IContainerRuntimeProvider runtimeProvider,
    string id,
    ExecStartOptionsDto options,
    CancellationToken cancellationToken)
{
    context.Response.StatusCode = StatusCodes.Status101SwitchingProtocols;
    context.Response.Headers.Connection = "Upgrade";
    context.Response.Headers.Upgrade = "tcp";
    context.Response.ContentType = "application/vnd.docker.raw-stream";
    await context.Response.StartAsync(cancellationToken);
    await WriteDockerExecRawStreamAsync(context.Response.Body, runtimeProvider, id, options, cancellationToken);
    await context.Response.CompleteAsync();
}

static bool IsDockerUpgradeRequest(HttpContext context)
{
    return context.Request.Headers.Connection.ToString().Contains("Upgrade", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(context.Request.Headers.Upgrade.ToString());
}

static async Task WriteDockerExecRawStreamAsync(
    Stream stream,
    IContainerRuntimeProvider runtimeProvider,
    string id,
    ExecStartOptionsDto options,
    CancellationToken cancellationToken)
{
    await foreach (var frame in runtimeProvider.StartExecAsync(id, options, cancellationToken))
    {
        byte[] bytes = EncodeRawFrame(
            frame.Stream.Equals("stderr", StringComparison.OrdinalIgnoreCase) ? 2 : 1,
            frame.Message.EndsWith('\n') ? frame.Message : frame.Message + "\n");
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}

static async Task<object> DockerExecInspect(IContainerRuntimeProvider runtimeProvider, string id, CancellationToken cancellationToken)
{
    var detail = await runtimeProvider.InspectExecAsync(id, cancellationToken);
    return new Dictionary<string, object?>
    {
        ["ID"] = detail.ExecId,
        ["Running"] = detail.Running,
        ["ExitCode"] = detail.ExitCode,
        ["ProcessConfig"] = new Dictionary<string, object?>(),
        ["OpenStdin"] = false,
        ["OpenStderr"] = true,
        ["OpenStdout"] = true,
        ["Tty"] = detail.Tty,
        ["CanRemove"] = false,
        ["ContainerID"] = ""
    };
}

static async Task<IResult> DockerExecResize(IContainerRuntimeProvider runtimeProvider, string id, CancellationToken cancellationToken)
{
    try
    {
        var detail = await runtimeProvider.InspectExecAsync(id, cancellationToken);
        return detail.Tty
            ? DockerError(501, "Interactive TTY resize is not supported by the active WSLC runtime provider.")
            : Results.NoContent();
    }
    catch (KeyNotFoundException ex)
    {
        return DockerError(404, ex.Message);
    }
    catch (RuntimeCommandException ex)
    {
        return DockerError(MapCommandStatus(ex), ex.Message);
    }
}

static async Task<object> DockerImages(IContainerRuntimeProvider runtimeProvider, CancellationToken cancellationToken)
{
    var images = await runtimeProvider.ListImagesAsync(cancellationToken);
    return images.Select(DockerImageSummary).ToArray();
}

static async Task<IResult> DockerImageCreate(IContainerRuntimeProvider runtimeProvider, HttpContext context, CancellationToken cancellationToken)
{
    try
    {
        string image = context.Request.Query["fromImage"].ToString();
        string tag = context.Request.Query["tag"].ToString();
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new DockerApiException(400, "fromImage is required");
        }

        string reference = string.IsNullOrWhiteSpace(tag) ? image : $"{image}:{tag}";
        return Results.Stream(async body =>
        {
            bool wroteFrame = false;
            await foreach (var item in runtimeProvider.PullImageProgressAsync(reference, cancellationToken))
            {
                wroteFrame = true;
                await WriteJsonLineAsync(body, DockerPullProgressMessage(item), cancellationToken);
            }

            if (!wroteFrame)
            {
                await WriteJsonLineAsync(
                    body,
                    new Dictionary<string, object?>
                    {
                        ["status"] = "Pull complete",
                        ["id"] = reference,
                        ["progressDetail"] = new Dictionary<string, object?>()
                    },
                    cancellationToken);
            }
        }, "application/json");
    }
    catch (DockerApiException ex)
    {
        return DockerError(ex.StatusCode, ex.Message);
    }
    catch (RuntimeCommandException ex)
    {
        return DockerError(MapCommandStatus(ex), ex.Message);
    }
    catch (ArgumentException ex)
    {
        return DockerError(400, ex.Message);
    }
}

static async Task NativeImagePullStreamAsync(
    IContainerRuntimeProvider runtimeProvider,
    OperationTracker operationTracker,
    ImagePullRequest request,
    HttpContext context,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(request.Reference))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(
            new NativeErrorResponse("request.invalid", "Image reference is required.", runtimeProvider.Name, string.Empty, null, string.Empty, string.Empty),
            cancellationToken);
        return;
    }

    context.Response.ContentType = "application/x-ndjson";
    await operationTracker.TrackAsync("image", request.Reference, "pull", async () =>
    {
        await foreach (var frame in runtimeProvider.PullImageProgressAsync(request.Reference, cancellationToken))
        {
            await WriteJsonLineAsync(context.Response.Body, frame, cancellationToken);
        }
    });
}

static async Task WriteJsonLineAsync(Stream body, object value, CancellationToken cancellationToken)
{
    string line = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n";
    byte[] bytes = Encoding.UTF8.GetBytes(line);
    await body.WriteAsync(bytes, cancellationToken);
    await body.FlushAsync(cancellationToken);
}

static Dictionary<string, object?> DockerPullProgressMessage(ImagePullProgressDto item)
{
    var message = new Dictionary<string, object?>
    {
        ["id"] = item.Id,
        ["status"] = item.Status,
        ["progressDetail"] = item.HasByteProgress
            ? new Dictionary<string, object?>
            {
                ["current"] = item.CurrentBytes,
                ["total"] = item.TotalBytes
            }
            : new Dictionary<string, object?>()
    };

    if (item.HasByteProgress)
    {
        message["progress"] = $"{item.CurrentBytes}/{item.TotalBytes}";
    }

    return message;
}

static IResult DockerImageSearchUnsupported()
{
    return DockerError(501, "Docker image search is not supported. Configure a registry/search provider before using /images/search.");
}

static Task<IResult> DockerImageGet(IContainerRuntimeProvider runtimeProvider, string name, CancellationToken cancellationToken)
{
    const string inspectSuffix = "/json";
    if (!name.EndsWith(inspectSuffix, StringComparison.OrdinalIgnoreCase))
    {
        return Task.FromResult(DockerError(404, $"No such image route: {name}"));
    }

    string imageName = name[..^inspectSuffix.Length];
    return DockerResult(() => DockerImageInspect(runtimeProvider, imageName, cancellationToken));
}

static async Task<object> DockerImageInspect(IContainerRuntimeProvider runtimeProvider, string name, CancellationToken cancellationToken)
{
    string decoded = Uri.UnescapeDataString(name);
    string normalized = NormalizeId(decoded);
    var images = await runtimeProvider.ListImagesAsync(cancellationToken);
    var image = images.FirstOrDefault(candidate =>
        candidate.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
        $"{candidate.Repository}:{candidate.Tag}".Equals(decoded, StringComparison.OrdinalIgnoreCase) ||
        candidate.Repository.Equals(decoded, StringComparison.OrdinalIgnoreCase));

    if (image is null)
    {
        throw new DockerApiException(404, $"No such image: {decoded}");
    }

    long size = ParseSize(image.Size);
    return new Dictionary<string, object?>
    {
        ["Id"] = "sha256:" + image.Id,
        ["RepoTags"] = new[] { $"{image.Repository}:{image.Tag}" },
        ["RepoDigests"] = Array.Empty<string>(),
        ["Parent"] = "",
        ["Comment"] = "",
        ["Created"] = image.Created,
        ["Container"] = "",
        ["ContainerConfig"] = new Dictionary<string, object?>(),
        ["DockerVersion"] = "",
        ["Author"] = "",
        ["Config"] = new Dictionary<string, object?> { ["Labels"] = new Dictionary<string, string>() },
        ["Architecture"] = WslcdDefaults.Architecture,
        ["Os"] = WslcdDefaults.OperatingSystem,
        ["Size"] = size,
        ["VirtualSize"] = size,
        ["GraphDriver"] = new Dictionary<string, object?> { ["Name"] = "wslc", ["Data"] = new Dictionary<string, string>() },
        ["RootFS"] = new Dictionary<string, object?> { ["Type"] = "layers", ["Layers"] = Array.Empty<string>() },
        ["Metadata"] = new Dictionary<string, object?>()
    };
}

static async Task<object> DockerImageRemove(IContainerRuntimeProvider runtimeProvider, string name, HttpContext context, CancellationToken cancellationToken)
{
    string decoded = Uri.UnescapeDataString(name);
    await runtimeProvider.RemoveImageAsync(decoded, cancellationToken);
    return new object[]
    {
        new Dictionary<string, object?>
        {
            ["Untagged"] = decoded
        }
    };
}

static async Task<object> DockerVolumes(IContainerRuntimeProvider runtimeProvider, CancellationToken cancellationToken)
{
    var volumes = await runtimeProvider.ListVolumesAsync(cancellationToken);
    return new Dictionary<string, object?>
    {
        ["Volumes"] = volumes.Select(DockerVolume).ToArray(),
        ["Warnings"] = Array.Empty<string>()
    };
}

static async Task<object> DockerVolumeCreate(IContainerRuntimeProvider runtimeProvider, JsonElement body, CancellationToken cancellationToken)
{
    string name = GetJsonString(body, "Name");
    if (string.IsNullOrWhiteSpace(name))
    {
        throw new DockerApiException(400, "volume name is required");
    }

    var volume = await runtimeProvider.CreateVolumeAsync(name, ReadLabels(body), cancellationToken);
    return DockerVolume(volume);
}

static async Task<object> DockerVolumeInspect(IContainerRuntimeProvider runtimeProvider, string name, CancellationToken cancellationToken)
{
    string decoded = Uri.UnescapeDataString(name);
    var volumes = await runtimeProvider.ListVolumesAsync(cancellationToken);
    var volume = volumes.FirstOrDefault(item => item.Name.Equals(decoded, StringComparison.OrdinalIgnoreCase));
    if (volume is null)
    {
        throw new DockerApiException(404, $"get {decoded}: no such volume");
    }

    return DockerVolume(volume);
}

static Dictionary<string, object?> DockerVolume(VolumeSummaryDto volume)
{
    return new Dictionary<string, object?>
    {
        ["Name"] = volume.Name,
        ["Driver"] = "local",
        ["Mountpoint"] = "",
        ["CreatedAt"] = DockerCreatedAt(volume.Created),
        ["Status"] = null,
        ["Labels"] = volume.Labels ?? new Dictionary<string, string>(),
        ["Scope"] = "local",
        ["Options"] = new Dictionary<string, string>(),
        ["UsageData"] = new Dictionary<string, object?>
        {
            ["Size"] = ParseSize(volume.Size),
            ["RefCount"] = volume.UsedBy == "-" ? 0 : 1
        }
    };
}

static string DockerCreatedAt(string value)
{
    if (string.IsNullOrWhiteSpace(value) || value == "-")
    {
        return "1970-01-01T00:00:00Z";
    }

    if (DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal,
            out DateTimeOffset parsed))
    {
        return parsed.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }

    return "1970-01-01T00:00:00Z";
}

static async Task<object> DockerNetworks(IContainerRuntimeProvider runtimeProvider, HttpContext context, CancellationToken cancellationToken)
{
    var filters = QueryFilters(context);
    var endpoints = await runtimeProvider.ListNetworksAsync(cancellationToken);
    var networks = new List<DockerPseudoNetwork> { DockerDefaultNetwork() };
    networks.AddRange(DockerNetworkRegistry.List());

    return networks
        .Where(network => MatchesNetworkFilters(network, filters))
        .Select(network => DockerNetwork(endpoints, network))
        .ToArray();
}

static async Task<object> DockerNetworkInspect(IContainerRuntimeProvider runtimeProvider, string id, CancellationToken cancellationToken)
{
    string decoded = Uri.UnescapeDataString(id);
    DockerPseudoNetwork network;
    if (IsDefaultDockerNetwork(decoded))
    {
        network = DockerDefaultNetwork();
    }
    else if (DockerNetworkRegistry.TryGet(decoded, out DockerPseudoNetwork? storedNetwork) && storedNetwork is not null)
    {
        network = storedNetwork;
    }
    else
    {
        throw new DockerApiException(404, $"network {decoded} not found");
    }

    var endpoints = await runtimeProvider.ListNetworksAsync(cancellationToken);
    return DockerNetwork(endpoints, network);
}

static IResult DockerNetworkCreate(JsonElement body)
{
    try
    {
        string name = GetJsonString(body, "Name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DockerApiException(400, "network name is required");
        }

        string driver = GetJsonString(body, "Driver");
        var network = DockerNetworkRegistry.Upsert(
            name,
            string.IsNullOrWhiteSpace(driver) ? "bridge" : driver,
            ReadLabels(body),
            ReadStringMap(body, "Options"));

        return Results.Json(new Dictionary<string, object?>
        {
            ["Id"] = network.Id,
            ["Warning"] = DockerNetworkCompatibilityWarning()
        }, statusCode: StatusCodes.Status201Created);
    }
    catch (DockerApiException ex)
    {
        return DockerError(ex.StatusCode, ex.Message);
    }
    catch (ArgumentException ex)
    {
        return DockerError(400, ex.Message);
    }
}

static IResult DockerNetworkConnect(string id)
{
    string decoded = Uri.UnescapeDataString(id);
    if (!IsDefaultDockerNetwork(decoded) && !DockerNetworkRegistry.TryGet(decoded, out _))
    {
        DockerNetworkRegistry.Upsert(decoded, "bridge", new Dictionary<string, string>(), new Dictionary<string, string>());
    }

    return Results.NoContent();
}

static IResult DockerNetworkDisconnect(string id)
{
    string decoded = Uri.UnescapeDataString(id);
    if (!IsDefaultDockerNetwork(decoded) && !DockerNetworkRegistry.TryGet(decoded, out _))
    {
        DockerNetworkRegistry.Upsert(decoded, "bridge", new Dictionary<string, string>(), new Dictionary<string, string>());
    }

    return Results.NoContent();
}

static IResult DockerNetworkDelete(string id)
{
    string decoded = Uri.UnescapeDataString(id);
    if (!IsDefaultDockerNetwork(decoded))
    {
        DockerNetworkRegistry.Remove(decoded);
    }

    return Results.NoContent();
}

static IResult DockerNetworkPrune(HttpContext context)
{
    try
    {
        var filters = QueryFilters(context);
        string[] deleted = DockerNetworkRegistry.Prune(network => MatchesNetworkFilters(network, filters));
        return Results.Json(new Dictionary<string, object?>
        {
            ["NetworksDeleted"] = deleted,
            ["SpaceReclaimed"] = 0,
            ["Warnings"] = new[] { DockerNetworkCompatibilityWarning() }
        });
    }
    catch (DockerApiException ex)
    {
        return DockerError(ex.StatusCode, ex.Message);
    }
}

static Dictionary<string, object?> DockerNetwork(IReadOnlyList<NetworkEndpointSummaryDto> endpoints, DockerPseudoNetwork network)
{
    var containers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    foreach (var endpoint in endpoints)
    {
        containers[endpoint.ContainerName] = new Dictionary<string, object?>
        {
            ["Name"] = endpoint.ContainerName,
            ["EndpointID"] = endpoint.ContainerName,
            ["MacAddress"] = "",
            ["IPv4Address"] = "",
            ["IPv6Address"] = ""
        };
    }

    return new Dictionary<string, object?>
    {
        ["Name"] = network.Name,
        ["Id"] = network.Id,
        ["Created"] = network.Created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
        ["Scope"] = "local",
        ["Driver"] = network.Driver,
        ["EnableIPv6"] = false,
        ["IPAM"] = new Dictionary<string, object?>
        {
            ["Driver"] = "default",
            ["Options"] = new Dictionary<string, string>(),
            ["Config"] = Array.Empty<object>()
        },
        ["Internal"] = false,
        ["Attachable"] = false,
        ["Ingress"] = false,
        ["Containers"] = containers,
        ["Options"] = network.Options,
        ["Labels"] = network.Labels
    };
}

static DockerPseudoNetwork DockerDefaultNetwork()
{
    return new DockerPseudoNetwork(
        "wslc",
        "wslc",
        "wslc",
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        DateTimeOffset.UnixEpoch);
}

static bool IsDefaultDockerNetwork(string id)
{
    return id.Equals("wslc", StringComparison.OrdinalIgnoreCase) ||
        id.Equals("default", StringComparison.OrdinalIgnoreCase) ||
        id.Equals("bridge", StringComparison.OrdinalIgnoreCase);
}

static bool MatchesNetworkFilters(DockerPseudoNetwork network, IReadOnlyDictionary<string, string[]> filters)
{
    foreach (var (name, values) in filters)
    {
        bool matched = name switch
        {
            "id" => values.Any(value => network.Id.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            "name" => values.Any(value => network.Name.Contains(value, StringComparison.OrdinalIgnoreCase)),
            "driver" => values.Any(value => network.Driver.Equals(value, StringComparison.OrdinalIgnoreCase)),
            "label" => values.All(value => MatchesLabelFilter(network.Labels, value)),
            "scope" => values.Any(value => "local".Equals(value, StringComparison.OrdinalIgnoreCase)),
            "type" => values.Any(value => value.Equals(IsDefaultDockerNetwork(network.Name) ? "builtin" : "custom", StringComparison.OrdinalIgnoreCase)),
            _ => true
        };

        if (!matched)
        {
            return false;
        }
    }

    return true;
}

static string DockerNetworkCompatibilityWarning()
{
    return "WSLC maps Docker-compatible networks to the default WSLC network; Docker bridge isolation, custom DNS, IPAM, and custom network drivers are not supported.";
}

static Dictionary<string, object?> DockerContainerSummary(ContainerSummaryDto container)
{
    return new Dictionary<string, object?>
    {
        ["Id"] = container.Id,
        ["Names"] = new[] { "/" + container.Name },
        ["Image"] = container.Image,
        ["ImageID"] = "",
        ["Command"] = container.Command,
        ["Created"] = 0,
        ["Ports"] = DockerPorts(container.PortSummary),
        ["Labels"] = ContainerLabels(container),
        ["State"] = DockerState(container.State),
        ["Status"] = container.Uptime,
        ["HostConfig"] = new Dictionary<string, object?> { ["NetworkMode"] = "default" },
        ["NetworkSettings"] = new Dictionary<string, object?> { ["Networks"] = new Dictionary<string, object?>() },
        ["Mounts"] = Array.Empty<object>()
    };
}

static Dictionary<string, object?> DockerImageSummary(ImageSummaryDto image)
{
    long size = ParseSize(image.Size);
    return new Dictionary<string, object?>
    {
        ["Containers"] = -1,
        ["Created"] = 0,
        ["Id"] = "sha256:" + image.Id,
        ["Labels"] = new Dictionary<string, string>(),
        ["ParentId"] = "",
        ["RepoDigests"] = Array.Empty<string>(),
        ["RepoTags"] = new[] { $"{image.Repository}:{image.Tag}" },
        ["SharedSize"] = -1,
        ["Size"] = size,
        ["VirtualSize"] = size
    };
}

static async Task<IResult> DockerNoContent(Func<Task> action)
{
    try
    {
        await action();
        return Results.NoContent();
    }
    catch (KeyNotFoundException ex)
    {
        return DockerError(404, ex.Message);
    }
    catch (RuntimeCommandException ex)
    {
        return DockerError(MapCommandStatus(ex), ex.Message);
    }
    catch (DirectoryNotFoundException ex)
    {
        return DockerError(400, ex.Message);
    }
    catch (ArgumentException ex)
    {
        return DockerError(400, ex.Message);
    }
}

static async Task<IResult> DockerResult(Func<Task<object>> action)
{
    try
    {
        return Results.Json(await action());
    }
    catch (DockerApiException ex)
    {
        return DockerError(ex.StatusCode, ex.Message);
    }
    catch (RuntimeCommandException ex)
    {
        return DockerError(MapCommandStatus(ex), ex.Message);
    }
    catch (ArgumentException ex)
    {
        return DockerError(400, ex.Message);
    }
}

static int MapCommandStatus(RuntimeCommandException ex)
{
    string message = ex.Message;
    if (message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("no such", StringComparison.OrdinalIgnoreCase))
    {
        return 404;
    }

    if (message.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("already", StringComparison.OrdinalIgnoreCase))
    {
        return 409;
    }

    return 500;
}

static IResult DockerError(int statusCode, string message)
{
    return Results.Json(new Dictionary<string, string> { ["message"] = message }, statusCode: statusCode);
}

static string GetJsonString(JsonElement root, string propertyName)
{
    if (!TryGetJsonProperty(root, propertyName, out JsonElement property))
    {
        return string.Empty;
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

static bool GetJsonBool(JsonElement root, string propertyName)
{
    return TryGetJsonProperty(root, propertyName, out JsonElement property) &&
        property.ValueKind == JsonValueKind.True;
}

static IReadOnlyList<string> ReadStringOrArray(JsonElement root, string propertyName)
{
    if (!TryGetJsonProperty(root, propertyName, out JsonElement property) ||
        property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
        return [];
    }

    if (property.ValueKind == JsonValueKind.String)
    {
        string? value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? [] : [value];
    }

    if (property.ValueKind == JsonValueKind.Array)
    {
        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    return [];
}

static Dictionary<string, string> ReadLabels(JsonElement root)
{
    JsonElement labels = default;
    if (TryGetJsonProperty(root, "Labels", out JsonElement directLabels))
    {
        labels = directLabels;
    }
    else if (TryGetJsonProperty(root, "Config", out JsonElement config) &&
        config.ValueKind == JsonValueKind.Object &&
        TryGetJsonProperty(config, "Labels", out JsonElement configLabels))
    {
        labels = configLabels;
    }

    if (labels.ValueKind != JsonValueKind.Object)
    {
        return [];
    }

    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var label in labels.EnumerateObject())
    {
        parsed[label.Name] = label.Value.ValueKind switch
        {
            JsonValueKind.String => label.Value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => label.Value.GetRawText(),
            _ => string.Empty
        };
    }

    return parsed;
}

static Dictionary<string, string> ReadStringMap(JsonElement root, string propertyName)
{
    if (!TryGetJsonProperty(root, propertyName, out JsonElement map) || map.ValueKind != JsonValueKind.Object)
    {
        return [];
    }

    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in map.EnumerateObject())
    {
        parsed[item.Name] = item.Value.ValueKind switch
        {
            JsonValueKind.String => item.Value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => item.Value.GetRawText(),
            _ => string.Empty
        };
    }

    return parsed;
}

static IReadOnlyList<ContainerMountDto> ReadBinds(JsonElement hostConfig)
{
    if (!TryGetJsonProperty(hostConfig, "Binds", out JsonElement binds) || binds.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    return binds.EnumerateArray()
        .Where(item => item.ValueKind == JsonValueKind.String)
        .Select(item => ParseBind(item.GetString() ?? string.Empty))
        .Where(mount => mount is not null)
        .Select(mount => mount!)
        .ToArray();
}

static ContainerMountDto? ParseBind(string bind)
{
    if (string.IsNullOrWhiteSpace(bind))
    {
        return null;
    }

    int delimiter = FindBindSourceDelimiter(bind);
    if (delimiter <= 0 || delimiter >= bind.Length - 1)
    {
        throw new ArgumentException($"Invalid bind mount syntax: {bind}");
    }

    string source = bind[..delimiter];
    string remainder = bind[(delimiter + 1)..];
    int modeDelimiter = remainder.IndexOf(':', StringComparison.Ordinal);
    string target = modeDelimiter < 0 ? remainder : remainder[..modeDelimiter];
    string mode = modeDelimiter < 0 ? string.Empty : remainder[(modeDelimiter + 1)..];

    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
    {
        throw new ArgumentException($"Invalid bind mount syntax: {bind}");
    }

    bool looksLikeWindowsPath = source.Length >= 3 && char.IsLetter(source[0]) && source[1] == ':' && (source[2] == '\\' || source[2] == '/');
    bool looksLikePath = looksLikeWindowsPath || source.StartsWith("\\", StringComparison.Ordinal) || source.StartsWith("/", StringComparison.Ordinal) || source.StartsWith(".", StringComparison.Ordinal);
    return new ContainerMountDto(source, target, mode.Contains("ro", StringComparison.OrdinalIgnoreCase), !looksLikePath);
}

static int FindBindSourceDelimiter(string bind)
{
    int start = bind.Length >= 2 && char.IsLetter(bind[0]) && bind[1] == ':' ? 2 : 0;
    return bind.IndexOf(':', start);
}

static IReadOnlyList<PortMappingDto> ReadPortBindings(JsonElement hostConfig)
{
    if (!TryGetJsonProperty(hostConfig, "PortBindings", out JsonElement bindings) || bindings.ValueKind != JsonValueKind.Object)
    {
        return [];
    }

    var ports = new List<PortMappingDto>();
    foreach (var binding in bindings.EnumerateObject())
    {
        var parts = binding.Name.Split('/', 2, StringSplitOptions.TrimEntries);
        if (!int.TryParse(parts[0], out int containerPort))
        {
            throw new ArgumentException($"Invalid container port binding: {binding.Name}");
        }

        string protocol = parts.Length == 2 && parts[1].Equals("udp", StringComparison.OrdinalIgnoreCase) ? "udp" : "tcp";
        if (binding.Value.ValueKind != JsonValueKind.Array)
        {
            continue;
        }

        foreach (var host in binding.Value.EnumerateArray())
        {
            string hostPortText = GetJsonString(host, "HostPort");
            if (string.IsNullOrWhiteSpace(hostPortText))
            {
                continue;
            }

            if (!int.TryParse(hostPortText, out int hostPort))
            {
                throw new ArgumentException($"Invalid host port binding: {hostPortText}");
            }

            ports.Add(new PortMappingDto(hostPort, containerPort, protocol));
        }
    }

    return ports;
}

static bool TryGetJsonProperty(JsonElement root, string propertyName, out JsonElement property)
{
    if (root.TryGetProperty(propertyName, out property))
    {
        return true;
    }

    foreach (var candidate in root.EnumerateObject())
    {
        if (candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
        {
            property = candidate.Value;
            return true;
        }
    }

    property = default;
    return false;
}

static bool QueryBool(HttpContext context, string name)
{
    string value = context.Request.Query[name].ToString();
    return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
}

static bool QueryBoolDefault(HttpContext context, string name, bool defaultValue)
{
    string value = context.Request.Query[name].ToString();
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
}

static int QueryInt(HttpContext context, string name)
{
    string value = context.Request.Query[name].ToString();
    return int.TryParse(value, out int parsed) ? parsed : 0;
}

static DateTimeOffset? QueryTimestamp(HttpContext context, string name)
{
    string value = context.Request.Query[name].ToString();
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long unixSeconds))
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
    }

    if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double fractionalUnixSeconds))
    {
        return DateTimeOffset.UnixEpoch.AddSeconds(fractionalUnixSeconds);
    }

    return DateTimeOffset.TryParse(
        value,
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
        out DateTimeOffset parsed)
        ? parsed
        : null;
}

static Dictionary<string, string[]> QueryFilters(HttpContext context)
{
    string filters = context.Request.Query["filters"].ToString();
    if (string.IsNullOrWhiteSpace(filters))
    {
        return [];
    }

    try
    {
        using JsonDocument document = JsonDocument.Parse(filters);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new DockerApiException(400, "invalid filters");
        }

        var parsed = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var filter in document.RootElement.EnumerateObject())
        {
            parsed[filter.Name] = ReadFilterValues(filter.Value);
        }

        return parsed;
    }
    catch (JsonException)
    {
        throw new DockerApiException(400, "invalid filters");
    }
}

static string[] ReadFilterValues(JsonElement value)
{
    return value.ValueKind switch
    {
        JsonValueKind.Array => value.EnumerateArray()
            .Select(FilterValueToString)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray(),
        JsonValueKind.Object => value.EnumerateObject()
            .Where(item => item.Value.ValueKind != JsonValueKind.False)
            .Select(item => item.Name)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray(),
        JsonValueKind.String => [value.GetString() ?? string.Empty],
        JsonValueKind.True => ["true"],
        JsonValueKind.False => ["false"],
        _ => []
    };
}

static string FilterValueToString(JsonElement value)
{
    return value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => string.Empty
    };
}

static bool MatchesContainerFilters(ContainerSummaryDto container, IReadOnlyDictionary<string, string[]> filters)
{
    foreach (var (name, values) in filters)
    {
        bool matched = name switch
        {
            "status" => values.Any(value => DockerState(container.State).Equals(value, StringComparison.OrdinalIgnoreCase)),
            "name" => values.Any(value => container.Name.Contains(value.TrimStart('/'), StringComparison.OrdinalIgnoreCase)),
            "id" => values.Any(value => container.Id.StartsWith(value, StringComparison.OrdinalIgnoreCase)),
            "ancestor" => values.Any(value => container.Image.Equals(value, StringComparison.OrdinalIgnoreCase)),
            "label" => values.All(value => MatchesLabelFilter(ContainerLabels(container), value)),
            _ => true
        };

        if (!matched)
        {
            return false;
        }
    }

    return true;
}

static bool MatchesLabelFilter(IReadOnlyDictionary<string, string> labels, string filter)
{
    int separator = filter.IndexOf('=', StringComparison.Ordinal);
    if (separator > 0)
    {
        string key = filter[..separator];
        string expected = filter[(separator + 1)..];
        return labels.TryGetValue(key, out string? actual) &&
            string.Equals(actual, expected, StringComparison.Ordinal);
    }

    return labels.ContainsKey(filter);
}

static IReadOnlyDictionary<string, string> ContainerLabels(ContainerSummaryDto container)
{
    if (container.Labels is { Count: > 0 })
    {
        return container.Labels;
    }

    if (string.IsNullOrWhiteSpace(container.InspectJson))
    {
        return new Dictionary<string, string>();
    }

    try
    {
        using JsonDocument document = JsonDocument.Parse(container.InspectJson);
        return ReadLabels(document.RootElement);
    }
    catch (JsonException)
    {
        return new Dictionary<string, string>();
    }
}

static object[] DockerPorts(string portSummary)
{
    return ParsePorts(portSummary)
        .Select(port => new Dictionary<string, object?>
        {
            ["IP"] = "0.0.0.0",
            ["PrivatePort"] = port.ContainerPort,
            ["PublicPort"] = port.HostPort,
            ["Type"] = port.Protocol
        })
        .ToArray<object>();
}

static Dictionary<string, object?> DockerPortMap(string portSummary)
{
    var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    foreach (var port in ParsePorts(portSummary))
    {
        map[$"{port.ContainerPort}/{port.Protocol}"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["HostIp"] = "0.0.0.0",
                ["HostPort"] = port.HostPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
    }

    return map;
}

static IReadOnlyList<(int HostPort, int ContainerPort, string Protocol)> ParsePorts(string portSummary)
{
    if (string.IsNullOrWhiteSpace(portSummary) || portSummary == "-")
    {
        return [];
    }

    var ports = new List<(int HostPort, int ContainerPort, string Protocol)>();
    foreach (string entry in portSummary.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var match = Regex.Match(entry, @"(?<host>\d+)\s*(?:->|:)\s*(?<container>\d+)(?:/(?<protocol>\w+))?", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            continue;
        }

        int hostPort = int.Parse(match.Groups["host"].Value, System.Globalization.CultureInfo.InvariantCulture);
        int containerPort = int.Parse(match.Groups["container"].Value, System.Globalization.CultureInfo.InvariantCulture);
        string protocol = match.Groups["protocol"].Success ? match.Groups["protocol"].Value : "tcp";
        ports.Add((hostPort, containerPort, protocol));
    }

    return ports;
}

static string DockerState(ContainerRuntimeState state)
{
    return state switch
    {
        ContainerRuntimeState.Created => "created",
        ContainerRuntimeState.Running => "running",
        ContainerRuntimeState.Stopped => "exited",
        ContainerRuntimeState.Exited => "exited",
        _ => "unknown"
    };
}

static string NormalizeId(string id)
{
    string normalized = id.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? id[7..] : id;
    return normalized.Length <= 12 ? normalized : normalized[..12];
}

static byte[] EncodeRawFrame(int streamType, string message)
{
    byte[] payload = Encoding.UTF8.GetBytes(message);
    byte[] frame = new byte[payload.Length + 8];
    frame[0] = (byte)streamType;
    frame[4] = (byte)((payload.Length >> 24) & 0xff);
    frame[5] = (byte)((payload.Length >> 16) & 0xff);
    frame[6] = (byte)((payload.Length >> 8) & 0xff);
    frame[7] = (byte)(payload.Length & 0xff);
    Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);
    return frame;
}

static long ParseMemoryBytes(string memoryUsed)
{
    if (string.IsNullOrWhiteSpace(memoryUsed))
    {
        return 0;
    }

    string used = memoryUsed.Split('/', 2, StringSplitOptions.TrimEntries)[0];
    return ParseSize(used);
}

static long ParseSize(string size)
{
    if (string.IsNullOrWhiteSpace(size) || size == "-")
    {
        return 0;
    }

    var match = Regex.Match(size.Trim(), @"^(?<value>\d+(?:\.\d+)?)\s*(?<unit>B|KB|MB|GB|TB)?$", RegexOptions.IgnoreCase);
    if (!match.Success)
    {
        return 0;
    }

    double value = double.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
    string unit = match.Groups["unit"].Success ? match.Groups["unit"].Value.ToUpperInvariant() : "B";
    double multiplier = unit switch
    {
        "KB" => 1_000,
        "MB" => 1_000_000,
        "GB" => 1_000_000_000,
        "TB" => 1_000_000_000_000,
        _ => 1
    };

    return (long)(value * multiplier);
}

internal sealed record DockerPseudoNetwork(
    string Name,
    string Id,
    string Driver,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Options,
    DateTimeOffset Created);

internal static class DockerNetworkRegistry
{
    private static readonly ConcurrentDictionary<string, DockerPseudoNetwork> Networks = new(StringComparer.OrdinalIgnoreCase);

    public static DockerPseudoNetwork Upsert(
        string name,
        string driver,
        IReadOnlyDictionary<string, string> labels,
        IReadOnlyDictionary<string, string> options)
    {
        string normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("network name is required", nameof(name));
        }

        string normalizedDriver = string.IsNullOrWhiteSpace(driver) ? "bridge" : driver.Trim();
        var snapshot = new DockerPseudoNetwork(
            normalizedName,
            normalizedName,
            normalizedDriver,
            new Dictionary<string, string>(labels, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);

        return Networks.AddOrUpdate(normalizedName, snapshot, (_, existing) => snapshot with { Created = existing.Created });
    }

    public static bool TryGet(string id, out DockerPseudoNetwork? network)
    {
        string normalized = id.Trim();
        return Networks.TryGetValue(normalized, out network);
    }

    public static IReadOnlyList<DockerPseudoNetwork> List()
    {
        return Networks.Values
            .OrderBy(network => network.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void Remove(string id)
    {
        Networks.TryRemove(id.Trim(), out _);
    }

    public static string[] Prune(Func<DockerPseudoNetwork, bool> predicate)
    {
        var deleted = new List<string>();
        foreach (var network in Networks.Values.Where(predicate).ToArray())
        {
            if (Networks.TryRemove(network.Name, out _))
            {
                deleted.Add(network.Name);
            }
        }

        return deleted.ToArray();
    }
}

internal sealed class DockerApiException : Exception
{
    public DockerApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

internal sealed class DaemonLog
{
    private readonly string _path;
    private readonly object _gate = new();

    public DaemonLog(string path)
    {
        _path = path;
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            File.AppendAllText(_path, $"{DateTimeOffset.UtcNow:o} {message}{Environment.NewLine}");
        }
    }
}
