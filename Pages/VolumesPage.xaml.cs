using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using wslc_desktop.Models;
using wslc_desktop.Services;
using wslc_desktop.ViewModels;

namespace wslc_desktop.Pages;

public sealed partial class VolumesPage : Page
{
    private readonly VolumesViewModel _viewModel = new(AppServices.Volumes);

    public VolumesPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += VolumesPage_Loaded;
    }

    private async void VolumesPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void NewVolume_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            Header = AppServices.Strings.Get("VolumeNameInput"),
            Text = _viewModel.NewVolumeName,
            MinWidth = 520
        };
        AutomationProperties.SetAutomationId(nameBox, "TxtVolumeDialogName");

        var sizeBox = new NumberBox
        {
            Header = AppServices.Strings.Get("VolumeSizeInput"),
            Value = _viewModel.NewVolumeSizeMb,
            Minimum = 1,
            SmallChange = 512,
            MinWidth = 220
        };
        AutomationProperties.SetAutomationId(sizeBox, "NumVolumeDialogSize");

        var panel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                nameBox,
                sizeBox
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppServices.Strings.Get("CreateVolumeTitle"),
            Content = panel,
            PrimaryButtonText = AppServices.Strings.Get("Create"),
            CloseButtonText = AppServices.Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _viewModel.NewVolumeName = nameBox.Text;
            _viewModel.NewVolumeSizeMb = sizeBox.Value;
            await _viewModel.CreateVolumeAsync();
        }
    }

    private async void ShowVolumeDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VolumeSummary volume })
        {
            _viewModel.SelectedVolume = volume;
        }

        if (_viewModel.SelectedVolume is null)
        {
            return;
        }

        VolumeSummary selected = _viewModel.SelectedVolume;
        var detail = new StackPanel
        {
            MinWidth = 520,
            Spacing = 10,
            Children =
            {
                CreateDetailRow(AppServices.Strings.Get("VolumeDetailName"), selected.Name),
                CreateDetailRow(AppServices.Strings.Get("VolumeDetailKind"), selected.Kind),
                CreateDetailRow(AppServices.Strings.Get("VolumeDetailSize"), selected.Size),
                CreateDetailRow(AppServices.Strings.Get("VolumeDetailUsedBy"), selected.UsedBy),
                CreateDetailRow(AppServices.Strings.Get("VolumeDetailCreated"), selected.Created)
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = selected.Name,
            Content = detail,
            CloseButtonText = AppServices.Strings.Get("Close"),
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private async void DeleteVolume_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_viewModel.CanDeleteSelectedVolume || _viewModel.SelectedVolume is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppServices.Strings.Get("DeleteVolumeTitle"),
            Content = AppServices.Strings.Format("DeleteVolumeContent", _viewModel.SelectedVolume.Name),
            PrimaryButtonText = AppServices.Strings.Get("Delete"),
            CloseButtonText = AppServices.Strings.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.DeleteSelectedVolumeAsync();
        }
    }

    private static Grid CreateDetailRow(string label, string value)
    {
        var grid = new Grid
        {
            ColumnSpacing = 16
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label
        };

        var valueBlock = new TextBlock
        {
            Text = value,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono"),
            TextWrapping = TextWrapping.WrapWholeWords,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        return grid;
    }
}
