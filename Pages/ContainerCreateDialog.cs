using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using wslc_desktop.Models;
using wslc_desktop.Services;
using wslc_desktop.ViewModels;

namespace wslc_desktop.Pages;

internal static class ContainerCreateDialog
{
    public static async Task<ContainerCreateDraft?> ShowAsync(
        XamlRoot xamlRoot,
        ContainerCreateDraft initialDraft,
        IReadOnlyList<string> imageSuggestions)
    {
        var imageBox = CreateImageAutoSuggestBox("ContainerImageInput", AppServices.Strings.Get("CreateContainerImage"), initialDraft.Image, imageSuggestions);
        var nameBox = CreateDialogTextBox("ContainerNameInput", AppServices.Strings.Get("CreateContainerName"), initialDraft.Name);
        var commandBox = CreateDialogTextBox("ContainerCommandInput", AppServices.Strings.Get("CreateContainerCommand"), initialDraft.Command);
        var portsBox = CreateDialogTextBox("ContainerPortsInput", AppServices.Strings.Get("CreateContainerPorts"), initialDraft.Ports);
        var mountsBox = CreateDialogTextBox("ContainerMountsInput", AppServices.Strings.Get("CreateContainerMounts"), initialDraft.Mounts);
        var environmentBox = CreateDialogTextBox("ContainerEnvironmentInput", AppServices.Strings.Get("CreateContainerEnvironment"), initialDraft.Environment);

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
            XamlRoot = xamlRoot,
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

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            ? new ContainerCreateDraft(
                nameBox.Text,
                imageBox.Text,
                commandBox.Text,
                portsBox.Text,
                mountsBox.Text,
                environmentBox.Text)
            : null;
    }

    private static TextBox CreateDialogTextBox(string automationId, string header, string text)
    {
        var textBox = new TextBox
        {
            Header = header,
            Text = text
        };

        AutomationProperties.SetAutomationId(textBox, automationId);
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

        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }
}
