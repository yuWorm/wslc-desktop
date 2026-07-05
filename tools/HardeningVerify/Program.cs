using System.Text.Json;
using System.Text.RegularExpressions;
using wslc_desktop.Models;
using wslc_desktop.Services;

Expect(AppLanguage.ResolveSystemLanguage(["zh-Hans-CN", "en-US"]) == AppLanguage.Chinese, "Chinese system language should select zh-CN.");
Expect(AppLanguage.ResolveSystemLanguage(["fr-FR"]) == AppLanguage.English, "Unsupported system language should fall back to en-US.");
Expect(AppLanguage.GetEffectiveLanguage(AppLanguage.System, ["en-US"]) == AppLanguage.English, "System language setting should resolve from Windows languages.");
Expect(AppLanguage.NormalizeSetting("de-DE") == AppLanguage.System, "Unsupported persisted language should normalize to system.");

var localizer = new AppStringLocalizer(AppLanguage.Chinese);
Expect(localizer.Get("SettingsRestartRequired") == "设置已保存。请重启应用或 wslcd-desktop 以应用运行时和语言更改。", "Chinese restart prompt should be localized.");
Expect(localizer.Get("MissingKey") == "MissingKey", "Unknown localized key should return the key.");

string containersXaml = await File.ReadAllTextAsync(Path.Combine("Pages", "ContainersPage.xaml"));
Expect(!Regex.IsMatch(containersXaml, "<AppBarButton[^>]*\\sIcon=\"[^\"]+\""),
    "Containers command bar should use explicit IconElement children instead of AppBarButton Icon string shorthand.");
Expect(!containersXaml.Contains("NewContainerName", StringComparison.Ordinal),
    "Containers page should not keep the create form inline in the main list surface.");
Expect(containersXaml.Contains("AutomationProperties.AutomationId=\"TxtContainerSearch\"", StringComparison.Ordinal),
    "Containers page should expose a search input with AutomationId TxtContainerSearch.");
Expect(containersXaml.Contains("AutomationProperties.AutomationId=\"ContainerStatusFilter\"", StringComparison.Ordinal),
    "Containers page should expose a status filter.");
Expect(containersXaml.Contains("AutomationProperties.AutomationId=\"ContainersDetailSelector\"", StringComparison.Ordinal),
    "Containers page should expose detail tabs.");
foreach (string tab in new[] { "ContainersOverviewSelector", "ContainersStatsSelector", "ContainersLogsSelector", "ContainersTerminalSelector", "ContainersInspectSelector" })
{
    Expect(containersXaml.Contains($"x:Uid=\"{tab}\"", StringComparison.Ordinal),
        $"Containers page should include {tab}.");
}

string imagesXaml = await File.ReadAllTextAsync(Path.Combine("Pages", "ImagesPage.xaml"));
Expect(imagesXaml.Contains("AutomationProperties.AutomationId=\"TxtImagesSearch\"", StringComparison.Ordinal),
    "Images page should expose a Dory-style search input.");
Expect(imagesXaml.Contains("AutomationProperties.AutomationId=\"LstImages\"", StringComparison.Ordinal),
    "Images page should expose its image table list.");
Expect(!imagesXaml.Contains("ImagesReferenceInput", StringComparison.Ordinal),
    "Images page should move pull image inputs into a dialog instead of the main surface.");

string volumesXaml = await File.ReadAllTextAsync(Path.Combine("Pages", "VolumesPage.xaml"));
Expect(volumesXaml.Contains("AutomationProperties.AutomationId=\"TxtVolumesSearch\"", StringComparison.Ordinal),
    "Volumes page should expose a Dory-style search input.");
Expect(volumesXaml.Contains("AutomationProperties.AutomationId=\"LstVolumes\"", StringComparison.Ordinal),
    "Volumes page should expose its volume table list.");
Expect(!volumesXaml.Contains("TxtVolumesNewName", StringComparison.Ordinal),
    "Volumes page should move new volume inputs into a dialog instead of the main surface.");
Expect(File.ReadAllText(Path.Combine("Pages", "VolumesPage.xaml.cs")).Contains("ShowVolumeDetail_Click", StringComparison.Ordinal),
    "Volumes page should provide a detail dialog entry point.");

string networksXaml = await File.ReadAllTextAsync(Path.Combine("Pages", "NetworksPage.xaml"));
Expect(networksXaml.Contains("AutomationProperties.AutomationId=\"TxtNetworksSearch\"", StringComparison.Ordinal),
    "Networks page should expose a Dory-style search input.");
