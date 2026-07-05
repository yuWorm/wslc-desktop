using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using wslc_desktop.Services;
using wslc_desktop.ViewModels;

namespace wslc_desktop.Pages;

public sealed partial class ContainersPage : Page
{
    private readonly ContainersViewModel _viewModel = new(
        AppServices.Containers,
        AppServices.Processes,
        AppServices.Terminals,
        AppServices.Operations);

    public ContainersPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += ContainersPage_Loaded;
        ContainersDetailSelector.SelectedItem = OverviewSelectorItem;
        ShowDetailPane(OverviewSelectorItem);
    }

    private async void ContainersPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void Refresh_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void CreateContainer_Click(object sender, RoutedEventArgs e)
    {
        var imageSuggestions = await LoadContainerImageSuggestionsAsync();
        var imageBox = CreateImageAutoSuggestBox("ContainerImageInput", AppServices.Strings.Get("CreateContainerImage"), _viewModel.NewContainerImage, imageSuggestions);
        var nameBox = CreateDialogTextBox("ContainerNameInput", AppServices.Strings.Get("CreateContainerName"), _viewModel.NewContainerName);
        var commandBox = CreateDialogTextBox("ContainerCommandInput", AppServices.Strings.Get("CreateContainerCommand"), _viewModel.NewContainerCommand);
        var portsBox = CreateDialogTextBox("ContainerPortsInput", AppServices.Strings.Get("CreateContainerPorts"), _viewModel.NewContainerPort);
        var mountsBox = CreateDialogTextBox("ContainerMountsInput", AppServices.Strings.Get("CreateContainerMounts"), _viewModel.NewContainerMounts);
        var environmentBox = CreateDialogTextBox("ContainerEnvironmentInput", AppServices.Strings.Get("CreateContainerEnvironment"), _viewModel.NewContainerEnvironment);

        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 520,
            Children =
            {
                imageBox,
                nameBox,
                commandBox,
                portsBox,
                mountsBox,
                environmentBox
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppServices.Strings.Get("CreateContainerTitle"),
            Content = content,
            PrimaryButtonText = AppServices.Strings.Get("Create"),
            CloseButtonText = AppServices.Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(imageBox.Text);
        imageBox.TextChanged += (_, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                imageBox.ItemsSource = ContainerImageSuggestionProvider.Filter(imageSuggestions, imageBox.Text);
            }

            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(imageBox.Text);
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        _viewModel.NewContainerImage = imageBox.Text;
        _viewModel.NewContainerName = nameBox.Text;
        _viewModel.NewContainerCommand = commandBox.Text;
        _viewModel.NewContainerPort = portsBox.Text;
        _viewModel.NewContainerMounts = mountsBox.Text;
        _viewModel.NewContainerEnvironment = environmentBox.Text;
        await _viewModel.CreateAsync();
    }

    private static async Task<IReadOnlyList<string>> LoadContainerImageSuggestionsAsync()
    {
        try
        {
            return ContainerImageSuggestionProvider.BuildReferences(await AppServices.Images.ListImagesAsync());
        }
        catch
        {
            return [];
        }
    }

    private async void ContainerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await _viewModel.LoadSelectedLogsAsync();
    }

    private async void DeleteSelected_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_viewModel.SelectedContainer is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppServices.Strings.Get("DeleteContainerTitle"),
            Content = _viewModel.SelectedContainer.Name,
            PrimaryButtonText = AppServices.Strings.Get("Delete"),
            CloseButtonText = AppServices.Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await _viewModel.DeleteSelectedAsync();
        }
    }

    private void ContainersDetailSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ShowDetailPane(sender.SelectedItem);
    }

    private async void OpenPort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url }
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        await Launcher.LaunchUriAsync(uri);
    }

    private async void ShowLogsDialog_Click(object sender, RoutedEventArgs e)
    {
        await ShowTextDialogAsync(
            $"{_viewModel.SelectedContainerName} · {AppServices.Strings.Get("ContainersLogsDialogTitle")}",
            _viewModel.SelectedContainerLogs);
    }

    private async void ShowTerminalDialog_Click(object sender, RoutedEventArgs e)
    {
        await ShowTextDialogAsync(
            $"{_viewModel.SelectedContainerName} · {AppServices.Strings.Get("ContainersTerminalDialogTitle")}",
            _viewModel.TerminalTranscript);
    }

    private async void ShowInspectDialog_Click(object sender, RoutedEventArgs e)
    {
        await ShowTextDialogAsync(
            $"{_viewModel.SelectedContainerName} · {AppServices.Strings.Get("ContainersInspectDialogTitle")}",
            _viewModel.SelectedContainerInspectJson);
    }

    private async void TerminalInputLine_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await _viewModel.SendTerminalInputLineAsync();
    }

    private static TextBox CreateDialogTextBox(string automationId, string header, string text)
    {
        var textBox = new TextBox
        {
            Header = header,
            Text = text
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(textBox, automationId);
        return textBox;
    }

    private static AutoSuggestBox CreateImageAutoSuggestBox(string automationId, string header, string text, IReadOnlyList<string> suggestions)
    {
        var box = new AutoSuggestBox
        {
            Header = header,
            Text = text,
            ItemsSource = ContainerImageSuggestionProvider.Filter(suggestions, text),
            MaxSuggestionListHeight = 240
        };
        box.SuggestionChosen += (_, args) =>
        {
            if (args.SelectedItem is string imageReference)
            {
                box.Text = imageReference;
            }
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }

    private void ShowDetailPane(SelectorBarItem? selectedItem)
    {
        OverviewPane.Visibility = selectedItem == OverviewSelectorItem ? Visibility.Visible : Visibility.Collapsed;
        StatsPane.Visibility = selectedItem == StatsSelectorItem ? Visibility.Visible : Visibility.Collapsed;
        LogsPane.Visibility = selectedItem == LogsSelectorItem ? Visibility.Visible : Visibility.Collapsed;
        TerminalPane.Visibility = selectedItem == TerminalSelectorItem ? Visibility.Visible : Visibility.Collapsed;
        EnvPane.Visibility = selectedItem == EnvSelectorItem ? Visibility.Visible : Visibility.Collapsed;
        InspectPane.Visibility = selectedItem == InspectSelectorItem ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ShowTextDialogAsync(string title, string text)
    {
        var textBlock = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = 12,
            Text = string.IsNullOrWhiteSpace(text) ? "-" : text,
            TextWrapping = TextWrapping.NoWrap
        };

        var viewer = new ScrollViewer
        {
            Content = textBlock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinWidth = 760,
            MaxWidth = 900,
            Height = 560
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = title,
            Content = viewer,
            CloseButtonText = AppServices.Strings.Get("Close"),
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

}
