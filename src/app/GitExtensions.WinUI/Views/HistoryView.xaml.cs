using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  The commit graph: history with its lanes and ref badges, the selected commit's details, and the
///  diff of whichever file that commit touched.
/// </summary>
/// <remarks>
///  Every operation that acts on a commit hangs off the row's own context menu. In the previous
///  layout these lived in a toolbar flyout labelled "History…", which meant picking a commit and then
///  hunting for the verb in an unrelated corner of the window.
/// </remarks>
public sealed partial class HistoryView : RepositoryPage
{
    public HistoryView()
    {
        InitializeComponent();
    }

    /// <summary>
    ///  Right-clicking a row selects it before its menu opens, so the operations act on the commit
    ///  actually clicked rather than on whatever was selected beforehand.
    /// </summary>
    private void CommitRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (Tab is RepositoryTabViewModel tab
            && sender is FrameworkElement { DataContext: CommitRowViewModel commit })
        {
            tab.SelectedCommit = commit;
        }
    }

    private void ScopeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        RevisionScope scope = ReferenceEquals(sender.SelectedItem, AllBranchesScope)
            ? RevisionScope.AllBranches
            : RevisionScope.CurrentBranch;

        // Assigning Query reloads, so only assign when it actually differs — the selector raises this
        // while restoring its own selection too.
        if (tab.Query.Scope != scope)
        {
            tab.Query = tab.Query with { Scope = scope };
        }
    }

    /// <summary>
    ///  Collects the search terms git will apply while walking history.
    /// </summary>
    /// <remarks>
    ///  Deliberately separate from the filter box. The filter narrows the rows already loaded, which
    ///  is instant but can only ever match the page you have; these terms are passed to
    ///  <c>git log</c>, so they find commits that were never read.
    /// </remarks>
    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        TextBox message = new() { Header = "Message contains", Text = tab.Query.MessageContains };
        TextBox author = new() { Header = "Author", Text = tab.Query.Author };
        TextBox content = new() { Header = "Added or removed text (searches file contents)", Text = tab.Query.ContainingText };
        TextBox path = new() { Header = "Limit to path", Text = tab.Query.Path };

        StackPanel panel = new() { Spacing = 12, Width = 420 };
        panel.Children.Add(message);
        panel.Children.Add(author);
        panel.Children.Add(content);
        panel.Children.Add(path);
        panel.Children.Add(new TextBlock
        {
            Text = "Leave a field empty to ignore it. Searching re-reads history from git, so it may "
                + "take a moment on a large repository.",
            Style = (Style)Application.Current.Resources["PageSubtitleStyle"],
            TextWrapping = TextWrapping.Wrap
        });

        ContentDialog dialog = new()
        {
            Title = "Search history",
            Content = panel,
            PrimaryButtonText = "Search",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            tab.Query = tab.Query with
            {
                MessageContains = message.Text.Trim(),
                Author = author.Text.Trim(),
                ContainingText = content.Text.Trim(),
                Path = path.Text.Trim()
            };
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            // Scope is a view preference rather than part of the search, so it survives clearing.
            tab.Query = new RevisionQuery(tab.Query.Scope);
        }
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            await tab.LoadMoreAsync();
        }
    }

    private async void CreateBranch_Click(object sender, RoutedEventArgs e)
    {
        if (await PromptAsync("Create branch", "Branch name", "Create") is string name && name.Length > 0)
        {
            await ReportAsync(tab => tab.CreateBranchAsync(name, checkout: true));
        }
    }

    private async void CreateTag_Click(object sender, RoutedEventArgs e)
    {
        if (await PromptAsync("Create tag", "Tag name", "Next") is not string name || name.Length == 0)
        {
            return;
        }

        // An empty message means a lightweight tag, which is a legitimate choice rather than a
        // cancellation — so only a cancelled second prompt stops here.
        if (await PromptAsync("Create tag", "Annotation message (leave empty for a lightweight tag)", "Create") is string message)
        {
            await ReportAsync(tab => tab.CreateTagAsync(name, message));
        }
    }

    private async void CheckoutCommit_Click(object sender, RoutedEventArgs e)
    {
        if (Tab?.SelectedCommit is not CommitRowViewModel { IsWorkingDirectory: false } commit)
        {
            return;
        }

        if (await ConfirmAsync(
            "Check out this commit?",
            $"This leaves HEAD detached at {commit.ShortHash}. You will not be on a branch until you check one out again.",
            "Check out"))
        {
            await ReportAsync(tab => tab.CheckoutDetachedAsync(commit.FullHash));
        }
    }

    private async void CherryPick_Click(object sender, RoutedEventArgs e) =>
        await ReportAsync(tab => tab.CherryPickAsync());

    private async void Revert_Click(object sender, RoutedEventArgs e) =>
        await ReportAsync(tab => tab.RevertAsync());

    private async void Rebase_Click(object sender, RoutedEventArgs e)
    {
        if (Tab?.SelectedCommit is not CommitRowViewModel { IsWorkingDirectory: false } commit)
        {
            return;
        }

        if (await ConfirmAsync(
            "Rebase onto this commit?",
            $"Replays the current branch on top of {commit.ShortHash}. Conflicts will pause the rebase.",
            "Rebase"))
        {
            await ReportAsync(tab => tab.RebaseAsync(commit.FullHash));
        }
    }

    private async void ResetSoft_Click(object sender, RoutedEventArgs e) => await ResetAsync(ResetMode.Soft);

    private async void ResetMixed_Click(object sender, RoutedEventArgs e) => await ResetAsync(ResetMode.Mixed);

    private async void ResetHard_Click(object sender, RoutedEventArgs e) => await ResetAsync(ResetMode.Hard);

    private async Task ResetAsync(ResetMode mode)
    {
        // Hard reset throws away uncommitted work, so it gets a confirmation the others don't need.
        if (mode == ResetMode.Hard && !await ConfirmAsync(
            "Hard reset?",
            "This discards all uncommitted changes in the working directory. It cannot be undone.",
            "Reset --hard"))
        {
            return;
        }

        await ReportAsync(tab => tab.ResetToSelectedAsync(mode));
    }

    private async void BisectStart_Click(object sender, RoutedEventArgs e) =>
        await ReportAsync(tab => tab.BisectAsync("start"));

    private async void BisectGood_Click(object sender, RoutedEventArgs e) =>
        await ReportAsync(tab => tab.BisectAtSelectedAsync("good"));

    private async void BisectBad_Click(object sender, RoutedEventArgs e) =>
        await ReportAsync(tab => tab.BisectAtSelectedAsync("bad"));

    private async void BisectReset_Click(object sender, RoutedEventArgs e) =>
        await ReportAsync(tab => tab.BisectAsync("reset"));

    private void CopyHash_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(Tab?.SelectedCommit?.FullHash, "Full hash copied.");

    private void CopySubject_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(Tab?.SelectedCommit?.Subject, "Subject copied.");

    private void CopyToClipboard(string? text, string confirmation)
    {
        if (Tab is not RepositoryTabViewModel tab || string.IsNullOrEmpty(text))
        {
            return;
        }

        DataPackage package = new();
        package.SetText(text);
        Clipboard.SetContent(package);

        tab.ReportInformation("Copied", confirmation);
    }

    private async void Blame_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not RepositoryTabViewModel { SelectedChangedFile: ChangedFileViewModel file } tab)
        {
            return;
        }

        string blame = await tab.GetBlameAsync(file.Name);
        await ShowTextAsync($"Blame — {file.Name}", string.IsNullOrWhiteSpace(blame) ? "No blame output." : blame);
    }

    private async void FileHistory_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not RepositoryTabViewModel { SelectedChangedFile: ChangedFileViewModel file } tab)
        {
            return;
        }

        IReadOnlyList<CommitRowViewModel> history = await tab.GetFileHistoryAsync(file.Name);

        string text = history.Count == 0
            ? "No history found for this file."
            : string.Join(
                Environment.NewLine,
                history.Select(commit => $"{commit.ShortHash}  {commit.Date,-22}  {commit.Author,-24}  {commit.Subject}"));

        await ShowTextAsync($"History — {file.Name}", text);
    }
}
