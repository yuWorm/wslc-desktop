using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using wslc_desktop.Models;
using wslc_desktop.Services;
using wslc_desktop.ViewModels;

namespace wslc_desktop.Pages;

public sealed partial class ImagesPage : Page
{
    private readonly ImagesViewModel _viewModel = new(AppServices.Images, AppServices.Operations);

    public ImagesPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += ImagesPage_Loaded;
    }

    private async void ImagesPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void PullImage_Click(object sender, RoutedEventArgs e)
    {
        var referenceBox = new TextBox
        {
            Header = AppServices.Strings.Get("PullImageReference"),
            Text = _viewModel.ImageReference,
            MinWidth = 520
        };
        AutomationProperties.SetAutomationId(referenceBox, "TxtImagePullReference");

        var panel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                referenceBox
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppServices.Strings.Get("PullImageTitle"),
            Content = panel,
            PrimaryButtonText = AppServices.Strings.Get("Pull"),
            CloseButtonText = AppServices.Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(referenceBox.Text);
        referenceBox.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(referenceBox.Text);
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _viewModel.ImageReference = referenceBox.Text;
            _ = _viewModel.PullAsync();
        }
    }

    private async void DeleteImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ImageSummary image })
        {
            _viewModel.SelectedImage = image;
        }

        await ConfirmDeleteSelectedAsync();
    }

    private async Task ConfirmDeleteSelectedAsync()
    {
        if (_viewModel.SelectedImage is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppServices.Strings.Get("DeleteImageTitle"),
            Content = $"{_viewModel.SelectedImage.Repository}:{_viewModel.SelectedImage.Tag}",
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
}
