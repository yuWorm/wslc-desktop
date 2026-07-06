using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Globalization;
using Windows.ApplicationModel.DataTransfer;
using wslc_desktop.Models;
using wslc_desktop.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace wslc_desktop;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public static Window? MainWindow { get; private set; }
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        AppLaunchLogger.InstallGlobalHandlers();
        AppLaunchLogger.Info($"App constructor started. BaseDirectory={AppContext.BaseDirectory}; ProcessPath={Environment.ProcessPath}");

        try
        {
            UnhandledException += App_UnhandledException;
            AppLaunchLogger.Info("Loading startup settings.");
            var settings = AppServices.StartupSettings.LoadForStartup();
            string language = AppLanguage.GetEffectiveLanguage(settings.Language, ApplicationLanguages.Languages);
            TrySetPrimaryLanguageOverride(language);
            AppServices.InitializeLocalization(language);
            AppLaunchLogger.Info($"Initializing XAML. Language={language}");
            InitializeComponent();
            AppLaunchLogger.Info("App constructor completed.");
        }
        catch (Exception ex)
        {
            AppLaunchLogger.Error("App constructor failed.", ex);
            throw;
        }
    }

    private static void TrySetPrimaryLanguageOverride(string language)
    {
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch (InvalidOperationException ex)
        {
            AppLaunchLogger.Info($"Skipping Windows language override in the current app model: {ex.Message}");
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            AppLaunchLogger.Info("OnLaunched started.");
            _window = new MainWindow();
            MainWindow = _window;
            AppLaunchLogger.Info("Activating main window.");
            _window.Activate();
            AppLaunchLogger.Info("Main window activated.");
            _ = RunStartupBootstrapAsync();
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    private static async Task RunStartupBootstrapAsync()
    {
        if (MainWindow is not wslc_desktop.MainWindow mainWindow)
        {
            return;
        }

        try
        {
            AppLaunchLogger.Info("Startup bootstrap started.");
            AppServices.Bootstrap.AddToolBinToProcessPath();

            AppSettingsSnapshot startupSettings = AppServices.StartupSettings.LoadForStartup();
            AppLaunchLogger.Info(startupSettings.WslcPrerequisiteInitialized
                ? "WSLC prerequisite was previously initialized; running silent startup check."
                : "WSLC prerequisite is not initialized; waiting for first-run check before entering the shell.");

            bool wslcReady = await EnsureWslcPrerequisiteAsync(mainWindow);
            if (!wslcReady)
            {
                AppLaunchLogger.Info("Startup bootstrap stopped because WSLC is not ready.");
                return;
            }

            await MarkWslcPrerequisiteInitializedAsync();
            mainWindow.EnterApplicationShell();
            await MaybeShowDockerCliDialogAsync(mainWindow);
            await StartDaemonOnLaunchAsync();
            await mainWindow.StartShellStatusPollingAsync();
            AppLaunchLogger.Info("Startup bootstrap completed.");
        }
        catch (Exception ex)
        {
            AppLaunchLogger.Error("Startup bootstrap failed.", ex);
            if (MainWindow is wslc_desktop.MainWindow failedWindow)
            {
                failedWindow.ShellStatus.ShowOfflineError(ex.Message);
            }
        }
    }

    private static async Task<bool> EnsureWslcPrerequisiteAsync(wslc_desktop.MainWindow mainWindow)
    {
        while (true)
        {
            mainWindow.ShowStartupOverlay();
            WslcPrerequisiteStatus status = await CheckWslcWithStartupTimeoutAsync();
            if (status.IsReady)
            {
                return true;
            }

            ContentDialogResult result = await ShowWslcRequiredDialogAsync(mainWindow, status);
            if (result == ContentDialogResult.Primary)
            {
                continue;
            }

            if (result == ContentDialogResult.Secondary)
            {
                CopyTextToClipboard(status.RequiredCommand);
                continue;
            }

            AppLaunchLogger.Info("WSLC prerequisite dialog was dismissed; showing it again because WSLC is required.");
        }
    }

    private static Task<WslcPrerequisiteStatus> CheckWslcWithStartupTimeoutAsync()
    {
        return AppServices.Bootstrap.CheckWslcAsync();
    }

    private static async Task MarkWslcPrerequisiteInitializedAsync()
    {
        try
        {
            AppSettingsSnapshot settings = AppServices.StartupSettings.LoadForStartup();
            if (settings.WslcPrerequisiteInitialized)
            {
                return;
            }

            await AppServices.StartupSettings.SaveAsync(settings with { WslcPrerequisiteInitialized = true });
            AppLaunchLogger.Info("WSLC prerequisite initialization marker saved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLaunchLogger.Error("Failed to save WSLC prerequisite initialization marker.", ex);
        }
    }

    private static async Task MaybeShowDockerCliDialogAsync(wslc_desktop.MainWindow mainWindow)
    {
        DockerCliStatus status = await AppServices.Bootstrap.CheckDockerCliAsync();
        if (status.DockerCliAvailable)
        {
            return;
        }

        if (mainWindow.Content is FrameworkElement root)
        {
            await Pages.CliToolsInstallDialog.ShowAsync(root, AppServices.Bootstrap, AppServices.CliTools);
        }
    }

    private static async Task<ContentDialogResult> ShowWslcRequiredDialogAsync(
        wslc_desktop.MainWindow mainWindow,
        WslcPrerequisiteStatus status)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = GetXamlRoot(mainWindow),
            Style = Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = AppServices.Strings.Get("WslcRequiredDialogTitle"),
            Content = CreateDialogContent(GetWslcPrerequisiteMessage(status), status.RequiredCommand),
            PrimaryButtonText = AppServices.Strings.Get("Recheck"),
            SecondaryButtonText = AppServices.Strings.Get("CopyCommand"),
            DefaultButton = ContentDialogButton.Primary
        };

        return await dialog.ShowAsync();
    }

    private static string GetWslcPrerequisiteMessage(WslcPrerequisiteStatus status)
    {
        return status.State switch
        {
            WslcPrerequisiteState.MissingWsl => AppServices.Strings.Get("WslcRequiredMissingWslMessage"),
            WslcPrerequisiteState.WslUpdateRequired when !string.IsNullOrWhiteSpace(status.DetectedVersion) =>
                AppServices.Strings.Format("WslcRequiredUpdateRequiredWithVersion", status.DetectedVersion),
            WslcPrerequisiteState.WslUpdateRequired => AppServices.Strings.Get("WslcRequiredUpdateRequiredMessage"),
            WslcPrerequisiteState.CheckTimedOut => AppServices.Strings.Get("WslcRequiredTimedOutMessage"),
            _ => status.Message
        };
    }

    private static StackPanel CreateDialogContent(string message, string command)
    {
        var panel = new StackPanel
        {
            Spacing = 12
        };

        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.WrapWholeWords
        });

        if (!string.IsNullOrWhiteSpace(command))
        {
            panel.Children.Add(new TextBlock
            {
                Text = AppServices.Strings.Get("WslcRequiredCommandHeader"),
                TextWrapping = TextWrapping.WrapWholeWords
            });

            panel.Children.Add(new TextBox
            {
                Text = command,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                MinHeight = 72,
                MaxHeight = 160,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono")
            });
        }

        return panel;
    }

    private static XamlRoot? GetXamlRoot(Window window)
    {
        return (window.Content as FrameworkElement)?.XamlRoot;
    }

    private static void CopyTextToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static async Task StartDaemonOnLaunchAsync()
    {
        wslc_desktop.MainWindow? mainWindow = MainWindow as wslc_desktop.MainWindow;

        try
        {
            mainWindow?.ShellStatus.SetBusy(true);
            await AppServices.DaemonControl.StartAsync();

            if (MainWindow is wslc_desktop.MainWindow refreshedWindow)
            {
                await refreshedWindow.RefreshShellStatusAsync();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or HttpRequestException or IOException)
        {
            if (MainWindow is wslc_desktop.MainWindow failedWindow)
            {
                failedWindow.ShellStatus.ShowOfflineError(ex.Message);
            }
        }
        finally
        {
            if (MainWindow is wslc_desktop.MainWindow completedWindow)
            {
                completedWindow.ShellStatus.SetBusy(false);
            }
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
    }

    private static void WriteCrashLog(Exception ex)
    {
        AppLaunchLogger.Error("WinUI unhandled exception.", ex);
    }
}
