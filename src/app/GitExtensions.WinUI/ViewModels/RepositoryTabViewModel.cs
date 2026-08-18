using System.Collections.ObjectModel;
using System.Globalization;
using GitCommands;
using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Graph;
using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
using GitUIPluginInterfaces;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.ViewModels;

/// <summary>
///  One open repository. Each tab owns its own state so tabs stay fully independent, mirroring how
///  the WinForms app gives every tab its own IGitUICommands/GitModule.
/// </summary>
public sealed class RepositoryTabViewModel : ShellTab
{
    /// <summary>
    ///  How many commits to read per page, and how many diff lines to render. Configurable in
    ///  Settings; capped at all because this repository alone has ~17,000 commits, which is more
    ///  than an unvirtualized bound collection should hold.
    /// </summary>
    private static int MaxCommits => AppOptions.MaxCommits;

    /// <summary>
    ///  Every loaded commit. <see cref="Commits"/> is the filtered projection actually bound to the
    ///  list, so filtering never discards rows we would have to re-read from git.
    /// </summary>
    private readonly List<CommitRowViewModel> _allCommits = [];

    private readonly RepositoryLoader _loader;
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>Rebuilt from scratch on a full reload; kept across pages so lanes stay continuous.</summary>
    private CommitGraphBuilder _graphBuilder = new();

    /// <summary>
    ///  Refs keyed by full commit hash, read once per reload so attaching badges to a streamed row is
    ///  a dictionary lookup rather than a git call.
    /// </summary>
    private IReadOnlyDictionary<string, List<RefBadge>> _refsByCommit = new Dictionary<string, List<RefBadge>>();

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _changedFilesCts;
    private UiMode _mode;
    private string _branch = "";
    private string _upstream = "";
    private int _aheadCount;
    private int _behindCount;
    private int _pendingCount;
    private string _status = "";
    private string _resultTitle = "";
    private string _resultMessage = "";
    private InfoBarSeverity _resultSeverity = InfoBarSeverity.Informational;
    private bool _isResultOpen;
    private string _filter = "";
    private RevisionQuery _query = RevisionQuery.Default;
    private bool _isLoading;
    private bool _hasMoreCommits;
    private bool _isBusy;
    private string? _selectedBranch;
    private bool _suppressBranchCheckout;
    private RepositoryOperation _operation = RepositoryOperation.None;
    private bool _needsIdentity;
    private HostedRepository? _host;
    private CommitRowViewModel? _selectedCommit;
    private CommitRowViewModel? _compareTarget;
    private ChangedFileViewModel? _selectedChangedFile;

    public RepositoryTabViewModel(IGitExecutorProvider executorProvider, string workingDir, UiMode mode)
    {
        _loader = new RepositoryLoader(executorProvider, workingDir);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _mode = mode;
        WorkingDir = workingDir;
        Title = GetRepositoryName(workingDir);
        Changes = new WorkingDirectoryViewModel(_loader, LoadAsync);
    }

    public string WorkingDir { get; }

    public override string Title { get; }

    /// <summary>The working directory, which is what distinguishes two tabs with the same folder name.</summary>
    public override string Description => WorkingDir;

    public override bool IsClosable => true;

    public override bool IsHome => false;

    /// <summary>The commits currently shown — <see cref="_allCommits"/> passed through the filter.</summary>
    public ObservableCollection<CommitRowViewModel> Commits { get; } = [];

    /// <summary>Files touched by the selected commit, shown alongside the history.</summary>
    public ObservableCollection<ChangedFileViewModel> ChangedFiles { get; } = [];

    /// <summary>The History page's diff panel. The Changes page owns a separate one.</summary>
    public DiffViewModel HistoryDiff { get; } = new();

    /// <summary>Staging and committing, which the Changes page drives.</summary>
    public WorkingDirectoryViewModel Changes { get; }

    // ---- Repository object pages ----------------------------------------------------------------
    // Each page loads its own collection on first navigation and on refresh, rather than every tab
    // paying for every listing up front.

    public ObservableCollection<BranchInfo> BranchDetails { get; } = [];

    public ObservableCollection<RemoteInfo> Remotes { get; } = [];

    public ObservableCollection<TagInfo> Tags { get; } = [];

    public ObservableCollection<StashInfo> Stashes { get; } = [];

    public ObservableCollection<SubmoduleInfo> Submodules { get; } = [];

    public ObservableCollection<WorktreeInfo> Worktrees { get; } = [];

    public ObservableCollection<RemoteBranchInfo> RemoteBranches { get; } = [];

    public ObservableCollection<ReflogEntry> Reflog { get; } = [];

    /// <summary>Files with unresolved merge conflicts, and the shape of each conflict.</summary>
    public ObservableCollection<ConflictedFile> Conflicts { get; } = [];

    /// <summary>Drives the Conflicts navigation badge, so the count is visible without going there.</summary>
    public int ConflictCount => Conflicts.Count;

    public Visibility ConflictVisibility => Conflicts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<GitConfigEntry> LocalConfig { get; } = [];

    public ObservableCollection<GitConfigEntry> GlobalConfig { get; } = [];

    /// <summary>The paused merge, rebase, cherry-pick, revert or bisect, if there is one.</summary>
    public RepositoryOperation Operation
    {
        get => _operation;
        private set
        {
            if (SetProperty(ref _operation, value))
            {
                OnPropertyChanged(nameof(HasOperation));
            }
        }
    }

    public bool HasOperation => Operation.IsInProgress;

