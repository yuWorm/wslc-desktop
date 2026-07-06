using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using wslc_desktop.Models;
using wslc_desktop.Services;

namespace wslc_desktop.Pages;

internal static class CliToolsInstallDialog
{
    public static async Task ShowAsync(
        FrameworkElement owner,
        IEnvironmentBootstrapService bootstrapService,
        ICliToolInstallationService cliToolInstallationService)
    {
        var statusInfo = new InfoBar
        {
            IsOpen = true,
            Severity = InfoBarSeverity.Informational,
            Title = AppServices.Strings.Get("CliToolsDialogStatusTitle"),
            Message = AppServices.Strings.Get("CliToolsStatusUnknown")
        };

        var installDirectory = new TextBlock
        {
            Text = cliToolInstallationService.BinDirectory,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        var composePluginDirectory = new TextBlock
        {
            Text = cliToolInstallationService.ComposePluginDirectory,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        var progress = new ProgressRing
        {
            Height = 20,
            Width = 20,
            IsActive = false
        };

        var refreshButton = CreateDialogButton("\uE72C", AppServices.Strings.Get("CliToolsDialogRefresh"));
        var installDockerButton = CreateDialogButton("\uE896", AppServices.Strings.Get("CliToolsDialogInstallDocker"));
        var installDockerZipButton = CreateDialogButton("\uE8E5", AppServices.Strings.Get("CliToolsDialogInstallDockerZip"));
        var installComposeButton = CreateDialogButton("\uE896", AppServices.Strings.Get("CliToolsDialogInstallCompose"));
        var installComposeExeButton = CreateDialogButton("\uE8E5", AppServices.Strings.Get("CliToolsDialogSelectComposeExe"));
        var addUserPathButton = CreateDialogButton("\uE8A7", AppServices.Strings.Get("CliToolsDialogAddUserPath"));
        var addSystemPathButton = CreateDialogButton("\uE7EF", AppServices.Strings.Get("CliToolsDialogAddSystemPath"));
        Button[] actionButtons =
        [
            refreshButton,
            installDockerButton,
            installDockerZipButton,
            installComposeButton,
            installComposeExeButton,
            addUserPathButton,
            addSystemPathButton
        ];

        async Task RefreshAsync()
        {
            WslcPrerequisiteStatus wslc = await bootstrapService.CheckWslcAsync();
            DockerCliStatus docker = await bootstrapService.CheckDockerCliAsync();
            statusInfo.Severity = docker.DockerCliAvailable && docker.ComposeAvailable
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Informational;
            statusInfo.Message = FormatCliToolsStatus(wslc, docker);
        }

        async Task RunActionAsync(Func<Task<string>> action)
        {
            SetBusy(true);
            try
            {
                string message = await action();
                statusInfo.Severity = InfoBarSeverity.Success;
                statusInfo.Message = message;
                await RefreshAsync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException)
            {
                statusInfo.Severity = InfoBarSeverity.Error;
                statusInfo.Message = AppServices.Strings.Format("CliToolsOperationFailed", ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        void SetBusy(bool isBusy)
        {
            progress.IsActive = isBusy;
            foreach (Button button in actionButtons)
            {
                button.IsEnabled = !isBusy;
            }
        }

        refreshButton.Click += async (_, _) => await RunActionAsync(async () =>
        {
            await RefreshAsync();
            return AppServices.Strings.Get("CliToolsDialogRefreshed");
        });
        installDockerButton.Click += async (_, _) => await RunActionAsync(async () =>
        {
            CliToolInstallResult result = await cliToolInstallationService.InstallLatestDockerCliAsync();
            bootstrapService.AddToolBinToProcessPath();
            return result.Message;
        });
        installDockerZipButton.Click += async (_, _) => await RunActionAsync(async () =>
        {
            string? path = await PickFileAsync(owner, ".zip");
            if (string.IsNullOrWhiteSpace(path))
            {
                return AppServices.Strings.Get("CliToolsDialogNoFileSelected");
            }

            CliToolInstallResult result = await cliToolInstallationService.InstallDockerCliFromZipAsync(path);
            bootstrapService.AddToolBinToProcessPath();
            return result.Message;
        });
        installComposeButton.Click += async (_, _) => await RunActionAsync(async () =>
        {
            CliToolInstallResult result = await cliToolInstallationService.InstallLatestComposeAsync();
            bootstrapService.AddToolBinToProcessPath();
            return result.Message;
        });
        installComposeExeButton.Click += async (_, _) => await RunActionAsync(async () =>
        {
            string? path = await PickFileAsync(owner, ".exe");
            if (string.IsNullOrWhiteSpace(path))
            {
                return AppServices.Strings.Get("CliToolsDialogNoFileSelected");
            }

            CliToolInstallResult result = await cliToolInstallationService.InstallComposeFromExeAsync(path);
            bootstrapService.AddToolBinToProcessPath();
            return result.Message;
        });
        addUserPathButton.Click += async (_, _) => await RunActionAsync(() =>
        {
            string bin = cliToolInstallationService.AddBinToUserPath();
            return Task.FromResult(AppServices.Strings.Format("CliToolsPathAdded", bin));
        });
        addSystemPathButton.Click += async (_, _) => await RunActionAsync(async () =>
        {
            string bin = await cliToolInstallationService.AddBinToMachinePathAsync();
            return AppServices.Strings.Format("CliToolsSystemPathAdded", bin);
        });

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        titleRow.Children.Add(new TextBlock
        {
            Text = AppServices.Strings.Get("CliToolsDialogInstallDirectory"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        titleRow.Children.Add(progress);

        var content = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 760
        };
        content.Children.Add(new TextBlock
        {
            Text = AppServices.Strings.Get("CliToolsDialogDescription"),
            TextWrapping = TextWrapping.WrapWholeWords
        });
        content.Children.Add(statusInfo);
        content.Children.Add(titleRow);
        content.Children.Add(installDirectory);
        content.Children.Add(new TextBlock
        {
            Text = AppServices.Strings.Get("CliToolsDialogComposePluginDirectory"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(composePluginDirectory);
        content.Children.Add(CreateButtonRow(refreshButton, installDockerButton, installDockerZipButton));
        content.Children.Add(CreateButtonRow(installComposeButton, installComposeExeButton));
        content.Children.Add(CreateButtonRow(addUserPathButton, addSystemPathButton));

        await RefreshAsync();

        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = AppServices.Strings.Get("CliToolsDialogTitle"),
            Content = content,
            CloseButtonText = AppServices.Strings.Get("Close"),
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private static Button CreateDialogButton(string glyph, string text)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        panel.Children.Add(new FontIcon
        {
            FontSize = 14,
            Glyph = glyph
        });
        panel.Children.Add(new TextBlock
        {
            Text = text
        });

        return new Button
        {
            Content = panel
        };
    }

    private static StackPanel CreateButtonRow(params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        foreach (Button button in buttons)
        {
            row.Children.Add(button);
        }

        return row;
    }

    private static async Task<string?> PickFileAsync(FrameworkElement element, params string[] extensions)
    {
        var picker = new FileOpenPicker(element.XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedStartLocation = PickerLocationId.Downloads
        };

        foreach (string extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (picker.FileTypeFilter.Count == 0)
        {
            picker.FileTypeFilter.Add("*");
        }

        PickFileResult? result = await picker.PickSingleFileAsync();
        return result?.Path;
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
}
