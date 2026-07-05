// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using wslc_desktop;
using wslc_desktop.Services;
using wslc_desktop.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace wslc_desktop.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel = new(
        AppServices.Settings,
        AppServices.Diagnostics,
        AppServices.ProviderPreview,
        AppServices.StartupTask,
        AppServices.DaemonControl,
        AppServices.DaemonDiagnostics,
        AppServices.Bootstrap,
        AppServices.CliTools,
        AppServices.DockerContext);

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += SettingsPage_Loaded;
    }

    public SettingsViewModel ViewModel => _viewModel;

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SettingsPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanSaveSettings)
        {
            return;
        }

        await _viewModel.SaveAsync();
        if (_viewModel.HasError || !_viewModel.LastSaveRequiresDaemonRestart)
        {
            return;
        }

        if (_viewModel.CanRestartDaemon)
        {
            bool confirmed = await ConfirmDaemonActionAsync(
                AppServices.Strings.Get("DaemonRestartDialogTitle"),
                AppServices.Strings.Get("DaemonRestartDialogContent"),
                AppServices.Strings.Get("Restart"));

            if (confirmed)
            {
                await _viewModel.RestartDaemonAsync();
                await RefreshShellStatusAsync();
            }
        }
        else if (_viewModel.CanStartDaemon)
        {
            bool confirmed = await ConfirmDaemonActionAsync(
                AppServices.Strings.Get("DaemonStartDialogTitle"),
                AppServices.Strings.Get("DaemonStartDialogContent"),
                AppServices.Strings.Get("Start"));

            if (confirmed)
            {
                await _viewModel.StartDaemonAsync();
                await RefreshShellStatusAsync();
            }
        }
    }

    private async void StartDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanStartDaemon)
        {
            return;
        }

        await _viewModel.StartDaemonAsync();
        await RefreshShellStatusAsync();
    }

    private async void RestartDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanRestartDaemon)
        {
            return;
        }

        bool confirmed = await ConfirmDaemonActionAsync(
            AppServices.Strings.Get("DaemonRestartDialogTitle"),
            AppServices.Strings.Get("DaemonRestartDialogContent"),
            AppServices.Strings.Get("Restart"));

        if (confirmed)
        {
            await _viewModel.RestartDaemonAsync();
            await RefreshShellStatusAsync();
        }
    }

    private async void StopDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanStopDaemon)
        {
            return;
        }

        bool confirmed = await ConfirmDaemonActionAsync(
            AppServices.Strings.Get("DaemonStopDialogTitle"),
            AppServices.Strings.Get("DaemonStopDialogContent"),
            AppServices.Strings.Get("Stop"));

        if (confirmed)
        {
            await _viewModel.StopDaemonAsync();
            await RefreshShellStatusAsync();
        }
    }

    private async void RefreshCliTools_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshCliToolsStatusAsync();
    }

    private async void OpenCliToolsDialog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        await CliToolsInstallDialog.ShowAsync(element, AppServices.Bootstrap, AppServices.CliTools);
        await _viewModel.RefreshCliToolsStatusAsync();
    }

    private async void CreateDockerContextDefault_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanSaveSettings)
        {
            return;
        }

        bool confirmed = await ConfirmDockerContextDefaultAsync();
        if (!confirmed)
        {
            return;
        }

        await _viewModel.CreateDefaultDockerContextAsync();
    }

    private async Task<bool> ConfirmDaemonActionAsync(string title, string content, string primaryText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = title,
            Content = new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = primaryText,
            CloseButtonText = AppServices.Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmDockerContextDefaultAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = AppServices.Strings.Get("DockerContextDefaultConfirmTitle"),
            Content = new TextBlock
            {
                Text = AppServices.Strings.Get("DockerContextDefaultConfirmContent"),
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = AppServices.Strings.Get("DockerContextDefaultConfirmPrimary"),
            CloseButtonText = AppServices.Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static async Task RefreshShellStatusAsync()
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            await mainWindow.RefreshShellStatusAsync();
        }
    }

}
