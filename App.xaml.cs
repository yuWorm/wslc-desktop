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
        UnhandledException += App_UnhandledException;
        var settings = AppServices.StartupSettings.LoadForStartup();
        string language = AppLanguage.GetEffectiveLanguage(settings.Language, ApplicationLanguages.Languages);
        ApplicationLanguages.PrimaryLanguageOverride = language;
        AppServices.InitializeLocalization(language);
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            MainWindow = _window;
            _window.Activate();
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

        AppServices.Bootstrap.AddToolBinToProcessPath();

        bool wslcReady = await EnsureWslcPrerequisiteAsync(mainWindow);
        if (!wslcReady)
        {
            return;
        }

        await MaybeShowDockerCliDialogAsync(mainWindow);
        await StartDaemonOnLaunchAsync();
    }

    private static async Task<bool> EnsureWslcPrerequisiteAsync(wslc_desktop.MainWindow mainWindow)
    {
        while (true)
        {
            WslcPrerequisiteStatus status = await AppServices.Bootstrap.CheckWslcAsync();
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

            Application.Current.Exit();
            return false;
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
            Content = CreateDialogContent(status.Message, status.RequiredCommand),
            PrimaryButtonText = AppServices.Strings.Get("Recheck"),
            SecondaryButtonText = AppServices.Strings.Get("CopyCommand"),
            CloseButtonText = AppServices.Strings.Get("Exit"),
            DefaultButton = ContentDialogButton.Primary
        };

        return await dialog.ShowAsync();
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
            panel.Children.Add(new TextBox
            {
                Text = command,
                IsReadOnly = true,
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
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WSLC Desktop",
                "Diagnostics");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "last-crash.log");
            File.WriteAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}");
        }
        catch
        {
        }
    }
}
