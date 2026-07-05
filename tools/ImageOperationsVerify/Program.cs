using WslcDesktop.Contracts;
using WslcDesktop.Runtime.Providers.DockerApi;
using wslc_desktop.Models;
using wslc_desktop.Services;
using wslc_desktop.ViewModels;

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'");
    }
}

AssertEqual(
    "library/nginx:alpine",
    WslcdRouteValues.DecodePathValue("library%2Fnginx:alpine"),
    "decode scoped repository");

AssertEqual(
    "c0fdff8467adaa93173ddcaed93e0204.d.1ms.run/debian:latest",
    WslcdRouteValues.DecodePathValue("c0fdff8467adaa93173ddcaed93e0204.d.1ms.run%2Fdebian:latest"),
    "decode registry repository");

AssertEqual(
    "ghcr.io/acme/app:1.2.3",
    WslcdRouteValues.DecodePathValue("ghcr.io%2Facme%2Fapp:1.2.3"),
    "decode registry and namespace");

var statusFrame = ImagePullProgressDto.StatusFrame("busybox:latest", "Pulling");
AssertEqual(WslcDesktop.Contracts.ImagePullProgressKind.Status, statusFrame.Kind, "status frame kind");
AssertEqual(false, statusFrame.HasByteProgress, "status frame byte progress");
AssertEqual(0UL, statusFrame.TotalBytes, "status frame total");

var progressFrame = ImagePullProgressDto.ProgressFrame("layer-1", "Downloading", 25, 100);
AssertEqual(WslcDesktop.Contracts.ImagePullProgressKind.Progress, progressFrame.Kind, "progress frame kind");
AssertEqual(true, progressFrame.HasByteProgress, "progress frame byte progress");
AssertEqual(25UL, progressFrame.CurrentBytes, "progress frame current");
AssertEqual(100UL, progressFrame.TotalBytes, "progress frame total");

var dockerFrames = DockerImagePullProgressParser.Parse("""
{"status":"Pulling from library/busybox","id":"latest"}
{"status":"Downloading","progressDetail":{"current":512,"total":1024},"id":"abc123"}
{"status":"Extracting","progressDetail":{"current":1024,"total":1024},"id":"abc123"}
{"status":"Pull complete","id":"abc123"}
""");
AssertEqual(4, dockerFrames.Count, "docker frame count");
AssertEqual(WslcDesktop.Contracts.ImagePullProgressKind.Status, dockerFrames[0].Kind, "docker status kind");
AssertEqual(WslcDesktop.Contracts.ImagePullProgressKind.Progress, dockerFrames[1].Kind, "docker download progress kind");
AssertEqual(true, dockerFrames[1].HasByteProgress, "docker download has progress");
AssertEqual(512UL, dockerFrames[1].CurrentBytes, "docker download current");
AssertEqual(1024UL, dockerFrames[1].TotalBytes, "docker download total");
AssertEqual(WslcDesktop.Contracts.ImagePullProgressKind.Status, dockerFrames[3].Kind, "docker complete status kind");

var imageSuggestions = ContainerImageSuggestionProvider.BuildReferences(
[
    new ImageSummary("sha256:a", "ubuntu", "latest", "100 MB", "today", false),
    new ImageSummary("sha256:b", "nginx", "alpine", "62 MB", "today", false),
    new ImageSummary("sha256:c", "ghcr.io/acme/app", "1.0", "12 MB", "today", false),
    new ImageSummary("sha256:d", "nginx", "alpine", "62 MB", "today", false),
    new ImageSummary("sha256:e", "<none>", "<none>", "1 MB", "today", false)
]);
AssertSequenceEqual(["ghcr.io/acme/app:1.0", "nginx:alpine", "ubuntu:latest"], imageSuggestions, "image suggestion references");
AssertSequenceEqual(["ghcr.io/acme/app:1.0"], ContainerImageSuggestionProvider.Filter(imageSuggestions, "acme"), "image suggestion contains filter");
AssertSequenceEqual(["nginx:alpine"], ContainerImageSuggestionProvider.Filter(imageSuggestions, "ng alp"), "image suggestion token filter");
AssertSequenceEqual(imageSuggestions, ContainerImageSuggestionProvider.Filter(imageSuggestions, ""), "image suggestion empty filter");

var startedAt = new DateTimeOffset(2026, 7, 5, 8, 30, 0, TimeSpan.Zero);
var pullTask = new ImagePullTaskViewModel("ghcr.io/acme/app:1", startedAt, "Queued");
AssertEqual(ImagePullTaskState.Queued, pullTask.State, "initial state");
AssertEqual(true, pullTask.IsActive, "queued is active");
AssertEqual(true, pullTask.IsIndeterminate, "queued is indeterminate");

pullTask.UpdateProgress("Pulling", "layer: 25/100 bytes", 25, 100);
AssertEqual(ImagePullTaskState.Pulling, pullTask.State, "pulling state");
AssertEqual(false, pullTask.IsIndeterminate, "known total is determinate");
AssertEqual(25d, pullTask.ProgressValue, "progress percent");

