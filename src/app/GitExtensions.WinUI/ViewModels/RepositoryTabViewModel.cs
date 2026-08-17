using System.Collections.ObjectModel;
using System.Globalization;
using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Diff;
using GitExtensions.WinUI.Graph;
using GitExtensions.WinUI.Services;
using GitUIPluginInterfaces;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI.ViewModels;

/// <summary>
///  One open repository. Each tab owns its own state so tabs stay fully independent, mirroring how
///  the WinForms app gives every tab its own IGitUICommands/GitModule.
/// </summary>
public sealed class RepositoryTabViewModel : ObservableObject
{
    /// <summary>
    ///  How many commits to read per page, and how many diff lines to render. Configurable in
    ///  Settings; capped at all because this repository alone has ~17,000 commits, which is more
    ///  than an unvirtualized bound collection should hold.
    /// </summary>
    private static int MaxCommits => AppOptions.MaxCommits;

    private static int MaxDiffLines => AppOptions.MaxDiffLines;

    /// <summary>
    ///  Every loaded commit. <see cref="Commits"/> is the filtered projection actually bound to the
    ///  list, so filtering never discards rows we would have to re-read from git.
    /// </summary>
    private readonly List<CommitRowViewModel> _allCommits = [];

    private readonly RepositoryLoader _loader;
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>Rebuilt from scratch on a full reload; kept across pages so lanes stay continuous.</summary>
    private CommitGraphBuilder _graphBuilder = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _changedFilesCts;
    private CancellationTokenSource? _diffCts;
    private UiMode _mode;
    private string _branch = "";
    private string _status = "";
    private string _filter = "";
    private bool _isLoading;
    private bool _hasMoreCommits;
    private bool _isBusy;
    private string? _selectedBranch;
    private bool _suppressBranchCheckout;
    private CommitRowViewModel? _selectedCommit;
    private ChangedFileViewModel? _selectedChangedFile;
    private string _diffTitle = "";
    private bool _isSideBySide;

    public RepositoryTabViewModel(IGitExecutorProvider executorProvider, string workingDir, UiMode mode)
    {
        _loader = new RepositoryLoader(executorProvider, workingDir);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _mode = mode;
        WorkingDir = workingDir;
        Title = GetRepositoryName(workingDir);
    }

    public string WorkingDir { get; }

    public string Title { get; }

    /// <summary>The commits currently shown — <see cref="_allCommits"/> passed through the filter.</summary>
    public ObservableCollection<CommitRowViewModel> Commits { get; } = [];

    public ObservableCollection<ChangedFileViewModel> ChangedFiles { get; } = [];

    public ObservableCollection<DiffLineViewModel> DiffLines { get; } = [];

    public ObservableCollection<SideBySideRow> SideBySideLines { get; } = [];

    /// <summary>Unified or side-by-side; the diff panel shows one or the other.</summary>
    public bool IsSideBySide
    {
        get => _isSideBySide;
        set
        {
            if (SetProperty(ref _isSideBySide, value))
            {
                OnPropertyChanged(nameof(UnifiedVisibility));
                OnPropertyChanged(nameof(SideBySideVisibility));
                RebuildSideBySide();
            }
        }
    }

    public Visibility UnifiedVisibility => IsSideBySide ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SideBySideVisibility => IsSideBySide ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<string> Branches { get; } = [];

    /// <summary>
    ///  Width of the graph column. Duplicated from the row view model because the column header
    ///  spacer binds against this tab, not against a row.
    /// </summary>
    public double GraphColumnWidth => CommitGraphBuilder.ColumnWidth;

