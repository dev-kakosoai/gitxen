using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace GitExtensions.WinUI.Views;

/// <summary>Tags, with the operations that were previously buried three menus deep.</summary>
public sealed partial class TagsView : RepositoryPage
{
    public TagsView()
    {
        InitializeComponent();
    }

    public override async Task ActivateAsync()
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            await tab.LoadTagsAsync();
        }
    }

    private static TagInfo? TagFrom(object sender) => (sender as FrameworkElement)?.DataContext as TagInfo;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ActivateAsync();

    private async void Checkout_Click(object sender, RoutedEventArgs e)
    {
        if (TagFrom(sender) is not TagInfo tag)
        {
            return;
        }

        if (await ConfirmAsync(
            "Check out this tag?",
            $"This leaves HEAD detached at '{tag.Name}'. You will not be on a branch until you check one out again.",
            "Check out"))
        {
            await ReportAsync(t => t.CheckoutDetachedAsync(tag.Name));
        }
    }

    private async void Push_Click(object sender, RoutedEventArgs e)
    {
        if (TagFrom(sender) is not TagInfo tag)
        {
            return;
        }

        // Pushing is the only thing here that changes something off this machine.
        if (await ConfirmAsync("Push tag?", $"Push '{tag.Name}' to the remote?", "Push"))
        {
            await ReportAsync(t => t.PushTagAsync(tag.Name));
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TagFrom(sender) is not TagInfo tag)
        {
            return;
        }

        if (await ConfirmAsync(
            "Delete tag?",
            $"Deletes '{tag.Name}' from this repository. A tag already pushed stays on the remote.",
            "Delete"))
        {
            await ReportAsync(t => t.DeleteTagAsync(tag.Name));
            await ActivateAsync();
        }
    }

    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        if (TagFrom(sender) is not TagInfo tag || Tab is not RepositoryTabViewModel viewModel)
        {
            return;
        }

        DataPackage package = new();
        package.SetText(tag.Name);
        Clipboard.SetContent(package);

        viewModel.ReportInformation("Copied", $"{tag.Name} copied to the clipboard.");
    }
}
