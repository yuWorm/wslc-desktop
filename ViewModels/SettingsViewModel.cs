using System.Diagnostics;
using wslc_desktop.Models;
using wslc_desktop.Services;

namespace wslc_desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private readonly IAppDiagnosticsService _diagnosticsService;
    private readonly IRuntimeProviderPreviewService _providerPreviewService;
    private readonly IStartupTaskService _startupTaskService;
    private readonly IDaemonControlService _daemonControlService;
    private readonly IDaemonDiagnosticsService _daemonDiagnosticsService;
    private readonly IEnvironmentBootstrapService _bootstrapService;
    private readonly ICliToolInstallationService _cliToolInstallationService;
    private readonly IDockerContextService _dockerContextService;
    private AppSettingsSnapshot? _loadedSettings;
    private string _dataRoot = string.Empty;
    private double _cpuCount;
    private double _memoryMB;
    private string _defaultShell = "/bin/sh";
    private string _language = AppLanguage.System;
    private string _runtimeProvider = RuntimeProviderSelection.WslcCli;
    private string _dockerApiHost = string.Empty;
    private string _wslcHttpProxy = string.Empty;
    private string _wslcHttpsProxy = string.Empty;
    private string _wslcNoProxy = string.Empty;
    private string _wslcImageMirror = string.Empty;
    private string _wslcEnvironment = string.Empty;
    private bool _preferExternalTerminal;
    private bool _allowTcpDockerApi;
    private bool _launchAtLogin;
    private bool _wslcRewriteImageTag;
    private bool _wslcRemoveRewrittenSourceTag;
    private bool _isBusy;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _message = AppServices.Strings.Get("SettingsStored");
    private string _providerPreview = AppServices.Strings.Get("ProviderPreviewNotTested");
    private string _startupTaskStatus = AppServices.Strings.Get("StartupTaskStatusUnknown");
    private string _cliToolsStatus = AppServices.Strings.Get("CliToolsStatusUnknown");
    private string _cliToolsBinDirectory = string.Empty;
    private ShellStatusState _daemonStatusState = ShellStatusState.Checking;
    private string _daemonStatusText = ShellStatusLabels.English.Checking;
    private bool _canStartDaemon;
    private bool _canRestartDaemon;
    private bool _canStopDaemon;
    private bool _lastSaveRequiresDaemonRestart;

    public SettingsViewModel(
        IAppSettingsService settingsService,
        IAppDiagnosticsService diagnosticsService,
        IRuntimeProviderPreviewService providerPreviewService,
        IStartupTaskService startupTaskService,
        IDaemonControlService daemonControlService,
        IDaemonDiagnosticsService daemonDiagnosticsService,
        IEnvironmentBootstrapService bootstrapService,
        ICliToolInstallationService cliToolInstallationService,
        IDockerContextService dockerContextService)
    {
        _settingsService = settingsService;
        _diagnosticsService = diagnosticsService;
        _providerPreviewService = providerPreviewService;
        _startupTaskService = startupTaskService;
        _daemonControlService = daemonControlService;
        _daemonDiagnosticsService = daemonDiagnosticsService;
        _bootstrapService = bootstrapService;
        _cliToolInstallationService = cliToolInstallationService;
        _dockerContextService = dockerContextService;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        TestProviderCommand = new AsyncRelayCommand(TestProviderAsync);
        StartDaemonCommand = new AsyncRelayCommand(StartDaemonAsync, () => CanStartDaemon);
        RestartDaemonCommand = new AsyncRelayCommand(RestartDaemonAsync, () => CanRestartDaemon);
        StopDaemonCommand = new AsyncRelayCommand(StopDaemonAsync, () => CanStopDaemon);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync);
        OpenDockerConfigCommand = new AsyncRelayCommand(OpenDockerConfigAsync);
        RefreshCliToolsCommand = new AsyncRelayCommand(RefreshCliToolsStatusAsync, () => !IsBusy);
        InstallLatestDockerCliCommand = new AsyncRelayCommand(InstallLatestDockerCliAsync, () => !IsBusy);
        InstallLatestComposeCommand = new AsyncRelayCommand(InstallLatestComposeAsync, () => !IsBusy);
        AddCliToolsPathCommand = new AsyncRelayCommand(AddCliToolsPathAsync, () => !IsBusy);
        AddCliToolsSystemPathCommand = new AsyncRelayCommand(AddCliToolsSystemPathAsync, () => !IsBusy);
        CreateDefaultDockerContextCommand = new AsyncRelayCommand(CreateDefaultDockerContextAsync, () => !IsBusy);
    }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand TestProviderCommand { get; }

    public AsyncRelayCommand StartDaemonCommand { get; }

    public AsyncRelayCommand RestartDaemonCommand { get; }

    public AsyncRelayCommand StopDaemonCommand { get; }

    public AsyncRelayCommand ExportDiagnosticsCommand { get; }

    public AsyncRelayCommand OpenDockerConfigCommand { get; }

    public AsyncRelayCommand RefreshCliToolsCommand { get; }

    public AsyncRelayCommand InstallLatestDockerCliCommand { get; }

    public AsyncRelayCommand InstallLatestComposeCommand { get; }

    public AsyncRelayCommand AddCliToolsPathCommand { get; }

    public AsyncRelayCommand AddCliToolsSystemPathCommand { get; }

    public AsyncRelayCommand CreateDefaultDockerContextCommand { get; }

    public IReadOnlyList<string> LanguageOptions => AppLanguage.SupportedSettings;

    public string DataRoot
    {
        get => _dataRoot;
        set => SetProperty(ref _dataRoot, value);
    }

    public double CpuCount
    {
        get => _cpuCount;
        set => SetProperty(ref _cpuCount, value);
    }

    public double MemoryMB
    {
        get => _memoryMB;
        set => SetProperty(ref _memoryMB, value);
    }

    public string DefaultShell
    {
        get => _defaultShell;
        set => SetProperty(ref _defaultShell, value);
    }

    public string Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, AppLanguage.NormalizeSetting(value)))
            {
                OnPropertyChanged(nameof(LanguageDisplayText));
            }
        }
    }

    public string LanguageDisplayText => AppLanguage.GetDisplayName(Language);

    public string RuntimeProvider
    {
        get => _runtimeProvider;
        set
        {
            if (SetProperty(ref _runtimeProvider, RuntimeProviderSelection.Normalize(value)))
            {
                OnPropertyChanged(nameof(IsDockerApiProvider));
                OnPropertyChanged(nameof(IsWslcCliProvider));
                OnPropertyChanged(nameof(CanRemoveRewrittenSourceTag));
                if (IsDockerApiProvider)
                {
                    WslcRemoveRewrittenSourceTag = false;
                }
            }
        }
    }

    public bool IsDockerApiProvider => RuntimeProvider.Equals(RuntimeProviderSelection.DockerApi, StringComparison.OrdinalIgnoreCase);

    public bool IsWslcCliProvider => !IsDockerApiProvider;

    public string DockerApiHost
    {
        get => _dockerApiHost;
        set => SetProperty(ref _dockerApiHost, value);
    }

    public bool AllowTcpDockerApi
    {
        get => _allowTcpDockerApi;
        set => SetProperty(ref _allowTcpDockerApi, value);
    }

    public string WslcHttpProxy
    {
        get => _wslcHttpProxy;
        set => SetProperty(ref _wslcHttpProxy, value);
    }

    public string WslcHttpsProxy
    {
        get => _wslcHttpsProxy;
        set => SetProperty(ref _wslcHttpsProxy, value);
    }

    public string WslcNoProxy
    {
        get => _wslcNoProxy;
        set => SetProperty(ref _wslcNoProxy, value);
    }

    public string WslcImageMirror
    {
        get => _wslcImageMirror;
        set => SetProperty(ref _wslcImageMirror, value);
    }

    public bool WslcRewriteImageTag
    {
        get => _wslcRewriteImageTag;
        set
        {
            if (SetProperty(ref _wslcRewriteImageTag, value))
            {
                OnPropertyChanged(nameof(CanRemoveRewrittenSourceTag));
                if (!value)
                {
                    WslcRemoveRewrittenSourceTag = false;
                }
            }
        }
    }

    public bool WslcRemoveRewrittenSourceTag
    {
        get => _wslcRemoveRewrittenSourceTag;
        set => SetProperty(ref _wslcRemoveRewrittenSourceTag, value && CanRemoveRewrittenSourceTag);
    }

    public bool CanRemoveRewrittenSourceTag => IsWslcCliProvider && WslcRewriteImageTag;

    public string WslcEnvironment
    {
        get => _wslcEnvironment;
        set => SetProperty(ref _wslcEnvironment, value);
    }

    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set => SetProperty(ref _launchAtLogin, value);
    }

    public bool PreferExternalTerminal
    {
        get => _preferExternalTerminal;
        set => SetProperty(ref _preferExternalTerminal, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSaveSettings));
                RefreshDaemonActionAvailability();
                SaveCommand.RaiseCanExecuteChanged();
                TestProviderCommand.RaiseCanExecuteChanged();
                ExportDiagnosticsCommand.RaiseCanExecuteChanged();
                OpenDockerConfigCommand.RaiseCanExecuteChanged();
                RefreshCliToolsCommand.RaiseCanExecuteChanged();
                InstallLatestDockerCliCommand.RaiseCanExecuteChanged();
                InstallLatestComposeCommand.RaiseCanExecuteChanged();
                AddCliToolsPathCommand.RaiseCanExecuteChanged();
                AddCliToolsSystemPathCommand.RaiseCanExecuteChanged();
                CreateDefaultDockerContextCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanSaveSettings => !IsBusy;

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string ProviderPreview
    {
        get => _providerPreview;
        private set => SetProperty(ref _providerPreview, value);
    }

    public string StartupTaskStatus
    {
        get => _startupTaskStatus;
        private set => SetProperty(ref _startupTaskStatus, value);
    }

    public string CliToolsStatus
    {
        get => _cliToolsStatus;
        private set => SetProperty(ref _cliToolsStatus, value);
    }

    public string CliToolsBinDirectory
    {
        get => _cliToolsBinDirectory;
        private set => SetProperty(ref _cliToolsBinDirectory, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public ShellStatusState DaemonStatusState
    {
        get => _daemonStatusState;
        private set
        {
            if (SetProperty(ref _daemonStatusState, value))
            {
                RefreshDaemonActionAvailability();
            }
        }
    }

    public string DaemonStatusText
    {
        get => _daemonStatusText;
        private set => SetProperty(ref _daemonStatusText, value);
    }

    public bool CanStartDaemon
    {
        get => _canStartDaemon;
        private set => SetProperty(ref _canStartDaemon, value);
    }

    public bool CanRestartDaemon
    {
        get => _canRestartDaemon;
        private set => SetProperty(ref _canRestartDaemon, value);
    }

    public bool CanStopDaemon
    {
        get => _canStopDaemon;
        private set => SetProperty(ref _canStopDaemon, value);
    }

    public bool LastSaveRequiresDaemonRestart
    {
        get => _lastSaveRequiresDaemonRestart;
        private set => SetProperty(ref _lastSaveRequiresDaemonRestart, value);
    }

    public async Task LoadAsync()
    {
        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            var settings = await _settingsService.LoadAsync();
            DataRoot = settings.DataRoot;
            CpuCount = settings.CpuCount;
            MemoryMB = settings.MemoryMB;
            DefaultShell = settings.DefaultShell;
            PreferExternalTerminal = settings.PreferExternalTerminal;
            Language = settings.Language;
            RuntimeProvider = settings.RuntimeProvider;
            DockerApiHost = settings.DockerApiHost;
            AllowTcpDockerApi = settings.AllowTcpDockerApi;
            LaunchAtLogin = settings.LaunchAtLogin;
            WslcHttpProxy = settings.WslcHttpProxy;
            WslcHttpsProxy = settings.WslcHttpsProxy;
            WslcNoProxy = settings.WslcNoProxy;
            WslcImageMirror = settings.WslcImageMirror;
            WslcRewriteImageTag = settings.WslcRewriteImageTag;
            WslcRemoveRewrittenSourceTag = settings.WslcRemoveRewrittenSourceTag;
            WslcEnvironment = settings.WslcEnvironment;
            _loadedSettings = settings;
            LastSaveRequiresDaemonRestart = false;
            StartupTaskSnapshot startupTask = await _startupTaskService.GetStatusAsync();
            StartupTaskStatus = startupTask.Message;
            if (startupTask.Availability == StartupTaskAvailability.Available)
            {
                LaunchAtLogin = startupTask.IsEnabled;
            }

            await RefreshDaemonStatusAsync();
            await RefreshCliToolsStatusAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            AppSettingsSnapshot nextSettings = CreateSnapshot();
            bool requiresDaemonRestart = _loadedSettings is not null
                && SettingsRestartImpact.RequiresDaemonRestart(_loadedSettings, nextSettings);

            await _settingsService.SaveAsync(nextSettings);
            _loadedSettings = nextSettings;
            LastSaveRequiresDaemonRestart = requiresDaemonRestart;

            StartupTaskSnapshot startupTask = await _startupTaskService.SetEnabledAsync(LaunchAtLogin);
            StartupTaskStatus = startupTask.Message;
            Message = startupTask.Availability == StartupTaskAvailability.Unavailable
                ? AppServices.Strings.Format("SettingsSavedStartupUnavailable", startupTask.Message)
                : requiresDaemonRestart
                    ? AppServices.Strings.Get("SettingsSavedDaemonRestartRecommended")
                    : AppServices.Strings.Get("SettingsRestartRequired");
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
    }

    private AppSettingsSnapshot CreateSnapshot()
    {
        return new AppSettingsSnapshot(
                DataRoot,
                Math.Max(1, (int)CpuCount),
                Math.Max(512, (int)MemoryMB),
                string.IsNullOrWhiteSpace(DefaultShell) ? "/bin/sh" : DefaultShell,
                PreferExternalTerminal,
                AppLanguage.NormalizeSetting(Language),
                RuntimeProviderSelection.Normalize(RuntimeProvider),
                DockerApiHost.Trim(),
                AllowTcpDockerApi,
                LaunchAtLogin,
                WslcHttpProxy.Trim(),
                WslcHttpsProxy.Trim(),
                WslcNoProxy.Trim(),
                WslcImageMirror.Trim().TrimEnd('/'),
                WslcRewriteImageTag,
                WslcRewriteImageTag && WslcRemoveRewrittenSourceTag,
                WslcEnvironment.Replace("\r\n", "\n", StringComparison.Ordinal).Trim());
    }

    private async Task TestProviderAsync()
    {
        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            var preview = await _providerPreviewService.PreviewAsync(
                RuntimeProvider,
                DockerApiHost,
                AllowTcpDockerApi);

            ProviderPreview = preview.IsAvailable
                ? AppServices.Strings.Format("ProviderPreviewAvailable", preview.RuntimeProvider, preview.CapabilitySummary)
                : AppServices.Strings.Format("ProviderPreviewUnavailable", preview.RuntimeProvider, preview.Message);
            Message = ProviderPreview;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestartDaemonAsync()
    {
        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            await _daemonControlService.RestartAsync();
            Message = AppServices.Strings.Get("DaemonRestarted");
            await RefreshDaemonStatusAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StartDaemonAsync()
    {
        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            await _daemonControlService.StartAsync();
            Message = AppServices.Strings.Get("DaemonStarted");
            await RefreshDaemonStatusAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StopDaemonAsync()
    {
        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            DaemonStopResult result = await _daemonControlService.StopAllAsync();
            Message = AppServices.Strings.Get("DaemonStopped") + FormatStopWarnings(result);
            await RefreshDaemonStatusAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            string path = await _diagnosticsService.ExportAsync();
            Message = AppServices.Strings.Format("DiagnosticsExported", path);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task OpenDockerConfigAsync()
    {
        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            string dockerConfigDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".docker");
            Directory.CreateDirectory(dockerConfigDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = dockerConfigDirectory,
                UseShellExecute = true
            });
            Message = AppServices.Strings.Get("DockerConfigOpened");
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }

        return Task.CompletedTask;
    }

    public async Task RefreshCliToolsStatusAsync()
    {
        await RunCliToolActionAsync(async () =>
        {
            WslcPrerequisiteStatus wslc = await _bootstrapService.CheckWslcAsync();
            DockerCliStatus docker = await _bootstrapService.CheckDockerCliAsync();
            CliToolsBinDirectory = _cliToolInstallationService.BinDirectory;
            CliToolsStatus = FormatCliToolsStatus(wslc, docker);
        }, updateMessage: false);
    }

    public async Task InstallDockerCliFromZipAsync(string zipPath)
    {
        await RunCliToolActionAsync(async () =>
        {
            CliToolInstallResult result = await _cliToolInstallationService.InstallDockerCliFromZipAsync(zipPath);
            _bootstrapService.AddToolBinToProcessPath();
            Message = result.Message;
            await RefreshCliToolsStatusCoreAsync();
        });
    }

    public async Task InstallComposeFromExeAsync(string exePath)
    {
        await RunCliToolActionAsync(async () =>
        {
            CliToolInstallResult result = await _cliToolInstallationService.InstallComposeFromExeAsync(exePath);
            _bootstrapService.AddToolBinToProcessPath();
            Message = result.Message;
            await RefreshCliToolsStatusCoreAsync();
        });
    }

    private async Task InstallLatestDockerCliAsync()
    {
        await RunCliToolActionAsync(async () =>
        {
            CliToolInstallResult result = await _cliToolInstallationService.InstallLatestDockerCliAsync();
            _bootstrapService.AddToolBinToProcessPath();
            Message = result.Message;
            await RefreshCliToolsStatusCoreAsync();
        });
    }

    private async Task InstallLatestComposeAsync()
    {
        await RunCliToolActionAsync(async () =>
        {
            CliToolInstallResult result = await _cliToolInstallationService.InstallLatestComposeAsync();
            _bootstrapService.AddToolBinToProcessPath();
            Message = result.Message;
            await RefreshCliToolsStatusCoreAsync();
        });
    }

    private Task AddCliToolsPathAsync()
    {
        return RunCliToolActionAsync(async () =>
        {
            string bin = _cliToolInstallationService.AddBinToUserPath();
            Message = AppServices.Strings.Format("CliToolsPathAdded", bin);
            await RefreshCliToolsStatusCoreAsync();
        });
    }

    private Task AddCliToolsSystemPathAsync()
    {
        return RunCliToolActionAsync(async () =>
        {
            string bin = await _cliToolInstallationService.AddBinToMachinePathAsync();
            Message = AppServices.Strings.Format("CliToolsSystemPathAdded", bin);
            await RefreshCliToolsStatusCoreAsync();
        });
    }

    public Task CreateDefaultDockerContextAsync()
    {
        return RunCliToolActionAsync(async () =>
        {
            DockerContextResult result = await _dockerContextService.CreateAndUseWslcDesktopContextAsync();
            Message = AppServices.Strings.Format("DockerContextDefaultCreated", result.ContextName, result.DockerHost);
            await RefreshCliToolsStatusCoreAsync();
        });
    }

    private static string FormatStopWarnings(DaemonStopResult result)
    {
        if (result.Warnings.Count == 0)
        {
            return string.Empty;
        }

        return AppServices.Strings.Format("DaemonStopWarning", string.Join(" ", result.Warnings));
    }

    private async Task RefreshDaemonStatusAsync()
    {
        try
        {
            DaemonDiagnosticsSnapshot snapshot = await _daemonDiagnosticsService.CaptureAsync(startIfNeeded: false);
            ShellStatusPresentation presentation = ShellStatusViewModel.Present(snapshot, CreateShellStatusLabels());
            DaemonStatusState = presentation.State;
            DaemonStatusText = presentation.SummaryText;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or HttpRequestException or IOException)
        {
            ShellStatusPresentation presentation = ShellStatusViewModel.Present(new DaemonDiagnosticsSnapshot(
                "native pipe unavailable",
                "-",
                string.Empty,
                [],
                ex.Message), CreateShellStatusLabels());
            DaemonStatusState = presentation.State;
            DaemonStatusText = presentation.SummaryText;
        }
    }

    private async Task RunCliToolActionAsync(Func<Task> action, bool updateMessage = true)
    {
        IsBusy = true;

        try
        {
            HasError = false;
            ErrorMessage = string.Empty;
            await action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            if (updateMessage)
            {
                Message = AppServices.Strings.Format("CliToolsOperationFailed", ex.Message);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCliToolsStatusCoreAsync()
    {
        WslcPrerequisiteStatus wslc = await _bootstrapService.CheckWslcAsync();
        DockerCliStatus docker = await _bootstrapService.CheckDockerCliAsync();
        CliToolsBinDirectory = _cliToolInstallationService.BinDirectory;
        CliToolsStatus = FormatCliToolsStatus(wslc, docker);
    }

    private static string FormatCliToolsStatus(WslcPrerequisiteStatus wslc, DockerCliStatus docker)
    {
        string wslcText = wslc.IsReady
            ? AppServices.Strings.Get("CliToolsWslcReady")
            : AppServices.Strings.Format("CliToolsWslcMissing", wslc.RequiredCommand);

        string dockerText = docker.DockerCliAvailable
            ? AppServices.Strings.Format("CliToolsDockerReady", docker.DockerCliPath)
            : AppServices.Strings.Get("CliToolsDockerMissing");

        string composeText = docker.ComposeAvailable
            ? AppServices.Strings.Format("CliToolsComposeReady", docker.ComposePath)
            : AppServices.Strings.Get("CliToolsComposeMissing");

        return string.Join(Environment.NewLine, wslcText, dockerText, composeText);
    }

    private void RefreshDaemonActionAvailability()
    {
        DaemonActionAvailability availability = DaemonActionAvailability.FromStatus(DaemonStatusState, IsBusy);
        CanStartDaemon = availability.CanStart;
        CanRestartDaemon = availability.CanRestart;
        CanStopDaemon = availability.CanStop;
        StartDaemonCommand.RaiseCanExecuteChanged();
        RestartDaemonCommand.RaiseCanExecuteChanged();
        StopDaemonCommand.RaiseCanExecuteChanged();
    }

    private static ShellStatusLabels CreateShellStatusLabels()
    {
        return new ShellStatusLabels(
            AppServices.Strings.Get("ShellStatusChecking"),
            AppServices.Strings.Get("ShellStatusDaemonOk"),
            AppServices.Strings.Get("ShellStatusDaemonOffline"),
            AppServices.Strings.Get("ShellStatusDaemonWarning"),
            AppServices.Strings.Get("ShellStatusRuntimeOk"),
            AppServices.Strings.Get("ShellStatusRuntimeIssue"),
            AppServices.Strings.Get("ShellStatusBackendUnknown"));
    }
}
