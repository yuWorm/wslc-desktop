using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using wslc_desktop.Models;
using wslc_desktop.Services;
using wslc_desktop.ViewModels;

namespace wslc_desktop.Pages;

public sealed partial class NetworksPage : Page
{
    private readonly NetworksViewModel _viewModel = new(AppServices.Networks);

    public NetworksPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += NetworksPage_Loaded;
    }

    private async void NetworksPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void ShowEndpointDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NetworkEndpointSummary endpoint })
        {
            _viewModel.SelectedEndpoint = endpoint;
        }

        if (_viewModel.SelectedEndpoint is null)
        {
            return;
        }

        NetworkEndpointSummary selected = _viewModel.SelectedEndpoint;
        var detail = new StackPanel
        {
            MinWidth = 520,
            Spacing = 10,
            Children =
            {
                CreateDetailRow(AppServices.Strings.Get("EndpointDetailContainer"), selected.ContainerName),
                CreateDetailRow(AppServices.Strings.Get("EndpointDetailHostPort"), selected.HostPort.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                CreateDetailRow(AppServices.Strings.Get("EndpointDetailContainerPort"), selected.ContainerPort.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                CreateDetailRow(AppServices.Strings.Get("EndpointDetailProtocol"), selected.Protocol),
                CreateDetailRow(AppServices.Strings.Get("EndpointDetailUrl"), selected.Url)
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = selected.ContainerName,
            Content = detail,
            CloseButtonText = AppServices.Strings.Get("Close"),
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private static Grid CreateDetailRow(string label, string value)
    {
        var grid = new Grid
        {
            ColumnSpacing = 16
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
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
