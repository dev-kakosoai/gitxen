using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
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
        nameof(Tab),
        typeof(RepositoryTabViewModel),
        typeof(RepositoryView),
        new PropertyMetadata(null, OnTabPropertyChanged));

    public RepositoryView()
    {
        InitializeComponent();
    }

    public RepositoryTabViewModel? Tab
    {
        get => (RepositoryTabViewModel?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    private static void OnTabPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((RepositoryView)sender).OnTabAssigned();

    /// <summary>Rebuilds the menus that depend on the repository or the machine.</summary>
    private void OnTabAssigned()
    {
        RefreshBranchMenu();
        RefreshExternalTools();
        RestoreSection();
    }

    /// <summary>
    ///  Selects the section this repository was last on.
    /// </summary>
    /// <remarks>
    ///  Driven through the navigation selection rather than by showing the page directly, so the
    ///  pane highlight and the page agree and the section is activated exactly as a click would.
    /// </remarks>
    private void RestoreSection()
    {
        if (Tab is not RepositoryTabViewModel tab || tab.LastSection.Length == 0)
        {
            return;
        }

        NavigationViewItem? item = Navigation.MenuItems
            .Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => (candidate.Tag as string) == tab.LastSection);

        if (item is not null)
        {
            Navigation.SelectedItem = item;
        }
    }

    /// <summary>The section pages, keyed by the Tag on their navigation item.</summary>
    private IReadOnlyDictionary<string, RepositoryPage> Pages => new Dictionary<string, RepositoryPage>
    {
        ["changes"] = ChangesPage,
        ["conflicts"] = ConflictsPage,
        ["history"] = HistoryPage,
        ["reflog"] = ReflogPage,
        ["branches"] = BranchesPage,
        ["remotes"] = RemotesPage,
        ["tags"] = TagsPage,
        ["stashes"] = StashesPage,
        ["submodules"] = SubmodulesPage,
        ["worktrees"] = WorktreesPage,
        ["gitconfig"] = GitConfigPage,
        ["maintenance"] = MaintenancePage,
        ["settings"] = SettingsPage
    };

    /// <summary>
    ///  Rebuilds the branch menu whenever the repository reloads, so it never lists a branch that has
    ///  since been renamed or deleted.
    /// </summary>
    private void RefreshBranchMenu()
    {
        BranchFlyout.Items.Clear();

        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        MenuFlyoutItem create = new() { Text = "New branch…" };
        create.Click += (_, _) => _ = CreateBranchAsync();
        BranchFlyout.Items.Add(create);
        BranchFlyout.Items.Add(new MenuFlyoutSeparator());

        foreach (string branch in tab.Branches)
        {
            MenuFlyoutItem item = new()
            {
                Text = branch,

                // The branch you are on is listed but not offered as a destination.
                IsEnabled = branch != tab.Branch
            };

            string target = branch;
            item.Click += (_, _) => _ = CheckoutAsync(target);
            BranchFlyout.Items.Add(item);
        }
    }

    /// <summary>
    ///  Rebuilds the Open in menu from the tools that are installed.
    /// </summary>
    /// <remarks>
    ///  Built in code rather than declared in XAML because the list depends on what is on the machine:
    ///  offering Visual Studio Code to someone who does not have it only produces a failure on click.
    /// </remarks>
    private void RefreshExternalTools()
    {
        ExternalToolsFlyout.Items.Clear();

        IReadOnlyList<ExternalTool> tools = ExternalTools.Available;

        if (tools.Count == 0)
        {
            ExternalToolsFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = "Nothing found on your PATH",
                IsEnabled = false
            });

            return;
        }

        foreach (ExternalTool tool in tools)
        {
            MenuFlyoutItem item = new()
            {
                Text = tool.Name,
                Icon = new FontIcon { Glyph = tool.Glyph }
            };

            ExternalTool target = tool;
            item.Click += (_, _) => OpenIn(target);
            ExternalToolsFlyout.Items.Add(item);
        }
    }

    private void OpenIn(ExternalTool tool)
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        string error = ExternalTools.Launch(tool, tab.WorkingDir);

        if (error.Length > 0)
        {
            tab.ReportInformation($"Could not open {tool.Name}", error);
        }
    }

    private async Task CreateBranchAsync()
    {
        if (await PromptForTextAsync("New branch", "Branch name", "Create") is string name && name.Length > 0)
        {
            await RunAsync(tab => tab.CreateBranchAsync(name, checkout: true));
            RefreshBranchMenu();
        }
    }

    private async Task CheckoutAsync(string branch)
    {
        await RunAsync(tab => tab.CheckoutBranchAsync(branch));
        RefreshBranchMenu();
    }

    // ---- Hosting service ------------------------------------------------------------------------
    // Everything here opens a browser rather than calling an API. Creating a pull request or reading
    // issues through the API would mean holding a credential, and this front-end has nowhere safe to
    // keep one — the session file is plain JSON. The browser is already signed in.

    private async void HostRepository_Click(object sender, RoutedEventArgs e) =>
        await OpenHostAsync(host => host.BrowseUrl);

    private async void HostBranch_Click(object sender, RoutedEventArgs e) =>
        await OpenHostAsync(host => host.BranchUrl(Tab?.Branch ?? ""));

    private async void HostPullRequests_Click(object sender, RoutedEventArgs e) =>
        await OpenHostAsync(host => host.PullRequestsUrl);

    private async void HostIssues_Click(object sender, RoutedEventArgs e) =>
        await OpenHostAsync(host => host.IssuesUrl);

    /// <summary>
    ///  Opens the host's "open a pull request" page for the current branch.
    /// </summary>
    /// <remarks>
    ///  Warns when the branch has no upstream: the compare page will not find it, and the fix — push
    ///  with "track this branch" — is not obvious from the resulting error on the website.
    /// </remarks>
    private async void HostCreatePullRequest_Click(object sender, RoutedEventArgs e) =>
        await CreatePullRequestAsync();

    private async Task CreatePullRequestAsync()
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        if (tab.Upstream.Length == 0)
        {
            tab.ReportInformation(
                "Branch is not on the remote yet",
                $"'{tab.Branch}' has no upstream, so there is nothing to open a pull request from. "
                    + "Push it first with \"Track this branch on the remote\" ticked.");

            return;
        }

        await OpenHostAsync(host => host.CreatePullRequestUrl(tab.Branch));
    }

    private async Task OpenHostAsync(Func<HostedRepository, string> buildUrl)
    {
        if (Tab?.Host is not HostedRepository host)
        {
            return;
        }

        await Windows.System.Launcher.LaunchUriAsync(new Uri(buildUrl(host)));
    }

    private void Palette_Click(object sender, RoutedEventArgs e) => _ = ShowPaletteAsync();

    /// <summary>
    ///  Opens the command palette, built from what this repository currently offers.
    /// </summary>
    /// <remarks>
    ///  The chosen command runs after the dialog has closed rather than inside it: several commands
    ///  open a dialog of their own, and WinUI permits only one at a time.
    /// </remarks>
    public async Task ShowPaletteAsync()
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        CommandPalette palette = new(BuildCommands(tab)) { XamlRoot = XamlRoot };
        await palette.ShowAsync();

        if (palette.Chosen is PaletteCommand chosen)
        {
            await chosen.Invoke();
        }
    }

    private IReadOnlyList<PaletteCommand> BuildCommands(RepositoryTabViewModel tab)
    {
        List<PaletteCommand> commands = [];

        foreach ((string tag, RepositoryPage page) in Pages)
        {
            string title = Navigation.MenuItems
                .Concat(Navigation.FooterMenuItems)
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => (item.Tag as string) == tag)?
                .Content?.ToString() ?? tag;

            commands.Add(new PaletteCommand(title, "Go to", "", () => NavigateAsync(tag, page)));
        }

        commands.Add(new PaletteCommand("Fetch", "Repository", "", () => RunAsync(t => t.FetchAsync())));
        commands.Add(new PaletteCommand("Pull", "Repository", "choose merge or rebase", PullAsync));
        commands.Add(new PaletteCommand("Push", "Repository", "", PushAsync));
        commands.Add(new PaletteCommand("Refresh", "Repository", "", () => tab.LoadAsync()));
        commands.Add(new PaletteCommand("New branch", "Repository", "", CreateBranchAsync));
        commands.Add(new PaletteCommand("Undo last commit", "Repository", "keeps the changes staged", UndoLastCommitAsync));
        commands.Add(new PaletteCommand("Stop comparing commits", "Repository", "", () =>
        {
            tab.StopComparing();
            return Task.CompletedTask;
        }));

        if (tab.Host is HostedRepository host)
        {
            commands.Add(new PaletteCommand($"Open {host.Owner}/{host.Name} in browser", "Host", host.Host,
                () => OpenHostAsync(_ => host.BrowseUrl)));
            commands.Add(new PaletteCommand("Create pull request", "Host", "opens the compare page",
                CreatePullRequestAsync));
            commands.Add(new PaletteCommand("Open pull requests", "Host", "",
                () => OpenHostAsync(_ => host.PullRequestsUrl)));
            commands.Add(new PaletteCommand("Open issues", "Host", "", () => OpenHostAsync(_ => host.IssuesUrl)));
        }

        foreach (ExternalTool tool in ExternalTools.Available)
        {
            ExternalTool target = tool;
            commands.Add(new PaletteCommand($"Open in {tool.Name}", "Open with", "", () =>
            {
                OpenIn(target);
                return Task.CompletedTask;
            }));
        }

        foreach (string branch in tab.Branches.Where(branch => branch != tab.Branch))
        {
            string target = branch;
            commands.Add(new PaletteCommand($"Check out {branch}", "Branch", "", () => CheckoutAsync(target)));
        }

        return commands;
    }

    private async Task NavigateAsync(string tag, RepositoryPage page)
    {
        NavigationViewItem? item = Navigation.MenuItems
            .Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => (candidate.Tag as string) == tag);

        if (item is not null)
        {
            // Setting the selection drives the same handler a click would, so the page is shown and
            // activated by one route rather than two.
            Navigation.SelectedItem = item;
            return;
        }

        await page.ActivateAsync();
    }

    private async Task UndoLastCommitAsync()
    {
        if (await ConfirmTextAsync(
            "Undo the last commit?",
            "The commit is removed and its changes go back to being staged, ready to commit again. "
                + "Nothing is lost — the original is still in the reflog.",
            "Undo"))
        {
            await RunAsync(tab => tab.UndoLastCommitAsync());
        }
    }

    private async Task<string?> PromptForTextAsync(string title, string label, string acceptText)
    {
        TextBox input = new() { Header = label, MinWidth = 320 };

        ContentDialog dialog = new()
        {
            Title = title,
            Content = input,
            PrimaryButtonText = acceptText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }

    private async Task<bool> ConfirmTextAsync(string title, string message, string acceptText)
    {
        ContentDialog dialog = new()
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = acceptText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

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

        // Remembered so the repository reopens on the section it was left on.
        if (Tab is RepositoryTabViewModel current)
        {
            current.LastSection = tag;
        }

        // Listings are read on arrival rather than up front, so opening a repository does not pay for
        // six git calls the user may never look at.
        await selected.ActivateAsync();
    }

    private async void OperationContinue_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(tab => tab.ResolveOperationAsync("continue"));

    private async void OperationSkip_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(tab => tab.ResolveOperationAsync("skip"));

    /// <summary>Aborting returns the repository to where the operation started, so it is confirmed.</summary>
    private async void OperationAbort_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        if (await ConfirmTextAsync(
            $"Abort the {tab.Operation.CommandName}?",
            "This returns the repository to where the operation started. Work you have already "
                + "committed as part of it is discarded.",
            "Abort"))
        {
            await RunAsync(t => t.ResolveOperationAsync("abort"));
        }
    }

    /// <summary>Takes the user to the page that fixes the missing identity.</summary>
    private async void OpenGitConfig_Click(object sender, RoutedEventArgs e) =>
        await NavigateAsync("gitconfig", GitConfigPage);
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
    private async void Pull_Click(object sender, RoutedEventArgs e) => await PullAsync();

    private async Task PullAsync()
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
    private async void Push_Click(object sender, RoutedEventArgs e) => await PushAsync();

    private async Task PushAsync()
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

    /// <summary>Takes you to the page that can actually resolve them.</summary>
    private async void ShowConflicts_Click(object sender, RoutedEventArgs e) =>
        await NavigateAsync("conflicts", ConflictsPage);

    private async Task RunAsync(Func<RepositoryTabViewModel, Task<Services.GitOperationResult>> operation)
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            tab.Report(await operation(tab));
        }
    }
}
