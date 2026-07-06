using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using wslc_desktop.Models;
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
        ContainerCreateDraft? draft = await ContainerCreateDialog.ShowAsync(XamlRoot, _viewModel.CreateDraft(), imageSuggestions);
        if (draft is null)
        {
            return;
        }

        _viewModel.ApplyCreateDraft(draft);
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
