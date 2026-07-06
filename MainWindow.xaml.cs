using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Graphics;
using wslc_desktop.Pages;
using wslc_desktop.Services;
using wslc_desktop.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace wslc_desktop;

public sealed partial class MainWindow : Window
{
    private const int ShowWindowNormal = 1;

    private bool _allowClose;
    private readonly DispatcherTimer _statusTimer = new()
    {
        Interval = TimeSpan.FromSeconds(20)
    };

    public ShellStatusViewModel ShellStatus { get; } = new(
        cancellationToken => AppServices.DaemonDiagnostics.CaptureAsync(startIfNeeded: false, cancellationToken: cancellationToken),
        CreateShellStatusLabels());

    public AsyncRelayCommand ShowMainWindowCommand { get; }

    public AsyncRelayCommand ShowSettingsCommand { get; }

    public AsyncRelayCommand StartDaemonCommand { get; }

    public AsyncRelayCommand RestartDaemonCommand { get; }

    public AsyncRelayCommand StopDaemonCommand { get; }

    public AsyncRelayCommand ExitApplicationCommand { get; }

    public MainWindow()
    {
        AppLaunchLogger.Info("MainWindow constructor started.");
        ShowMainWindowCommand = new AsyncRelayCommand(() =>
        {
            ShowMainWindow();
            return Task.CompletedTask;
        });
        ShowSettingsCommand = new AsyncRelayCommand(() =>
        {
            ShowMainWindow();
            NavigateToSettings();
            return Task.CompletedTask;
        });
        StartDaemonCommand = new AsyncRelayCommand(StartDaemonAsync, () => ShellStatus.CanStartDaemon);
        RestartDaemonCommand = new AsyncRelayCommand(RestartDaemonAsync, () => ShellStatus.CanRestartDaemon);
        StopDaemonCommand = new AsyncRelayCommand(StopDaemonAsync, () => ShellStatus.CanStopDaemon);
        ExitApplicationCommand = new AsyncRelayCommand(ExitApplicationAsync);

        try
        {
            AppLaunchLogger.Info("MainWindow InitializeComponent started.");
            InitializeComponent();
            AppLaunchLogger.Info("MainWindow InitializeComponent completed.");
        }
        catch (Exception ex)
        {
            AppLaunchLogger.Error("MainWindow InitializeComponent failed.", ex);
            throw;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        CenterWindowOnPrimaryDisplay();
        AppWindow.SetIcon("Assets/AppIcon.ico");
        NavFrame.Navigate(typeof(ContainersPage));

        _statusTimer.Tick += ShellStatusTimer_Tick;
        ShellStatus.PropertyChanged += ShellStatus_PropertyChanged;
        AppTitleBar.Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        AppLaunchLogger.Info("MainWindow constructor completed.");
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "containers":
                    NavFrame.Navigate(typeof(ContainersPage));
                    break;
                case "images":
                    NavFrame.Navigate(typeof(ImagesPage));
                    break;
                case "volumes":
                    NavFrame.Navigate(typeof(VolumesPage));
                    break;
                case "networks":
                    NavFrame.Navigate(typeof(NetworksPage));
                    break;
                case "compose":
                    NavFrame.Navigate(typeof(ComposePage));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        BringWindowToFront();
        await RefreshShellStatusAsync();
        _statusTimer.Start();
    }

    private async void ShellStatusTimer_Tick(object? sender, object e)
    {
        await RefreshShellStatusAsync();
    }

    private async void RefreshShellStatus_Click(object sender, RoutedEventArgs e)
    {
        await RefreshShellStatusAsync();
    }

    private async void StartDaemonFromStatus_Click(object sender, RoutedEventArgs e)
    {
        await StartDaemonAsync();
    }

    private async void RestartDaemonFromStatus_Click(object sender, RoutedEventArgs e)
    {
        await RestartDaemonAsync();
    }

    private async void StopDaemonFromStatus_Click(object sender, RoutedEventArgs e)
    {
        await StopDaemonAsync();
    }

    private void OpenSettingsFromStatus_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSettings();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_allowClose)
        {
            _statusTimer.Stop();
            ShellStatus.PropertyChanged -= ShellStatus_PropertyChanged;
            TrayIcon.Dispose();
            return;
        }

        args.Handled = true;
        AppWindow.Hide();
    }

    private async Task ExitApplicationAsync()
    {
        _allowClose = true;
        _statusTimer.Stop();
        ShellStatus.SetBusy(true);

        try
        {
            await AppServices.DaemonControl.StopAllAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or HttpRequestException or IOException)
        {
            ShellStatus.ShowOfflineError(ex.Message);
        }
        finally
        {
            ShellStatus.SetBusy(false);
            Close();
            Application.Current.Exit();
        }
    }

    private void ShowMainWindow()
    {
        AppWindow.Show();
        Activate();
        BringWindowToFront();
    }

    public void ShowSettingsPage()
    {
        ShowMainWindow();
        NavigateToSettings();
    }

    private void NavigateToSettings()
    {
        NavView.SelectedItem = NavView.SettingsItem;
        NavFrame.Navigate(typeof(SettingsPage));
    }

    public async Task RefreshShellStatusAsync()
    {
        await ShellStatus.RefreshAsync();
    }

    private Task StartDaemonAsync()
    {
        if (!ShellStatus.CanStartDaemon)
        {
            return Task.CompletedTask;
        }

        return RunDaemonActionAsync(cancellationToken => AppServices.DaemonControl.StartAsync(cancellationToken));
    }

    private Task RestartDaemonAsync()
    {
        if (!ShellStatus.CanRestartDaemon)
        {
            return Task.CompletedTask;
        }

        return RunDaemonActionAsync(cancellationToken => AppServices.DaemonControl.RestartAsync(cancellationToken));
    }

    private Task StopDaemonAsync()
    {
        if (!ShellStatus.CanStopDaemon)
        {
            return Task.CompletedTask;
        }

        return RunDaemonActionAsync(async cancellationToken =>
        {
            await AppServices.DaemonControl.StopAllAsync(cancellationToken);
        });
    }

    private async Task RunDaemonActionAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            _statusTimer.Stop();
            ShellStatus.SetBusy(true);
            await action(CancellationToken.None);
            await RefreshShellStatusAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or HttpRequestException or IOException)
        {
            ShellStatus.ShowOfflineError(ex.Message);
        }
        finally
        {
            ShellStatus.SetBusy(false);
            _statusTimer.Start();
            RaiseTrayCommandCanExecuteChanged();
        }
    }

    private void ShellStatus_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellStatusViewModel.CanStartDaemon)
            or nameof(ShellStatusViewModel.CanRestartDaemon)
            or nameof(ShellStatusViewModel.CanStopDaemon)
            or nameof(ShellStatusViewModel.IsBusy)
            or nameof(ShellStatusViewModel.State))
        {
            RaiseTrayCommandCanExecuteChanged();
        }
    }

    private void RaiseTrayCommandCanExecuteChanged()
    {
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

    private void CenterWindowOnPrimaryDisplay()
    {
        DisplayArea displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;
        int width = Math.Min(Math.Max(760, workArea.Width - 160), workArea.Width);
        int height = Math.Min(Math.Max(560, workArea.Height - 120), workArea.Height);
        int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void BringWindowToFront()
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, ShowWindowNormal);
        _ = SetForegroundWindow(hwnd);
        AppLaunchLogger.Info("MainWindow foreground requested.");
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
}
