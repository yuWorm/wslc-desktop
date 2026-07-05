using Microsoft.UI.Xaml.Controls;
using wslc_desktop.Services;
using wslc_desktop.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace wslc_desktop.Pages;

public sealed partial class ComposePage : Page
{
    private readonly ComposeViewModel _viewModel = new(AppServices.ComposePlans);

    public ComposePage()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += ComposePage_Loaded;
    }

    private async void ComposePage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void OpenCompose_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".yaml");
        picker.FileTypeFilter.Add(".yml");

        var window = App.MainWindow ?? throw new InvalidOperationException("Main window is not available.");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await _viewModel.OpenComposePathAsync(file.Path);
    }
}
