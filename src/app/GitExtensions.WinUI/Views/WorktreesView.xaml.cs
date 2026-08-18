using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace GitExtensions.WinUI.Views;

/// <summary>Linked worktrees, and adding or removing them.</summary>
public sealed partial class WorktreesView : RepositoryPage
{
    public WorktreesView()
    {
        InitializeComponent();
    }

    public override async Task ActivateAsync()
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            await tab.LoadWorktreesAsync();
        }
    }

    private static WorktreeInfo? WorktreeFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as WorktreeInfo;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ActivateAsync();

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (await PromptAsync("Add worktree", "Folder for the new checkout", "Next") is not string path
            || path.Length == 0)
        {
            return;
        }

        if (await PromptAsync("Add worktree", "Branch to check out there", "Add") is string branch
            && branch.Length > 0)
        {
            await ReportAsync(tab => tab.AddWorktreeAsync(path, branch));
            await ActivateAsync();
        }
    }

    private async void Prune_Click(object sender, RoutedEventArgs e)
    {
        await ReportAsync(tab => tab.PruneWorktreesAsync());
        await ActivateAsync();
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (WorktreeFrom(sender) is not WorktreeInfo worktree)
        {
            return;
        }

        if (!worktree.CanRemove)
        {
            Tab?.ReportInformation("Cannot remove", "That is the main worktree — the repository itself.");
            return;
        }

        if (await ConfirmAsync(
            "Remove worktree?",
            $"Removes the checkout at {worktree.Path}. Uncommitted changes there will be lost.",
            "Remove"))
        {
            await ReportAsync(tab => tab.RemoveWorktreeAsync(worktree.Path));
            await ActivateAsync();
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (WorktreeFrom(sender) is not WorktreeInfo worktree || Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        DataPackage package = new();
        package.SetText(worktree.Path);
        Clipboard.SetContent(package);

        tab.ReportInformation("Copied", $"{worktree.Path} copied to the clipboard.");
    }
}