    /// <summary>Free-text filter over the loaded commits.</summary>
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
                OnPropertyChanged(nameof(DiffVisibility));
                OnPropertyChanged(nameof(LoadMoreVisibility));
                OnPropertyChanged(nameof(AdvancedVisibility));
            }
        }
    }

    public string Branch
    {
        get => _branch;
        private set => SetProperty(ref _branch, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
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
                _ = LoadChangedFilesAsync(value);
            }
        }
    }

    /// <summary>Selecting a changed file loads its diff into the bottom panel.</summary>
    public ChangedFileViewModel? SelectedChangedFile
    {
        get => _selectedChangedFile;
        set
        {
            if (SetProperty(ref _selectedChangedFile, value))
            {
                OnPropertyChanged(nameof(DiffVisibility));
                _ = LoadDiffAsync(value);
            }
        }
    }

    public string DiffTitle
    {
        get => _diffTitle;
        private set => SetProperty(ref _diffTitle, value);
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

    /// <summary>Anything Advanced-only that isn't the details pane (branch picker, filter box).</summary>
    public Visibility AdvancedVisibility => Mode == UiMode.Advanced ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Column headers, branch/path line: everything except Zen.</summary>
    public Visibility ChromeVisibility => Mode == UiMode.Zen ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Author and date columns — dropped in Zen so only the message remains.</summary>
    public Visibility SecondaryColumnVisibility => Mode == UiMode.Zen ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The diff panel only takes up space once a file is actually selected.</summary>
    public Visibility DiffVisibility =>
        Mode == UiMode.Advanced && SelectedChangedFile is not null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LoadMoreVisibility =>
        HasMoreCommits && Mode != UiMode.Zen ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    ///  Reloads the commit list, streaming rows in as <c>git log</c> produces them so the window stays
    ///  responsive on large repositories rather than blocking until the whole history is read.
    /// </summary>
    public Task LoadAsync() => LoadPageAsync(append: false);

    /// <summary>Appends the next page of older commits.</summary>
    public Task LoadMoreAsync() => LoadPageAsync(append: true);

    public Task<GitOperationResult> FetchAsync() => RunOperationAsync(loader => loader.Fetch());

    public Task<GitOperationResult> PullAsync() => RunOperationAsync(loader => loader.Pull());

    public Task<GitOperationResult> PushAsync()
    {
        string branch = Branch;
        return RunOperationAsync(loader => loader.Push(branch));
    }

    public Task<GitOperationResult> CommitAllAsync(string message) =>
        RunOperationAsync(loader => loader.CommitAll(message));

    /// <summary>Commits only what is staged, so partial commits are possible.</summary>
    public Task<GitOperationResult> CommitStagedAsync(string message, bool amend, bool signOff) =>
        RunOperationAsync(loader => loader.Commit(message, amend, signOff));

    public Task<string> GetLastCommitMessageAsync() => Task.Run(_loader.GetLastCommitMessage);

    /// <summary>The working-directory changes split by whether they are staged.</summary>
    public Task<IReadOnlyList<ChangedFileViewModel>> GetWorkingDirectoryFilesAsync() =>
        Task.Run<IReadOnlyList<ChangedFileViewModel>>(
            () => _loader.GetWorkingDirectoryChanges()
                .Select(file => new ChangedFileViewModel(file, isWorkingDirectory: true))
                .ToList());

    /// <summary>Stages or unstages without the full reload a normal operation triggers.</summary>
    public Task<GitOperationResult> SetStagedAsync(string fileName, bool staged) =>
        Task.Run(() => staged ? _loader.StageFile(fileName) : _loader.UnstageFile(fileName));

    public Task<GitOperationResult> StageAsync(string fileName) =>
        RunOperationAsync(loader => loader.StageFile(fileName));

    public Task<GitOperationResult> UnstageAsync(string fileName) =>
        RunOperationAsync(loader => loader.UnstageFile(fileName));

    public Task<GitOperationResult> CreateBranchAsync(string branchName, bool checkout)
    {
        // Branch from the selected commit when there is one, otherwise from HEAD.
        if (SelectedCommit?.Revision?.ObjectId is not ObjectId objectId)
        {
            return RunOperationAsync(loader => loader.CreateBranchFromHead(branchName, checkout));
        }

        return RunOperationAsync(loader => loader.CreateBranch(branchName, objectId, checkout));
    }

    public Task<GitOperationResult> DeleteBranchAsync(string branchName, bool force) =>
        RunOperationAsync(loader => loader.DeleteBranch(branchName, force));

    public Task<GitOperationResult> MergeBranchAsync(string branchName) =>
        RunOperationAsync(loader => loader.MergeBranch(branchName));

    public Task<GitOperationResult> StashSaveAsync(string message) =>
        RunOperationAsync(loader => loader.StashSave(message));

    public Task<GitOperationResult> StashListAsync() =>
        RunOperationAsync(loader => loader.StashList());

    public Task<GitOperationResult> StashPopAsync() =>
        RunOperationAsync(loader => loader.StashPop());

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

    public Task<IReadOnlyList<string>> GetConflictedFilesAsync() =>
        Task.Run(_loader.GetConflictedFiles);

    public Task<GitOperationResult> MarkResolvedAsync(string fileName) =>
        RunOperationAsync(loader => loader.MarkResolved(fileName));

    public Task<GitOperationResult> ListTagsAsync() => RunReadOnlyAsync(loader => loader.ListTags());

    public Task<GitOperationResult> CreateTagAsync(string name, string message) =>
        RunOperationAsync(loader => loader.CreateTag(name, SelectedCommit?.Revision?.ObjectId, message));

    public Task<GitOperationResult> DeleteTagAsync(string name) =>
        RunOperationAsync(loader => loader.DeleteTag(name));

    public Task<GitOperationResult> PushTagAsync(string name) =>
        RunOperationAsync(loader => loader.PushTag(name));

    public Task<GitOperationResult> ListRemotesAsync() => RunReadOnlyAsync(loader => loader.ListRemotes());

    public Task<GitOperationResult> AddRemoteAsync(string name, string url) =>
        RunOperationAsync(loader => loader.AddRemote(name, url));

    public Task<GitOperationResult> RemoveRemoteAsync(string name) =>
        RunOperationAsync(loader => loader.RemoveRemote(name));

    public Task<GitOperationResult> ListSubmodulesAsync() => RunReadOnlyAsync(loader => loader.ListSubmodules());

    public Task<GitOperationResult> UpdateSubmodulesAsync() =>
        RunOperationAsync(loader => loader.UpdateSubmodules());

    public Task<GitOperationResult> SyncSubmodulesAsync() =>
        RunOperationAsync(loader => loader.SyncSubmodules());

    public Task<GitOperationResult> ListWorktreesAsync() => RunReadOnlyAsync(loader => loader.ListWorktrees());

    public Task<GitOperationResult> AddWorktreeAsync(string path, string branch) =>
        RunOperationAsync(loader => loader.AddWorktree(path, branch));

    public Task<GitOperationResult> RemoveWorktreeAsync(string path) =>
        RunOperationAsync(loader => loader.RemoveWorktree(path));

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
        _diffCts?.Cancel();
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
                await RefreshBranchesAsync(cts.Token);
                await AddWorkingDirectoryRowAsync(cts.Token);
            }

            // The completion callback is enqueued behind every batch the observer dispatched, so by
            // the time it runs the collection is fully populated — the DispatcherQueue is FIFO.
            StreamingObserver observer = new(_dispatcherQueue, cts.Token, batch => AddCommitBatch(batch, skip + MaxCommits), OnStreamFinished);

            await Task.Run(() => _loader.StreamRevisions(observer, skip, cts.Token), cts.Token);
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

            CommitRowViewModel row = new(revision) { GraphSegments = _graphBuilder.AddCommit(revision) };
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
            : $"Showing {commitCount.ToString("N0", CultureInfo.InvariantCulture)} commits.";
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

    /// <summary>
    ///  For listing commands, which change nothing and so don't need the reload that
    ///  <see cref="RunOperationAsync"/> does afterwards.
    /// </summary>
    private async Task<GitOperationResult> RunReadOnlyAsync(Func<RepositoryLoader, GitOperationResult> operation)
    {
        try
        {
            return await Task.Run(() => operation(_loader));
        }
        catch (Exception ex)
        {
            return new GitOperationResult("Error", false, ex.Message);
        }
    }

    private async Task CheckoutAsync(string branch)
    {
        GitOperationResult result = await RunOperationAsync(loader => loader.Checkout(branch));

        if (!result.Succeeded)
        {
            Status = $"Checkout failed: {result.Output}";
        }
    }

    private async Task RefreshBranchesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> branches = await Task.Run(_loader.GetLocalBranches, cancellationToken);

        Branches.Clear();
        foreach (string branch in branches)
        {
            Branches.Add(branch);
        }

        // Reflect the checked-out branch without treating it as a request to check something out.
        _suppressBranchCheckout = true;
        SelectedBranch = Branches.Contains(Branch) ? Branch : null;
        _suppressBranchCheckout = false;
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

            IReadOnlyList<GitItemStatus> files = await Task.Run(
                () => isWorkingDirectory
                    ? _loader.GetWorkingDirectoryChanges()
                    : _loader.GetChangedFiles(revision!, cts.Token),
                cts.Token);

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

    private async Task LoadDiffAsync(ChangedFileViewModel? file)
    {
        Cancel(ref _diffCts);
        DiffLines.Clear();

        if (file is null || SelectedCommit is null)
        {
            DiffTitle = "";
            return;
        }

        CancellationTokenSource cts = new();
        _diffCts = cts;
        DiffTitle = file.Name;

        try
        {
            CommitRowViewModel commit = SelectedCommit;

            string diff = await (commit.IsWorkingDirectory
                ? Task.Run(() => _loader.GetWorkingDirectoryDiff(file.Name, file.OldName, file.IsStaged), cts.Token)
                : _loader.GetDiffTextAsync(commit.Revision!, file.Name, file.OldName, cts.Token));

            // The selection may have moved on while git was running.
            if (!ReferenceEquals(SelectedChangedFile, file) || cts.Token.IsCancellationRequested)
            {
                return;
            }

            if (string.IsNullOrEmpty(diff))
            {
                DiffLines.Add(DiffLineViewModel.CreatePlain("(no textual diff — binary file, or no changes)"));
                return;
            }

            IReadOnlyList<DiffLineViewModel> parsed = DiffParser.ParseUnified(diff, file.Name, MaxDiffLines, out int omitted);

            foreach (DiffLineViewModel line in parsed)
            {
                DiffLines.Add(line);
            }

            if (omitted > 0)
            {
                DiffLines.Add(DiffLineViewModel.CreatePlain(
                    $"… {omitted.ToString("N0", CultureInfo.InvariantCulture)} more lines not shown"));
            }

            RebuildSideBySide();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DiffLines.Add(DiffLineViewModel.CreatePlain(ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_diffCts, cts))
            {
                _diffCts = null;
            }
        }
    }

    private void RebuildSideBySide()
    {
        SideBySideLines.Clear();

        if (!IsSideBySide)
        {
            return;
        }

        foreach (SideBySideRow row in DiffParser.ToSideBySide([.. DiffLines]))
        {
            SideBySideLines.Add(row);
        }
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