Expect(networksXaml.Contains("AutomationProperties.AutomationId=\"LstNetworkEndpoints\"", StringComparison.Ordinal),
    "Networks page should expose its endpoint table list.");
Expect(File.ReadAllText(Path.Combine("Pages", "NetworksPage.xaml.cs")).Contains("ShowEndpointDetail_Click", StringComparison.Ordinal),
    "Networks page should provide an endpoint detail dialog entry point.");

string composeXaml = await File.ReadAllTextAsync(Path.Combine("Pages", "ComposePage.xaml"));
Expect(composeXaml.Contains("AutomationProperties.AutomationId=\"TxtComposeSearch\"", StringComparison.Ordinal),
    "Compose page should expose a Dory-style search input.");
Expect(composeXaml.Contains("AutomationProperties.AutomationId=\"LstComposeServicePlans\"", StringComparison.Ordinal),
    "Compose page should retain its service plan table list.");

var ports = ContainerCreateInputParser.ParsePortMappings("8080:80, 8443:443/tcp, 5353:53/udp");
Expect(ports.Count == 3, "Expected three parsed port mappings.");
Expect(ports[2] == new PortMapping(5353, 53, "udp"), "UDP protocol should be parsed.");
ExpectThrows<ArgumentException>(() => ContainerCreateInputParser.ParsePortMappings("8080:80, 8080:81"), "Duplicate host ports should be rejected.");
ExpectThrows<ArgumentException>(() => ContainerCreateInputParser.ParsePortMappings("70000:80"), "Out-of-range host ports should be rejected.");

var mounts = ContainerCreateInputParser.ParseMounts(@"C:\data=>/data:ro, cache=>/cache");
Expect(mounts.Count == 2, "Expected two parsed mounts.");
Expect(!mounts[0].IsNamedVolume && mounts[0].IsReadOnly, "Windows bind mount should preserve read-only mode.");
Expect(mounts[1].IsNamedVolume, "Plain source names should be treated as named volumes.");

var environment = ContainerCreateInputParser.ParseEnvironment("LOG_LEVEL=info, DEBUG=true");
Expect(environment.Count == 2, "Expected two parsed environment variables.");
Expect(environment["LOG_LEVEL"] == "info", "Environment parser should preserve values.");
Expect(environment["DEBUG"] == "true", "Environment parser should parse second value.");
ExpectThrows<ArgumentException>(() => ContainerCreateInputParser.ParseEnvironment("LOG_LEVEL=info, LOG_LEVEL=debug"), "Duplicate environment keys should be rejected.");

var cliImages = WslcCliOutputParser.ParseImages("""
REPOSITORY    TAG      IMAGE ID       CREATED        SIZE
hello-world   latest   e2ac70e7319a   3 months ago   0.01 MB
""");
Expect(cliImages.Count == 1, "CLI image parser should read one image row.");
Expect(cliImages[0].Repository == "hello-world", "CLI image parser should read repository.");
Expect(cliImages[0].Tag == "latest", "CLI image parser should read tag.");
Expect(cliImages[0].Id == "e2ac70e7319a", "CLI image parser should read image id.");
Expect(cliImages[0].Created == "3 months ago", "CLI image parser should preserve multi-word created value.");
Expect(cliImages[0].Size == "0.01 MB", "CLI image parser should preserve multi-token size.");

var emptyCliContainers = WslcCliOutputParser.ParseContainers("""
容器 ID   名称   映像   已创建   状态   端口
""");
Expect(emptyCliContainers.Count == 0, "CLI container parser should accept an empty table.");

var cliContainers = WslcCliOutputParser.ParseContainers("""
容器 ID        名称          映像                 已创建          状态        端口
abc123def456   demo-web      nginx:latest         3 minutes ago   Running     8080->80/tcp
""");
Expect(cliContainers.Count == 1, "CLI container parser should read one container row.");
Expect(cliContainers[0].Id == "abc123def456", "CLI container parser should read id.");
Expect(cliContainers[0].Name == "demo-web", "CLI container parser should read name.");
Expect(cliContainers[0].Image == "nginx:latest", "CLI container parser should read image.");
Expect(cliContainers[0].State == ContainerRuntimeState.Running, "CLI container parser should map running state.");
Expect(cliContainers[0].PortSummary == "8080->80/tcp", "CLI container parser should preserve ports.");