    /// <summary>
    ///  Set when no committer identity is configured, which makes every commit fail with an error
    ///  that does not say where to fix it.
    /// </summary>
    public bool NeedsIdentity
    {
        get => _needsIdentity;
        private set
        {
            if (SetProperty(ref _needsIdentity, value))
            {
                OnPropertyChanged(nameof(IdentityWarningVisibility));
            }
        }
    }

    public Visibility IdentityWarningVisibility => NeedsIdentity ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    ///  Where this repository lives online, from its remote URL, or null when it has no remote or the
    ///  remote is not a recognisable hosting service.
    /// </summary>
    public HostedRepository? Host
    {
        get => _host;
        private set
        {
            if (SetProperty(ref _host, value))
            {
                OnPropertyChanged(nameof(HasHost));
                OnPropertyChanged(nameof(HostVisibility));
                OnPropertyChanged(nameof(HostName));
            }
        }
    }

    public bool HasHost => Host is not null;

    /// <summary>The hosting actions are hidden entirely rather than shown disabled when there is no remote.</summary>
    public Visibility HostVisibility => HasHost ? Visibility.Visible : Visibility.Collapsed;

    public string HostName => Host is null ? "" : $"{Host.Owner}/{Host.Name}";

    /// <summary>Local branch names, for the pickers that just need a name.</summary>
    public ObservableCollection<string> Branches { get; } = [];

    /// <summary>
    ///  Width of the graph column. Duplicated from the row view model because the column header
    ///  spacer binds against this tab, not against a row.
    /// </summary>
    public double GraphColumnWidth => CommitGraphBuilder.ColumnWidth;