pullTask.UpdateProgress("Pulling", "layer: downloading", 0, 0);
AssertEqual(true, pullTask.IsIndeterminate, "unknown total is indeterminate");

pullTask.MarkSucceeded("Completed");
AssertEqual(ImagePullTaskState.Succeeded, pullTask.State, "succeeded state");
AssertEqual(false, pullTask.IsActive, "succeeded is inactive");
AssertEqual(100d, pullTask.ProgressValue, "succeeded percent");

var failedTask = new ImagePullTaskViewModel("bad/image:latest", startedAt, "Queued");
failedTask.MarkFailed("invalid reference");
AssertEqual(ImagePullTaskState.Failed, failedTask.State, "failed state");
AssertEqual(true, failedTask.IsFailed, "failed flag");
AssertEqual("invalid reference", failedTask.DetailText, "failure detail");

var healthyStatus = ShellStatusViewModel.Present(new DaemonDiagnosticsSnapshot(
    "native pipe reachable",
    "wslc-cli",
    "matrix",
    [],
    "ready"));
AssertEqual(ShellStatusState.Ready, healthyStatus.State, "healthy shell status state");
AssertEqual("Daemon OK", healthyStatus.DaemonText, "healthy daemon text");
AssertEqual("wslc-cli", healthyStatus.BackendText, "healthy backend text");
AssertEqual("ready", healthyStatus.Message, "healthy message");

var offlineStatus = ShellStatusViewModel.Present(new DaemonDiagnosticsSnapshot(
    "native pipe unavailable",
    "-",
    "matrix",
    ["wslcd-desktop.log"],
    "pipe missing"));
AssertEqual(ShellStatusState.Offline, offlineStatus.State, "offline shell status state");
AssertEqual("Daemon offline", offlineStatus.DaemonText, "offline daemon text");
AssertEqual("Backend unknown", offlineStatus.BackendText, "offline backend text");
AssertEqual("pipe missing", offlineStatus.Message, "offline message");

var offlineActions = DaemonActionAvailability.FromStatus(ShellStatusState.Offline, isBusy: false);
AssertEqual(true, offlineActions.CanStart, "offline can start daemon");
AssertEqual(false, offlineActions.CanRestart, "offline cannot restart daemon");
AssertEqual(false, offlineActions.CanStop, "offline cannot stop daemon");

var readyActions = DaemonActionAvailability.FromStatus(ShellStatusState.Ready, isBusy: false);
AssertEqual(false, readyActions.CanStart, "ready cannot start daemon");
AssertEqual(true, readyActions.CanRestart, "ready can restart daemon");
AssertEqual(true, readyActions.CanStop, "ready can stop daemon");

var warningActions = DaemonActionAvailability.FromStatus(ShellStatusState.Warning, isBusy: false);
AssertEqual(false, warningActions.CanStart, "warning cannot start daemon");
AssertEqual(true, warningActions.CanRestart, "warning can restart daemon");
AssertEqual(true, warningActions.CanStop, "warning can stop daemon");

var busyActions = DaemonActionAvailability.FromStatus(ShellStatusState.Ready, isBusy: true);
AssertEqual(false, busyActions.CanStart, "busy cannot start daemon");
AssertEqual(false, busyActions.CanRestart, "busy cannot restart daemon");
AssertEqual(false, busyActions.CanStop, "busy cannot stop daemon");

var previousSettings = new AppSettingsSnapshot(
    "C:\\Data",
    4,
    4096,
    "/bin/sh",
    true,
    "en-US",
    wslc_desktop.Models.RuntimeProviderSelection.WslcCli,
    "",
    false,
    false,
    "",
    "",
    "",
    "",
    false,
    false,
    "");

AssertEqual(false, SettingsRestartImpact.RequiresDaemonRestart(
    previousSettings,
    previousSettings with { Language = "zh-CN" }),
    "language change does not require daemon restart");

AssertEqual(false, SettingsRestartImpact.RequiresDaemonRestart(
    previousSettings,
    previousSettings with { PreferExternalTerminal = false }),
    "terminal preference does not require daemon restart");

AssertEqual(true, SettingsRestartImpact.RequiresDaemonRestart(
    previousSettings,
    previousSettings with { RuntimeProvider = wslc_desktop.Models.RuntimeProviderSelection.DockerApi }),
    "runtime provider requires daemon restart");

AssertEqual(true, SettingsRestartImpact.RequiresDaemonRestart(
    previousSettings,
    previousSettings with { WslcHttpProxy = "http://127.0.0.1:7890" }),
    "wslc proxy requires daemon restart");

AssertEqual(true, SettingsRestartImpact.RequiresDaemonRestart(
    previousSettings,
    previousSettings with { WslcEnvironment = "FOO=bar" }),
    "wslc environment requires daemon restart");

Console.WriteLine("IMAGE_OPERATIONS_VERIFY_OK");

static void AssertSequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string name)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException($"{name}: expected '{string.Join(", ", expected)}', got '{string.Join(", ", actual)}'");
    }
}
