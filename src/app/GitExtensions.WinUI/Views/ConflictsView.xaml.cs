using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  Resolving merge conflicts: a side per file, a merge tool, or edit it yourself and mark it done.
/// </summary>
/// <remarks>
///  Before this page the app could only list the conflicted paths and stage them, which meant every
///  real conflict ended in a terminal. The resolutions offered per row depend on the shape of the
///  conflict — a file one side deleted has no content to merge, so it is a question of whether the
///  file should exist, not which text wins.
/// </remarks>
public sealed partial class ConflictsView : RepositoryPage
{
    public ConflictsView()
    {
        InitializeComponent();
    }

    public Visibility EmptyVisibility =>
        Tab?.Conflicts.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ListVisibility =>
        Tab?.Conflicts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    ///  Says which side "ours" is, because it inverts during a rebase.
    /// </summary>
    /// <remarks>
    ///  During a rebase git replays your commits onto the other branch, so "ours" is the branch being
    ///  rebased onto and "theirs" is your own work — the opposite of what the words suggest, and one
    ///  of the most common ways people resolve a rebase backwards.
    /// </remarks>
    public string SideExplanation =>
        Tab?.Operation.Kind == RepositoryOperationKind.Rebase
            ? "During a rebase these are inverted: \"ours\" is the branch you are rebasing onto, and "
                + "\"theirs\" is the commit of yours being replayed."
            : "\"Ours\" is your current branch. \"Theirs\" is the branch being brought in.";

    public override async Task ActivateAsync()
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            await tab.LoadConflictsAsync();
            Bindings.Update();
        }
    }

    private static ConflictedFile? FileFrom(object sender) =>
        (sender as FrameworkElement)?.Tag as ConflictedFile;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ActivateAsync();

    private async void TakeOurs_Click(object sender, RoutedEventArgs e) => await TakeSideAsync(sender, ours: true);

    private async void TakeTheirs_Click(object sender, RoutedEventArgs e) => await TakeSideAsync(sender, ours: false);

    private async Task TakeSideAsync(object sender, bool ours)
    {
        if (FileFrom(sender) is ConflictedFile file)
        {
            await ReportAsync(tab => tab.TakeSideAsync(file, ours));
            await ActivateAsync();
        }
    }

    private async void TakeAllOurs_Click(object sender, RoutedEventArgs e) => await TakeAllAsync(ours: true);

    private async void TakeAllTheirs_Click(object sender, RoutedEventArgs e) => await TakeAllAsync(ours: false);

    /// <summary>
    ///  Resolving everything one way discards the other side's work in those files, so it is confirmed
    ///  and the count is stated.
    /// </summary>
    private async Task TakeAllAsync(bool ours)
    {
        if (Tab is not RepositoryTabViewModel tab || tab.Conflicts.Count == 0)
        {
            return;
        }

        string side = ours ? "ours" : "theirs";

        if (await ConfirmAsync(
            $"Keep {side} for all {tab.Conflicts.Count} file(s)?",
            $"Every conflicted file is resolved by taking {side}. The other side's changes to those "
                + "files are discarded.",
            $"Keep {side}"))
        {
            await ReportAsync(t => t.TakeSideForAllAsync(ours));
            await ActivateAsync();
        }
    }

    private async void MergeTool_Click(object sender, RoutedEventArgs e)
    {
        if (FileFrom(sender) is ConflictedFile file)
        {
            // Blocks until the tool is closed; the view model keeps it off the UI thread.
            await ReportAsync(tab => tab.LaunchMergeToolAsync(file.Path));
            await ActivateAsync();
        }
    }

    private async void MarkResolved_Click(object sender, RoutedEventArgs e)
    {
        if (FileFrom(sender) is ConflictedFile file)
        {
            await ReportAsync(tab => tab.MarkConflictResolvedAsync(file.Path));
            await ActivateAsync();
        }
    }

    /// <summary>Shows the file as it stands, conflict markers included.</summary>
    private async void View_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not RepositoryTabViewModel tab || FileFrom(sender) is not ConflictedFile file)
        {
            return;
        }

        string text = await tab.ReadConflictedTextAsync(file.Path);

        await ShowTextAsync(
            file.Path,
            text.Length == 0 ? "The file is empty or could not be read." : text);
    }
}