    /// <summary>
    ///  Free-text filter over the commits already loaded — an instant narrowing of what is on screen,
    ///  not a search of the repository. <see cref="Query"/> is what asks git to search.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>
    ///  What git is asked to walk: the scope, and any message/author/content/path search.
    /// </summary>
    /// <remarks>
    ///  Assigning this reloads, because these are filters git applies while walking history — unlike
    ///  <see cref="Filter"/>, they can find commits that were never loaded.
    /// </remarks>
    public RevisionQuery Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
            {
                OnPropertyChanged(nameof(IsShowingAllBranches));
                OnPropertyChanged(nameof(QueryVisibility));
                OnPropertyChanged(nameof(QueryDescription));
                _ = LoadAsync();
            }
        }
    }

    public bool IsShowingAllBranches => Query.Scope == RevisionScope.AllBranches;

    /// <summary>Shown while git-side filters are narrowing the log, so an empty list is explicable.</summary>
    public Visibility QueryVisibility => Query.HasFilters ? Visibility.Visible : Visibility.Collapsed;

    public string QueryDescription
    {
        get
        {
            List<string> parts = [];

            if (!string.IsNullOrWhiteSpace(Query.MessageContains))
            {
                parts.Add($"message contains \"{Query.MessageContains}\"");
            }

            if (!string.IsNullOrWhiteSpace(Query.Author))
            {
                parts.Add($"author \"{Query.Author}\"");
            }

            if (!string.IsNullOrWhiteSpace(Query.ContainingText))
            {
                parts.Add($"changes to \"{Query.ContainingText}\"");
            }

            if (!string.IsNullOrWhiteSpace(Query.Path))
            {
                parts.Add($"path \"{Query.Path}\"");
            }

            return parts.Count == 0 ? "" : $"Searching: {string.Join(", ", parts)}";
        }
    }

    public UiMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(DetailsVisibility));
                OnPropertyChanged(nameof(ChromeVisibility));
                OnPropertyChanged(nameof(SecondaryColumnVisibility));
                OnPropertyChanged(nameof(LoadMoreVisibility));
                OnPropertyChanged(nameof(AdvancedVisibility));
                OnPropertyChanged(nameof(IsPaneOpen));
            }
        }
    }

    public string Branch
    {
        get => _branch;
        private set
        {
            if (SetProperty(ref _branch, value))
            {
                OnPropertyChanged(nameof(BranchHeader));
            }
        }
    }

    /// <summary>The upstream the current branch tracks, empty when it tracks nothing.</summary>
    public string Upstream
    {
        get => _upstream;
        private set
        {
            if (SetProperty(ref _upstream, value))
            {
                OnPropertyChanged(nameof(UpstreamVisibility));
            }
        }
    }

    public Visibility UpstreamVisibility => Upstream.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Commits on the current branch that the upstream does not have.</summary>
    public int AheadCount
    {
        get => _aheadCount;
        private set
        {
            if (SetProperty(ref _aheadCount, value))
            {
                OnPropertyChanged(nameof(AheadVisibility));
            }
        }
    }

    public int BehindCount
    {
        get => _behindCount;
        private set
        {
            if (SetProperty(ref _behindCount, value))
            {
                OnPropertyChanged(nameof(BehindVisibility));
            }
        }
    }

    public Visibility AheadVisibility => AheadCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BehindVisibility => BehindCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Uncommitted change count, shown as a badge on the Changes navigation item.</summary>
    public int PendingCount
    {
        get => _pendingCount;
        private set
        {
            if (SetProperty(ref _pendingCount, value))
            {
                OnPropertyChanged(nameof(PendingVisibility));
            }
        }
    }

    public Visibility PendingVisibility => PendingCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>"main" or "main ↑2 ↓1" — the title-bar summary of where the repository stands.</summary>
    public string BranchHeader => Branch.Length == 0 ? "(no branch)" : Branch;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    // ---- Operation reporting --------------------------------------------------------------------
    // Results land in an InfoBar on the repository view rather than a modal dialog: a fetch that
    // found nothing should not need dismissing before the next action, and the output of the last
    // operation is worth leaving on screen while you decide what to do about it.

    public string ResultTitle
    {
        get => _resultTitle;
        private set => SetProperty(ref _resultTitle, value);
    }

    public string ResultMessage
    {
        get => _resultMessage;
        private set => SetProperty(ref _resultMessage, value);
    }

    public InfoBarSeverity ResultSeverity
    {
        get => _resultSeverity;
        private set => SetProperty(ref _resultSeverity, value);
    }

    /// <summary>Two-way with the InfoBar so dismissing it closes it for good rather than reopening.</summary>
    public bool IsResultOpen
    {
        get => _isResultOpen;
        set => SetProperty(ref _isResultOpen, value);
    }

    /// <summary>
    ///  Shows the outcome of a git operation. Success with no output closes the bar instead of
    ///  announcing nothing — the refreshed list is its own confirmation.
    /// </summary>
    public void Report(GitOperationResult result)
    {
        bool hasOutput = !string.IsNullOrWhiteSpace(result.Output);

        if (result.Succeeded && !hasOutput)
        {
            IsResultOpen = false;
            return;
        }

        ResultTitle = result.Succeeded ? result.Description : $"{result.Description} failed";
        ResultMessage = hasOutput ? result.Output.Trim() : "git reported a failure with no output.";
        ResultSeverity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        IsResultOpen = true;
    }

    /// <summary>Reports something the front-end itself decided, with no git command behind it.</summary>
    public void ReportInformation(string title, string message)
    {
        ResultTitle = title;
        ResultMessage = message;
        ResultSeverity = InfoBarSeverity.Informational;
        IsResultOpen = true;
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>True while a git operation (fetch/pull/push/checkout/commit) is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    ///  Bound to the branch picker. Assigning it from the UI checks that branch out; assignments made
    ///  while refreshing our own state are suppressed so they don't trigger a spurious checkout.
    /// </summary>
    public string? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (!SetProperty(ref _selectedBranch, value))
            {
                return;
            }

            if (!_suppressBranchCheckout && value is not null && value != Branch)
            {
                _ = CheckoutAsync(value);
            }
        }
    }

    public CommitRowViewModel? SelectedCommit
    {
        get => _selectedCommit;
        set
        {
            if (SetProperty(ref _selectedCommit, value))
            {
                OnPropertyChanged(nameof(HasSelectedCommit));
                OnPropertyChanged(nameof(SelectedCommitVisibility));
                _ = LoadChangedFilesAsync(value);
            }
        }
    }

    /// <summary>
    ///  A second commit to compare <see cref="SelectedCommit"/> against.
    /// </summary>
    /// <remarks>
    ///  When set, the changed-files pane and the diff panel show the difference between the two
    ///  commits rather than what the selected one changed. Reusing those two controls is why
    ///  comparing needs no separate page: the question "what is different" has the same shape whether
    ///  the answer comes from one commit or two.
    /// </remarks>
    public CommitRowViewModel? CompareTarget
    {
        get => _compareTarget;
        set
        {
            if (SetProperty(ref _compareTarget, value))
            {
                OnPropertyChanged(nameof(IsComparing));
                OnPropertyChanged(nameof(CompareVisibility));
                OnPropertyChanged(nameof(CompareDescription));
                _ = LoadChangedFilesAsync(SelectedCommit);
            }
        }
    }

    public bool IsComparing => CompareTarget is not null && SelectedCommit is not null;

    public Visibility CompareVisibility => IsComparing ? Visibility.Visible : Visibility.Collapsed;

    public string CompareDescription =>
        IsComparing ? $"Comparing {CompareTarget!.ShortHash} → {SelectedCommit!.ShortHash}" : "";

    /// <summary>Drops back to showing what the selected commit itself changed.</summary>
    public void StopComparing() => CompareTarget = null;

    public bool HasSelectedCommit => SelectedCommit is not null;

    /// <summary>Collapses the details pane's contents rather than showing empty fields.</summary>
    public Visibility SelectedCommitVisibility => HasSelectedCommit ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Selecting a changed file loads its diff into the History page's diff panel.</summary>
    public ChangedFileViewModel? SelectedChangedFile
    {
        get => _selectedChangedFile;
        set
        {
            if (SetProperty(ref _selectedChangedFile, value))
            {
                _ = LoadHistoryDiffAsync(value);
            }
        }
    }

    /// <summary>The commit cap was hit, so there is more history to page in.</summary>
    public bool HasMoreCommits
    {
        get => _hasMoreCommits;
        private set
        {
            if (SetProperty(ref _hasMoreCommits, value))
            {
                OnPropertyChanged(nameof(LoadMoreVisibility));
            }
        }
    }

    /// <summary>Commit-details pane: Advanced only.</summary>
    public Visibility DetailsVisibility => Mode == UiMode.Advanced ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Anything Advanced-only that isn't the details pane (filter box, extra columns).</summary>
    public Visibility AdvancedVisibility => Mode == UiMode.Advanced ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Column headers, branch/path line: everything except Zen.</summary>
    public Visibility ChromeVisibility => Mode == UiMode.Zen ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Author and date columns — dropped in Zen so only the message remains.</summary>
    public Visibility SecondaryColumnVisibility => Mode == UiMode.Zen ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Zen collapses the navigation pane to nothing; the other modes leave it open.</summary>
    public bool IsPaneOpen => Mode != UiMode.Zen;

    /// <summary>
    ///  Zen drops the pane to its minimal form so the commit list is all that is left; every other
    ///  mode shows the full left pane.
    /// </summary>
    public NavigationViewPaneDisplayMode PaneDisplayMode =>
        Mode == UiMode.Zen ? NavigationViewPaneDisplayMode.LeftMinimal : NavigationViewPaneDisplayMode.Left;

    public Visibility LoadMoreVisibility =>
        HasMoreCommits && Mode != UiMode.Zen ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    ///  Reloads the commit list, streaming rows in as <c>git log</c> produces them so the window stays
    ///  responsive on large repositories rather than blocking until the whole history is read.
    /// </summary>
    public Task LoadAsync() => LoadPageAsync(append: false);

    /// <summary>Appends the next page of older commits.</summary>
    public Task LoadMoreAsync() => LoadPageAsync(append: true);

    // ---- Object page loads ----------------------------------------------------------------------

    public Task LoadBranchesAsync() => ReplaceAsync(BranchDetails, _loader.GetBranches);

    public Task LoadRemoteBranchesAsync() => ReplaceAsync(RemoteBranches, _loader.GetRemoteBranches);

    public Task LoadLocalConfigAsync() =>
        ReplaceAsync(LocalConfig, () => _loader.GetConfiguration(GitConfigScope.Local));

    public Task LoadGlobalConfigAsync() =>
        ReplaceAsync(GlobalConfig, () => _loader.GetConfiguration(GitConfigScope.Global));

    public Task<string> GetConfigValueAsync(GitConfigScope scope, string key) =>
        Task.Run(() => _loader.GetConfigValue(scope, key));

    /// <summary>
    ///  Writes a setting. Unlike the git operations, this does not reload the commit list — nothing
    ///  about history changes when a name or a line-ending rule does.
    /// </summary>
    public Task<GitOperationResult> SetConfigValueAsync(GitConfigScope scope, string key, string value) =>
        Task.Run(() => _loader.SetConfigValue(scope, key, value));

    /// <summary>Capped: the reflog is a recovery aid, not a history to page through.</summary>
    public Task LoadReflogAsync() => ReplaceAsync(Reflog, () => _loader.GetReflog(maxCount: 200));

    public Task LoadRemotesAsync() => ReplaceAsync(Remotes, _loader.GetRemotes);

    public Task LoadTagsAsync() => ReplaceAsync(Tags, _loader.GetTags);

    public Task LoadStashesAsync() => ReplaceAsync(Stashes, _loader.GetStashes);

    public Task LoadSubmodulesAsync() => ReplaceAsync(Submodules, _loader.GetSubmodules);

    public Task LoadWorktreesAsync() => ReplaceAsync(Worktrees, _loader.GetWorktrees);

    public Task<GitOperationResult> FetchAsync() => RunOperationAsync(loader => loader.Fetch());

    public Task<GitOperationResult> PullAsync() => RunOperationAsync(loader => loader.Pull());

    public Task<GitOperationResult> PushAsync()
    {
        string branch = Branch;
        return RunOperationAsync(loader => loader.Push(branch));
    }

    public Task<GitOperationResult> CommitAllAsync(string message) =>
        RunOperationAsync(loader => loader.CommitAll(message));

    /// <summary>Undoes the last commit, keeping its changes staged so they can be recommitted.</summary>
    public Task<GitOperationResult> UndoLastCommitAsync() =>
        RunOperationAsync(loader => loader.UndoLastCommit());

    public Task<string> GetLastCommitMessageAsync() => Task.Run(_loader.GetLastCommitMessage);

    public Task<GitOperationResult> CheckoutBranchAsync(string branch) =>
        RunOperationAsync(loader => loader.Checkout(branch));

    public Task<GitOperationResult> CheckoutDetachedAsync(string reference) =>
        RunOperationAsync(loader => loader.CheckoutDetached(reference));

    public Task<GitOperationResult> CreateBranchAsync(string branchName, bool checkout)
    {
        // Branch from the selected commit when there is one, otherwise from HEAD.
        if (SelectedCommit?.Revision?.ObjectId is not ObjectId objectId)
        {
            return RunOperationAsync(loader => loader.CreateBranchFromHead(branchName, checkout));
        }

        return RunOperationAsync(loader => loader.CreateBranch(branchName, objectId, checkout));
    }

    /// <summary>Creates a branch at any resolvable reference — a reflog selector, a remote branch.</summary>
    public Task<GitOperationResult> CreateBranchAtAsync(string branchName, string reference, bool checkout) =>
        RunOperationAsync(loader => loader.CreateBranchAt(branchName, reference, checkout));

    public Task<GitOperationResult> ResetToAsync(string reference, ResetMode mode) =>
        RunOperationAsync(loader => loader.ResetTo(reference, mode));

    public Task<GitOperationResult> DeleteBranchAsync(string branchName, bool force) =>
        RunOperationAsync(loader => loader.DeleteBranch(branchName, force));

    public Task<GitOperationResult> RenameBranchAsync(string oldName, string newName) =>
        RunOperationAsync(loader => loader.RenameBranch(oldName, newName));

    public Task<GitOperationResult> SetUpstreamAsync(string branch, string upstream) =>
        RunOperationAsync(loader => loader.SetUpstream(branch, upstream));

    public Task<GitOperationResult> MergeBranchAsync(string branchName) =>
        RunOperationAsync(loader => loader.MergeBranch(branchName));

    public Task<GitOperationResult> MergeBranchAsync(string branchName, MergeOptions options) =>
        RunOperationAsync(loader => loader.MergeBranch(branchName, options));

    public Task<GitOperationResult> PushAsync(PushOptions options) =>
        RunOperationAsync(loader => loader.Push(options));

    public Task<GitOperationResult> PullAsync(PullOptions options) =>
        RunOperationAsync(loader => loader.Pull(options));

    public Task<GitOperationResult> CheckoutBranchAsync(string branch, LocalChangesAction localChanges) =>
        RunOperationAsync(loader => loader.Checkout(branch, localChanges));

    public Task<GitOperationResult> DeleteRemoteBranchAsync(string remote, string branch) =>
        RunOperationAsync(loader => loader.DeleteRemoteBranch(remote, branch));

    public Task<GitOperationResult> CleanAsync(bool includeDirectories, bool includeIgnored, bool dryRun) =>
        RunOperationAsync(loader => loader.Clean(includeDirectories, includeIgnored, dryRun));

    public Task<GitOperationResult> CollectGarbageAsync() =>
        RunOperationAsync(loader => loader.CollectGarbage());

    public Task<GitOperationResult> ArchiveAsync(string reference, string outputPath) =>
        RunOperationAsync(loader => loader.Archive(reference, outputPath));

    public Task<string> GetStashDiffAsync(string reference) =>
        Task.Run(() => _loader.GetStashDiff(reference));

    public Task<GitOperationResult> StashSaveAsync(string message) =>
        RunOperationAsync(loader => loader.StashSave(message));

    public Task<GitOperationResult> StashSaveAsync(string message, bool includeUntracked, bool keepIndex) =>
        RunOperationAsync(loader => loader.StashSave(message, includeUntracked, keepIndex));

    public Task<GitOperationResult> StashPopAsync() =>
        RunOperationAsync(loader => loader.StashPop());

    public Task<GitOperationResult> StashApplyAsync(string reference) =>
        RunOperationAsync(loader => loader.StashApply(reference));

    public Task<GitOperationResult> StashPopAsync(string reference) =>
        RunOperationAsync(loader => loader.StashPop(reference));

    public Task<GitOperationResult> StashDropAsync(string reference) =>
        RunOperationAsync(loader => loader.StashDrop(reference));

    /// <summary>
    ///  Operations that act on the selected commit. They report rather than throw when nothing is
    ///  selected, so the menu items never need to be disabled.
    /// </summary>
    public Task<GitOperationResult> CherryPickAsync() =>
        RunOnSelectedCommitAsync("Cherry-pick", (loader, commit) => loader.CherryPick(commit));

    public Task<GitOperationResult> RevertAsync() =>
        RunOnSelectedCommitAsync("Revert", (loader, commit) => loader.Revert(commit));

    public Task<GitOperationResult> ResetToSelectedAsync(ResetMode mode) =>
        RunOnSelectedCommitAsync($"Reset ({mode})", (loader, commit) => loader.ResetTo(commit, mode));

    public Task<GitOperationResult> BisectAtSelectedAsync(string action) =>
        RunOnSelectedCommitAsync($"Bisect {action}", (loader, commit) => loader.Bisect(action, commit));

    public Task<GitOperationResult> RebaseAsync(string onto) =>
        RunOperationAsync(loader => loader.Rebase(onto));

    public Task<GitOperationResult> ContinueOperationAsync(string operation, string action) =>
        RunOperationAsync(loader => loader.ContinueOperation(operation, action));

    public Task<GitOperationResult> BisectAsync(string action) =>
        RunOperationAsync(loader => loader.Bisect(action));

    /// <summary>Re-reads the conflicts and refreshes the count the navigation badge shows.</summary>
    public async Task LoadConflictsAsync()
    {
        await ReplaceAsync(Conflicts, _loader.GetConflicts);

        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(ConflictVisibility));
    }

    /// <summary>
    ///  Resolves one conflict by taking a side. Reloads afterwards, since resolving the last one
    ///  changes what the operation banner should say.
    /// </summary>
    public Task<GitOperationResult> TakeSideAsync(ConflictedFile file, bool ours) =>
        RunOperationAsync(loader => loader.TakeSide(file, ours));

    public Task<GitOperationResult> TakeSideForAllAsync(bool ours) =>
        RunOperationAsync(loader => loader.TakeSideForAll(ours));

    /// <summary>Stages a file the user resolved by hand, which is what git means by resolved.</summary>
    public Task<GitOperationResult> MarkConflictResolvedAsync(string path) =>
        RunOperationAsync(loader => loader.MarkResolved(path));

    /// <summary>Runs the merge tool. Blocks until the tool closes, so it is kept off the UI thread.</summary>
    public Task<GitOperationResult> LaunchMergeToolAsync(string path) =>
        RunOperationAsync(loader => loader.LaunchMergeTool(path));

    public Task<string> ReadConflictedTextAsync(string path) =>
        Task.Run(() => _loader.ReadConflictedText(path));

    public Task<IReadOnlyList<string>> GetConflictedFilesAsync() =>
        Task.Run(_loader.GetConflictedFiles);

    public Task<GitOperationResult> MarkResolvedAsync(string fileName) =>
        RunOperationAsync(loader => loader.MarkResolved(fileName));

    public Task<GitOperationResult> CreateTagAsync(string name, string message) =>
        RunOperationAsync(loader => loader.CreateTag(name, SelectedCommit?.Revision?.ObjectId, message));

    public Task<GitOperationResult> DeleteTagAsync(string name) =>
        RunOperationAsync(loader => loader.DeleteTag(name));

    public Task<GitOperationResult> PushTagAsync(string name) =>
        RunOperationAsync(loader => loader.PushTag(name));

    public Task<GitOperationResult> AddRemoteAsync(string name, string url) =>
        RunOperationAsync(loader => loader.AddRemote(name, url));

    public Task<GitOperationResult> RemoveRemoteAsync(string name) =>
        RunOperationAsync(loader => loader.RemoveRemote(name));

    public Task<GitOperationResult> FetchRemoteAsync(string remote, bool prune) =>
        RunOperationAsync(loader => loader.FetchRemote(remote, prune));

    public Task<GitOperationResult> UpdateSubmodulesAsync() =>
        RunOperationAsync(loader => loader.UpdateSubmodules());

    public Task<GitOperationResult> UpdateSubmoduleAsync(string path) =>
        RunOperationAsync(loader => loader.UpdateSubmodule(path));

    public Task<GitOperationResult> SyncSubmodulesAsync() =>
        RunOperationAsync(loader => loader.SyncSubmodules());

    public Task<GitOperationResult> AddWorktreeAsync(string path, string branch) =>
        RunOperationAsync(loader => loader.AddWorktree(path, branch));

    public Task<GitOperationResult> RemoveWorktreeAsync(string path) =>
        RunOperationAsync(loader => loader.RemoveWorktree(path));

    public Task<GitOperationResult> PruneWorktreesAsync() =>
        RunOperationAsync(loader => loader.PruneWorktrees());

    /// <summary>Raw <c>git blame</c> output for the selected file at the selected commit.</summary>
    public Task<string> GetBlameAsync(string fileName)
    {
        ObjectId? revision = SelectedCommit?.Revision?.ObjectId;
        return Task.Run(() => _loader.GetBlame(fileName, revision));
    }

    /// <summary>The commits that touched one file, as display rows.</summary>
    public Task<IReadOnlyList<CommitRowViewModel>> GetFileHistoryAsync(string fileName) =>
        Task.Run<IReadOnlyList<CommitRowViewModel>>(
            () => _loader.GetFileHistory(fileName, maxCount: 200, CancellationToken.None)
                .Select(revision => new CommitRowViewModel(revision))
                .ToList());

    /// <summary>Names of the files that a commit would include, for confirmation before committing.</summary>
    public Task<IReadOnlyList<string>> GetPendingChangesAsync() =>
        Task.Run<IReadOnlyList<string>>(() => _loader.GetWorkingDirectoryChanges().Select(file => file.Name).ToList());

    public void Close()
    {
        _loadCts?.Cancel();
        _changedFilesCts?.Cancel();
        HistoryDiff.Clear();
        Changes.Diff.Clear();
    }

    private async Task LoadPageAsync(bool append)
    {
        Cancel(ref _loadCts);
        CancellationTokenSource cts = new();
        _loadCts = cts;

        // The working-directory row isn't part of the history, so it never counts towards paging.
        int loadedCommits = _allCommits.Count(row => !row.IsWorkingDirectory);
        int skip = append ? loadedCommits : 0;

        IsLoading = true;
        Status = "";
        HasMoreCommits = false;

        if (!append)
        {
            _allCommits.Clear();
            Commits.Clear();
            ChangedFiles.Clear();

            // Lanes are only meaningful relative to the commits already placed, so a full reload
            // starts the graph over.
            _graphBuilder = new CommitGraphBuilder();
        }

        try
        {
            // Read the branch and refs first: once the commit cap is hit the stream is cancelled,
            // which would otherwise abort these too.
            if (!append)
            {
                Branch = await Task.Run(_loader.GetCurrentBranch, cts.Token);
                _refsByCommit = await Task.Run(_loader.GetRefsByCommit, cts.Token);
                await ResolveHostAsync(cts.Token);
                await RefreshRepositoryStateAsync(cts.Token);
                await RefreshBranchesAsync(cts.Token);
                await AddWorkingDirectoryRowAsync(cts.Token);
            }

            // The completion callback is enqueued behind every batch the observer dispatched, so by
            // the time it runs the collection is fully populated — the DispatcherQueue is FIFO.
            StreamingObserver observer = new(_dispatcherQueue, cts.Token, batch => AddCommitBatch(batch, skip + MaxCommits), OnStreamFinished);

            RevisionQuery query = Query;
            await Task.Run(() => _loader.StreamRevisions(observer, skip, query, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load, or the tab closed.
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
                _loadCts = null;
            }
        }
    }

    /// <summary>Adds a batch on the UI thread. Returns false once the page is full.</summary>
    private bool AddCommitBatch(IReadOnlyList<GitRevision> batch, int maxCommits)
    {
        foreach (GitRevision revision in batch)
        {
            if (_allCommits.Count(row => !row.IsWorkingDirectory) >= maxCommits)
            {
                return false;
            }

            CommitRowViewModel row = new(revision)
            {
                GraphSegments = _graphBuilder.AddCommit(revision),
                Refs = _refsByCommit.TryGetValue(revision.ObjectId.ToString(), out List<RefBadge>? refs) ? refs : []
            };

            _allCommits.Add(row);

            if (PassesFilter(row))
            {
                Commits.Add(row);
            }
        }

        return true;
    }

    private async Task AddWorkingDirectoryRowAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> pending = await Task.Run<IReadOnlyList<string>>(
            () => _loader.GetWorkingDirectoryChanges().Select(file => file.Name).ToList(),
            cancellationToken);

        PendingCount = pending.Count;

        if (pending.Count == 0)
        {
            return;
        }

        CommitRowViewModel row = CommitRowViewModel.CreateWorkingDirectory(pending.Count);
        _allCommits.Insert(0, row);

        if (PassesFilter(row))
        {
            Commits.Insert(0, row);
        }
    }

    private bool PassesFilter(CommitRowViewModel row) =>
        string.IsNullOrWhiteSpace(Filter) || row.IsWorkingDirectory || row.Matches(Filter);

    private void ApplyFilter()
    {
        CommitRowViewModel? previous = SelectedCommit;

        Commits.Clear();
        foreach (CommitRowViewModel row in _allCommits.Where(PassesFilter))
        {
            Commits.Add(row);
        }

        // Keep the current selection if it survived the filter, otherwise fall back to the top.
        SelectedCommit = previous is not null && Commits.Contains(previous) ? previous : Commits.FirstOrDefault();
    }

    /// <summary>
    ///  Runs on the UI thread once every streamed batch has been applied — either because the log
    ///  ended, or because the page was filled.
    /// </summary>
    private void OnStreamFinished(bool limitReached)
    {
        SelectedCommit ??= Commits.FirstOrDefault();

        if (limitReached)
        {
            // Stop git reading a history we're not going to show yet.
            _loadCts?.Cancel();
            HasMoreCommits = true;
        }

        int commitCount = _allCommits.Count(row => !row.IsWorkingDirectory);
        Status = commitCount == 0
            ? "No commits found."
            : $"{commitCount.ToString("N0", CultureInfo.InvariantCulture)} commits";
    }

    /// <summary>
    ///  Reloads one object collection off the UI thread. Failures leave the collection empty and are
    ///  reported through <see cref="Status"/> rather than thrown — a listing page is not worth a crash.
    /// </summary>
    private async Task ReplaceAsync<T>(ObservableCollection<T> target, Func<IReadOnlyList<T>> read)
    {
        try
        {
            IReadOnlyList<T> items = await Task.Run(read);

            target.Clear();
            foreach (T item in items)
            {
                target.Add(item);
            }
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    /// <summary>
    ///  Runs a git operation off the UI thread and reloads afterwards. Returns the result so the view
    ///  can report it; never throws. Private because the loader it hands out is internal — callers
    ///  use the named operations above.
    /// </summary>
    private async Task<GitOperationResult> RunOperationAsync(Func<RepositoryLoader, GitOperationResult> operation)
    {
        if (IsBusy)
        {
            return new GitOperationResult("Busy", false, "Another git operation is still running.");
        }

        IsBusy = true;

        try
        {
            GitOperationResult result = await Task.Run(() => operation(_loader));

            // Even a failed operation can have changed things (a partial fetch, a checkout that
            // touched the index), so always resync.
            await LoadAsync();
            return result;
        }
        catch (Exception ex)
        {
            return new GitOperationResult("Error", false, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task<GitOperationResult> RunOnSelectedCommitAsync(string description, Func<RepositoryLoader, ObjectId, GitOperationResult> operation)
    {
        if (SelectedCommit?.Revision?.ObjectId is not ObjectId commit)
        {
            return Task.FromResult(new GitOperationResult(description, false, "Select a commit first."));
        }

        return RunOperationAsync(loader => operation(loader, commit));
    }

    private async Task CheckoutAsync(string branch)
    {
        GitOperationResult result = await RunOperationAsync(loader => loader.Checkout(branch));

        if (!result.Succeeded)
        {
            Status = $"Checkout failed: {result.Output}";
        }
    }

    /// <summary>
    ///  Refreshes the branch picker and the ahead/behind counters from one <c>for-each-ref</c> read,
    ///  which reports both the names and the tracking state.
    /// </summary>
    private async Task RefreshBranchesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<BranchInfo> branches = await Task.Run(_loader.GetBranches, cancellationToken);

        BranchDetails.Clear();
        Branches.Clear();

        foreach (BranchInfo branch in branches.OrderBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase))
        {
            Branches.Add(branch.Name);
        }

        foreach (BranchInfo branch in branches)
        {
            BranchDetails.Add(branch);
        }

        BranchInfo? current = branches.FirstOrDefault(branch => branch.IsCurrent);
        Upstream = current?.Upstream ?? "";
        AheadCount = current?.Ahead ?? 0;
        BehindCount = current?.Behind ?? 0;

        // Reflect the checked-out branch without treating it as a request to check something out.
        _suppressBranchCheckout = true;
        SelectedBranch = Branches.Contains(Branch) ? Branch : null;
        _suppressBranchCheckout = false;
    }

    /// <summary>
    ///  Works out where this repository lives online, preferring "origin" and otherwise taking the
    ///  first remote that parses into something browsable.
    /// </summary>
    private async Task ResolveHostAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RemoteInfo> remotes = await Task.Run(_loader.GetRemotes, cancellationToken);

        RemoteInfo? preferred = remotes.FirstOrDefault(remote => remote.Name == "origin") ?? remotes.FirstOrDefault();

        Host = preferred is null ? null : GitHostLinks.Parse(preferred.FetchUrl);
    }

    /// <summary>
    ///  Re-reads the things that are true of the repository as a whole rather than of one commit: a
    ///  paused operation, and whether commits can be made at all.
    /// </summary>
    private async Task RefreshRepositoryStateAsync(CancellationToken cancellationToken)
    {
        Operation = await Task.Run(_loader.GetCurrentOperation, cancellationToken);

        await LoadConflictsAsync();

        (string name, string email) = await Task.Run(_loader.GetEffectiveIdentity, cancellationToken);
        NeedsIdentity = name.Length == 0 || email.Length == 0;
    }

    /// <summary>Continue, skip or abort whatever is paused, using that operation's own subcommand.</summary>
    public Task<GitOperationResult> ResolveOperationAsync(string action)
    {
        string command = Operation.CommandName;

        return command.Length == 0
            ? Task.FromResult(new GitOperationResult("Resolve", false, "Nothing is in progress."))
            : RunOperationAsync(loader => loader.ContinueOperation(command, action));
    }

    /// <summary>
    ///  Fetches in the background so the ahead/behind counts mean something.
    /// </summary>
    /// <remarks>
    ///  Those counts come from the remote-tracking refs, which only move when something fetches — so
    ///  without this the toolbar can report "up to date" indefinitely while the remote moves on.
    ///  Skipped while another operation is running, and it refreshes only the branch state rather than
    ///  reloading the commit list, so it never disturbs what is on screen.
    /// </remarks>
    public async Task AutoFetchAsync()
    {
        if (IsBusy || IsLoading)
        {
            return;
        }

        try
        {
            GitOperationResult result = await Task.Run(_loader.FetchQuietly);

            if (result.Succeeded)
            {
                await RefreshBranchesAsync(CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // A background fetch failing is not worth telling anyone about: no network, no remote,
            // or credentials needed. The next manual fetch will say so properly.
        }
    }

    private async Task LoadChangedFilesAsync(CommitRowViewModel? commit)
    {
        Cancel(ref _changedFilesCts);

        // Moving to another commit invalidates whichever file's diff was on screen.
        SelectedChangedFile = null;
        ChangedFiles.Clear();

        if (commit is null)
        {
            return;
        }

        CancellationTokenSource cts = new();
        _changedFilesCts = cts;

        try
        {
            bool isWorkingDirectory = commit.IsWorkingDirectory;
            GitRevision? revision = commit.Revision;

            // Resolved to a non-nullable local before the lambda captures it: null-state analysis does
            // not flow into a closure, so the comparison branch would not compile against it.
            Func<IReadOnlyList<GitItemStatus>> read;

            if (isWorkingDirectory)
            {
                read = _loader.GetWorkingDirectoryChanges;
            }
            else if (CompareTarget?.Revision?.ObjectId is ObjectId compareFrom && revision is not null)
            {
                ObjectId to = revision.ObjectId;
                read = () => _loader.GetChangedFilesBetween(compareFrom, to, cts.Token);
            }
            else
            {
                read = () => _loader.GetChangedFiles(revision!, cts.Token);
            }

            IReadOnlyList<GitItemStatus> files = await Task.Run(read, cts.Token);

            // The selection may have moved on while we were reading.
            if (!ReferenceEquals(SelectedCommit, commit))
            {
                return;
            }

            foreach (GitItemStatus file in files)
            {
                ChangedFiles.Add(new ChangedFileViewModel(file, isWorkingDirectory));
            }

            // Show something straight away rather than an empty diff pane awaiting a click.
            SelectedChangedFile = ChangedFiles.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_changedFilesCts, cts))
            {
                _changedFilesCts = null;
            }
        }
    }

    private Task LoadHistoryDiffAsync(ChangedFileViewModel? file)
    {
        if (file is null || SelectedCommit is not CommitRowViewModel commit)
        {
            HistoryDiff.Clear();
            return Task.CompletedTask;
        }

        if (commit.IsWorkingDirectory)
        {
            return HistoryDiff.LoadAsync(
                file.Name,
                token => Task.Run(() => _loader.GetWorkingDirectoryDiff(file.Name, file.OldName, file.IsStaged), token));
        }

        // As above: narrowed into a local so the closure captures a non-nullable value.
        if (CompareTarget?.Revision?.ObjectId is ObjectId compareFrom && commit.Revision is GitRevision revision)
        {
            return HistoryDiff.LoadAsync(
                file.Name,
                token => _loader.GetDiffTextBetweenAsync(compareFrom, revision.ObjectId, file.Name, file.OldName, token));
        }

        return HistoryDiff.LoadAsync(
            file.Name,
            token => _loader.GetDiffTextAsync(commit.Revision!, file.Name, file.OldName, token));
    }

    /// <remarks>
    ///  The token sources are deliberately not disposed: background work may still be observing the
    ///  token after cancellation, and disposing it out from under them throws ObjectDisposedException.
    ///  They hold no timer, so letting the GC take them is harmless.
    /// </remarks>
    private static void Cancel(ref CancellationTokenSource? cts)
    {
        CancellationTokenSource? previous = cts;
        cts = null;
        previous?.Cancel();
    }

    private static string GetRepositoryName(string workingDir)
    {
        string trimmed = workingDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    /// <summary>
    ///  Marshals each batch the revision reader emits onto the UI thread. Batching per callback
    ///  (rather than per revision) keeps the ObservableCollection churn manageable.
    /// </summary>
    private sealed class StreamingObserver : IObserver<IReadOnlyList<GitRevision>>
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly CancellationToken _cancellationToken;
        private readonly Func<IReadOnlyList<GitRevision>, bool> _addBatch;
        private readonly Action<bool> _onFinished;

        /// <summary>UI-thread only, so it needs no synchronisation.</summary>
        private bool _finished;

        public StreamingObserver(
            DispatcherQueue dispatcherQueue,
            CancellationToken cancellationToken,
            Func<IReadOnlyList<GitRevision>, bool> addBatch,
            Action<bool> onFinished)
        {
            _dispatcherQueue = dispatcherQueue;
            _cancellationToken = cancellationToken;
            _addBatch = addBatch;
            _onFinished = onFinished;
        }

        public void OnCompleted()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Queued behind all the batch callbacks above, so the collection is complete by now.
            _dispatcherQueue.TryEnqueue(() => Finish(limitReached: false));
        }

        public void OnError(Exception error)
        {
            // Surfaced by the awaited Task.Run instead: GetLog rethrows on the calling thread.
        }

        public void OnNext(IReadOnlyList<GitRevision> value)
        {
            if (_cancellationToken.IsCancellationRequested || value.Count == 0)
            {
                return;
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_cancellationToken.IsCancellationRequested || _finished)
                {
                    return;
                }

                if (!_addBatch(value))
                {
                    Finish(limitReached: true);
                }
            });
        }

        private void Finish(bool limitReached)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _onFinished(limitReached);
        }
    }
}
