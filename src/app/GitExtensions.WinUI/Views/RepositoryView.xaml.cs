using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  One open repository: a navigation pane of sections, a command bar of repository-wide actions, and
///  the result of the last git operation.
/// </summary>
/// <remarks>
///  The navigation pane is what breaks the previous single screen apart. Everything that used to hide
///  behind a toolbar flyout — branches, remotes, tags, stashes, submodules, worktrees — is a section
///  with a real list in it, and the actions live on the objects they act on.
/// </remarks>
public sealed partial class RepositoryView : UserControl
{
    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab), typeof(RepositoryTabViewModel), typeof(RepositoryView), new PropertyMetadata(null));

    public RepositoryView()
    {
        InitializeComponent();
    }

    public RepositoryTabViewModel? Tab
    {
        get => (RepositoryTabViewModel?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    /// <summary>The section pages, keyed by the Tag on their navigation item.</summary>
    private IReadOnlyDictionary<string, RepositoryPage> Pages => new Dictionary<string, RepositoryPage>
    {
        ["changes"] = ChangesPage,
        ["history"] = HistoryPage,
        ["reflog"] = ReflogPage,
        ["branches"] = BranchesPage,
        ["remotes"] = RemotesPage,
        ["tags"] = TagsPage,
        ["stashes"] = StashesPage,
        ["submodules"] = SubmodulesPage,
        ["worktrees"] = WorktreesPage,
        ["maintenance"] = MaintenancePage,
        ["settings"] = SettingsPage
    };

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        IReadOnlyDictionary<string, RepositoryPage> pages = Pages;

        if (!pages.TryGetValue(tag, out RepositoryPage? selected))
        {
            return;
        }

        foreach (RepositoryPage page in pages.Values)
        {
            page.Visibility = ReferenceEquals(page, selected) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Listings are read on arrival rather than up front, so opening a repository does not pay for
        // six git calls the user may never look at.
        await selected.ActivateAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            await tab.LoadAsync();
        }
    }

    private async void Fetch_Click(object sender, RoutedEventArgs e) => await RunAsync(tab => tab.FetchAsync());

    /// <summary>
    ///  Pull, after choosing between merging and rebasing.
    /// </summary>
    /// <remarks>
    ///  The choice is offered rather than defaulted because the two produce different history and the
    ///  right answer depends on the branch: rebasing a shared branch rewrites commits other people
    ///  already have.
    /// </remarks>
    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        RadioButtons strategy = new()
        {
            Header = "When there are new commits on both sides",
            ItemsSource = new[] { "Merge them", "Rebase mine on top", "Refuse unless it fast-forwards" },
            SelectedIndex = 0
        };

        CheckBox prune = new() { Content = "Delete remote-tracking branches that no longer exist" };

        StackPanel panel = new() { Spacing = 14, Width = 400 };
        panel.Children.Add(strategy);
        panel.Children.Add(prune);

        ContentDialog dialog = new()
        {
            Title = $"Pull into {tab.Branch}",
            Content = panel,
            PrimaryButtonText = "Pull",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        PullOptions options = new(
            Rebase: strategy.SelectedIndex == 1,
            Prune: prune.IsChecked == true,
            FastForwardOnly: strategy.SelectedIndex == 2);

        await RunAsync(t => t.PullAsync(options));
    }

    /// <summary>
    ///  Push, with the options a real branch needs.
    /// </summary>
    /// <remarks>
    ///  Force is offered only as <c>--force-with-lease</c>. A bare force discards whatever the remote
    ///  gained since you last fetched; the lease refuses in exactly that case, which is the difference
    ///  between rewriting your own history and destroying someone else's.
    /// </remarks>
    private async void Push_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        CheckBox setUpstream = new()
        {
            Content = "Track this branch on the remote",
            IsChecked = tab.Upstream.Length == 0,
            IsEnabled = tab.Upstream.Length == 0
        };

        CheckBox tags = new() { Content = "Also push tags" };
        CheckBox force = new() { Content = "Force (with lease) — overwrite the remote branch" };

        StackPanel panel = new() { Spacing = 12, Width = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = tab.Upstream.Length > 0
                ? $"Pushing '{tab.Branch}' to {tab.Upstream}."
                : $"Pushing '{tab.Branch}'. It does not track a remote branch yet.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(setUpstream);
        panel.Children.Add(tags);
        panel.Children.Add(force);
        panel.Children.Add(new TextBlock
        {
            Text = "Force-with-lease refuses the push if the remote has moved since your last fetch, "
                + "so it cannot silently discard someone else's commits.",
            Style = (Style)Application.Current.Resources["PageSubtitleStyle"],
            TextWrapping = TextWrapping.Wrap
        });

        ContentDialog dialog = new()
        {
            Title = "Push",
            Content = panel,
            PrimaryButtonText = "Push",
            CloseButtonText = "Cancel",

            // This is the one action here that changes something off this machine.
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        PushOptions options = new(
            Branch: tab.Branch,
            ForceWithLease: force.IsChecked == true,
            SetUpstream: setUpstream.IsChecked == true,
            PushAllTags: tags.IsChecked == true);

        await RunAsync(t => t.PushAsync(options));
    }

    private async void ShowConflicts_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        IReadOnlyList<string> conflicts = await tab.GetConflictedFilesAsync();

        tab.ReportInformation(
            conflicts.Count == 0 ? "No conflicts" : $"{conflicts.Count} conflicted file(s)",
            conflicts.Count == 0
                ? "Nothing is waiting to be resolved."
                : string.Join(Environment.NewLine, conflicts));
    }

    private async void RebaseContinue_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(tab => tab.ContinueOperationAsync("rebase", "continue"));

    private async void RebaseSkip_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(tab => tab.ContinueOperationAsync("rebase", "skip"));

    private async void RebaseAbort_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(tab => tab.ContinueOperationAsync("rebase", "abort"));

    private async void MergeAbort_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(tab => tab.ContinueOperationAsync("merge", "abort"));

    private async void CherryPickContinue_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(tab => tab.ContinueOperationAsync("cherry-pick", "continue"));

    private async void CherryPickAbort_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(tab => tab.ContinueOperationAsync("cherry-pick", "abort"));

    private async void RevertAbort_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(tab => tab.ContinueOperationAsync("revert", "abort"));

    private async Task RunAsync(Func<RepositoryTabViewModel, Task<Services.GitOperationResult>> operation)
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            tab.Report(await operation(tab));
        }
    }
}
