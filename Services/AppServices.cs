using WslcDesktop.DaemonClient;

namespace wslc_desktop.Services;

public static class AppServices
{
    private static readonly MockWslcControlPlane ControlPlane = new();
    private static readonly IWslcdNativeClient NativeDaemonClient = WslcdNativeClient.CreateDefault();
    private static readonly IWslcdNativeClient MockDaemonClient = new MockWslcdNativeClient();
    private static readonly FileAppSettingsService RealSettings = new();
    private static readonly CliToolPathResolver RealCliToolPaths = new();
    private static readonly ProcessCommandProbe RealCommandProbe = new(RealCliToolPaths);
    private static readonly DaemonProcessManager Daemon = new(NativeDaemonClient, RealSettings);
    private static readonly DaemonWslcHostService RealHost = new(NativeDaemonClient, Daemon);
    private static readonly DaemonWslcImageService RealImages = new(NativeDaemonClient, Daemon);
    private static readonly DaemonWslcContainerService RealContainers = new(NativeDaemonClient, Daemon);
    private static readonly DaemonWslcProcessService RealProcesses = new(NativeDaemonClient, Daemon);
    private static readonly WslcCliTerminalService RealTerminals = new(RealSettings);
    private static readonly DaemonWslcVolumeService RealVolumes = new(NativeDaemonClient, Daemon);
    private static readonly DaemonWslcNetworkService RealNetworks = new(NativeDaemonClient, Daemon);
    private static readonly DaemonOperationTracker RealOperations = new(NativeDaemonClient, Daemon);
    private static readonly DaemonRuntimeProviderPreviewService RealProviderPreview = new(NativeDaemonClient, Daemon);
    private static readonly StartupTaskService RealStartupTask = new();
    private static readonly EnvironmentBootstrapService RealBootstrap = new(RealCommandProbe, RealCliToolPaths);
    private static readonly CliToolInstallationService RealCliTools = new(RealCliToolPaths);
    private static readonly DockerContextService RealDockerContext = new(RealCommandProbe);
    private static readonly DaemonDiagnosticsService RealDaemonDiagnostics = new(NativeDaemonClient, Daemon, RealSettings);
    private static readonly DaemonControlService RealDaemonControl = new(Daemon);
    private static readonly ComposePlanService RealComposePlans = new(RealImages, RealContainers, RealOperations);
    private static readonly AppDiagnosticsService RealDiagnostics = new(RealHost, RealSettings, RealOperations, RealComposePlans, RealVolumes, RealNetworks, RealDaemonDiagnostics);

    public static IWslcHostService Host => RealHost;

    public static IMockWslcScenarioService MockScenario => ControlPlane;

    public static IWslcdNativeClient MockDaemon => MockDaemonClient;

    public static IWslcImageService Images => RealImages;

    public static IWslcContainerService Containers => RealContainers;

    public static IWslcProcessService Processes => RealProcesses;

    public static IWslcTerminalService Terminals => RealTerminals;

    public static IWslcVolumeService Volumes => RealVolumes;

    public static IWslcNetworkService Networks => RealNetworks;

    public static IComposePlanService ComposePlans => RealComposePlans;

    public static IWslcCommandFallback CommandFallback => ControlPlane;

    public static IAppSettingsService Settings => RealSettings;

    public static FileAppSettingsService StartupSettings => RealSettings;

    public static IRuntimeProviderPreviewService ProviderPreview => RealProviderPreview;

    public static IStartupTaskService StartupTask => RealStartupTask;

    public static IEnvironmentBootstrapService Bootstrap => RealBootstrap;

    public static ICliToolInstallationService CliTools => RealCliTools;

    public static IDockerContextService DockerContext => RealDockerContext;

    public static IDaemonDiagnosticsService DaemonDiagnostics => RealDaemonDiagnostics;

    public static IDaemonControlService DaemonControl => RealDaemonControl;

    public static IStringLocalizer Strings { get; private set; } = new AppStringLocalizer(AppLanguage.English);

    public static void InitializeLocalization(string language)
    {
        Strings = new AppStringLocalizer(language);
    }

    public static IAppDiagnosticsService Diagnostics => RealDiagnostics;

    public static IOperationTracker Operations => RealOperations;
}
