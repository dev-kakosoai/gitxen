using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  The reflog: where HEAD has been, and the way back from a reset or rebase that went wrong.
/// </summary>
/// <remarks>
///  A commit dropped by a reset is unreachable from any branch, so it never appears in the history
///  graph — but it is still in the object store, and its reflog selector still resolves. That makes
///  this the only page from which such work can be recovered.
/// </remarks>
public sealed partial class ReflogView : RepositoryPage
{
    public ReflogView()
    {
        InitializeComponent();
    }

    public override async Task ActivateAsync()
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            await tab.LoadReflogAsync();
        }
    }

    private static ReflogEntry? EntryFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as ReflogEntry;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ActivateAsync();

    private async void CreateBranch_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFrom(sender) is not ReflogEntry entry)
        {
            return;
        }

        // Branching is the non-destructive recovery: the old state gets a name without moving
        // anything that currently exists.
        if (await PromptAsync("Create branch", $"Name for a branch at {entry.ShortHash}", "Create") is string name
            && name.Length > 0)
        {
            await ReportAsync(tab => tab.CreateBranchAtAsync(name, entry.ShortHash, checkout: true));
            await ActivateAsync();
        }
    }

    private async void Checkout_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFrom(sender) is not ReflogEntry entry)
        {
            return;
        }

        if (await ConfirmAsync(
            "Check out this state?",
            $"Leaves HEAD detached at {entry.ShortHash}. Nothing is lost — your branches stay where they are.",
            "Check out"))
        {
            await ReportAsync(tab => tab.CheckoutDetachedAsync(entry.ShortHash));
        }
    }

    private async void ResetHard_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFrom(sender) is not ReflogEntry entry)
        {
            return;
        }

        if (await ConfirmAsync(
            "Reset to this state?",
            $"Moves the current branch to {entry.ShortHash} and discards all uncommitted changes. "
                + "The state you are leaving will itself be listed here afterwards.",
            "Reset --hard"))
        {
            await ReportAsync(tab => tab.ResetToAsync(entry.ShortHash, ResetMode.Hard));
            await ActivateAsync();
        }
    }

    private void CopySelector_Click(object sender, RoutedEventArgs e)
    {
        if (EntryFrom(sender) is not ReflogEntry entry || Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        DataPackage package = new();
        package.SetText(entry.Selector);
        Clipboard.SetContent(package);

        tab.ReportInformation("Copied", $"{entry.Selector} copied to the clipboard.");
    }
}