string root = Path.Combine(Path.GetTempPath(), "wslc-hardening-verify", Guid.NewGuid().ToString("N"));
var fileSettings = new FileAppSettingsService(Path.Combine(root, "Settings"));
await fileSettings.SaveAsync(new AppSettingsSnapshot(Path.Combine(root, "Data"), 2, 1024, "/bin/bash", true, AppLanguage.Chinese));
var loadedSettings = await fileSettings.LoadAsync();
Expect(loadedSettings.Language == AppLanguage.Chinese, "Persisted language setting should round-trip.");
Expect(loadedSettings.DefaultShell == "/bin/bash", "Persisted shell setting should round-trip.");

var settings = new FakeSettingsService(root);
var diagnostics = new AppDiagnosticsService(
    new FakeHostService(),
    settings,
    new FakeOperationTracker(),
    new FakeComposePlanService(),
    new FakeVolumeService(),
    new FakeNetworkService());

string reportPath = await diagnostics.ExportAsync();
Expect(File.Exists(reportPath), "Diagnostics report file should exist.");
Expect(Path.GetDirectoryName(reportPath) == Path.Combine(root, "Diagnostics"), "Diagnostics report should be written under DataRoot\\Diagnostics.");

using var document = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
var rootElement = document.RootElement;
Expect(rootElement.TryGetProperty("generatedAt", out _), "Diagnostics should include generatedAt.");
Expect(rootElement.GetProperty("engine").GetProperty("state").GetString() == "Ready", "Diagnostics should include engine state.");
Expect(rootElement.GetProperty("settings").GetProperty("dataRoot").GetString() == root, "Diagnostics should include settings.");
Expect(rootElement.GetProperty("operations").GetArrayLength() == 1, "Diagnostics should include operations.");
Expect(rootElement.GetProperty("composeProjects").GetArrayLength() == 1, "Diagnostics should include compose projects.");
Expect(rootElement.GetProperty("volumes").GetArrayLength() == 1, "Diagnostics should include volumes.");
Expect(rootElement.GetProperty("publishedPorts").GetArrayLength() == 1, "Diagnostics should include published ports.");

Console.WriteLine("PHASE8_HARDENING_OK");
return 0;

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void ExpectThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

sealed class FakeHostService : IWslcHostService
{
    public Task<EngineSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EngineSummary(
            EngineReadinessState.Ready,
            "verify",
            "ready",
            1,
            2,
            "3 GB",
            4.5,
            "512 MB"));
    }

    public Task<IReadOnlyList<string>> GetMissingComponentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }
}

sealed class FakeSettingsService : IAppSettingsService
{
    private readonly AppSettingsSnapshot _settings;

    public FakeSettingsService(string dataRoot)
    {
        _settings = new AppSettingsSnapshot(dataRoot, 2, 1024, "/bin/sh", false, AppLanguage.System);
    }

    public Task<AppSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings);
    }

    public Task SaveAsync(AppSettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

sealed class FakeOperationTracker : IOperationTracker
{
    public Task<IReadOnlyList<OperationRecord>> ListRecentAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OperationRecord> operations =
        [
            new("op1", "Verify", OperationState.Succeeded, "ok", DateTimeOffset.Parse("2026-07-04T00:00:00Z"))
        ];

        return Task.FromResult(operations);
    }

    public void Track(OperationRecord operation)
    {
    }
}

sealed class FakeComposePlanService : IComposePlanService
{
    public Task<IReadOnlyList<ComposeProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ComposeProjectSummary> projects =
        [
            new("verify-stack", 2, 2, @"C:\compose.yaml")
        ];

        return Task.FromResult(projects);
    }

    public Task<IReadOnlyList<ComposeServicePlan>> PreviewAsync(string composePath, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<ContainerSummary>> CreateAndStartAsync(string composePath, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

sealed class FakeVolumeService : IWslcVolumeService
{
    public Task<IReadOnlyList<VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VolumeSummary> volumes =
        [
            new("verify-volume", "-", "verify", "now", false)
        ];

        return Task.FromResult(volumes);
    }

    public Task CreateNamedVolumeAsync(VolumeCreateRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task DeleteNamedVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

sealed class FakeNetworkService : IWslcNetworkService
{
    public Task<IReadOnlyList<NetworkEndpointSummary>> ListPublishedPortsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NetworkEndpointSummary> ports =
        [
            new("verify", 8080, 80, "tcp", "http://localhost:8080")
        ];

        return Task.FromResult(ports);
    }
}
