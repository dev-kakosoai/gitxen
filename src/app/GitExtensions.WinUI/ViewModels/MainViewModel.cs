using System.Collections.ObjectModel;
using System.ComponentModel.Design;
using GitCommands;
using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
using GitExtUtils;
using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI.ViewModels;

/// <summary>
///  The shell: the Home tab, the set of open repositories, and the current <see cref="UiMode"/>.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IGitExecutorProvider _executorProvider;
    private readonly RepositoryCreator _creator;
    private UiMode _mode = UiMode.Simple;
    private UiMode _modeBeforeZen = UiMode.Simple;
    private ShellTab? _selectedTab;

    public MainViewModel(ServiceContainer serviceContainer)
    {
        _executorProvider = serviceContainer.GetRequiredService<IGitExecutorProvider>();
        _creator = new RepositoryCreator(_executorProvider);
        GlobalGit = new GlobalGitSettings(_executorProvider);

        Tabs.Add(Home);
        SelectedTab = Home;
    }

    /// <summary>Always first in the strip, and never removed.</summary>
    public HomeTabViewModel Home { get; } = new();

    /// <summary>
    ///  Reads and writes git's global configuration, with no repository open.
    /// </summary>
    /// <remarks>
    ///  Lives here because the setup wizard needs it before anything has been opened, and this is
    ///  the one object that exists that early and already holds the executor provider.
    /// </remarks>
    internal GlobalGitSettings GlobalGit { get; }

    public ObservableCollection<ShellTab> Tabs { get; } = [];

    /// <summary>
    ///  Repository groups, in display order. A repository belongs to at most one.
    /// </summary>
    public ObservableCollection<RepositoryGroup> Groups { get; } = [];

    /// <summary>
    ///  The open repositories, sectioned by project, for the repository column.
    /// </summary>
    /// <remarks>
    ///  A separate projection from <see cref="Tabs"/> because the column and the tab strip need
    ///  different shapes of the same thing: the column shows sections, the strip is necessarily flat.
    /// </remarks>
    public ObservableCollection<ShellTabGroup> TabGroups { get; } = [];

    /// <summary>Recent repositories that are not in any group.</summary>
    public RepositoryGroup Ungrouped { get; } = new("Ungrouped", GroupIcons.Default, "Slate");

    /// <summary>Recent repositories with their current branch, shown on the Home page.</summary>
    public ObservableCollection<RecentRepository> RecentRepositories { get; } = [];

    public ShellTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(SelectedRepository));
                OnPropertyChanged(nameof(HomeVisibility));
                OnPropertyChanged(nameof(RepositoryVisibility));
            }
        }
    }

    /// <summary>
    ///  The selected tab when it is a repository, null when Home is selected. The repository view
    ///  binds to this so it is simply empty on Home rather than needing to be told about it.
    /// </summary>
    public RepositoryTabViewModel? SelectedRepository => SelectedTab as RepositoryTabViewModel;

    public Visibility HomeVisibility =>
        SelectedRepository is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RepositoryVisibility =>
        SelectedRepository is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Open repositories, excluding Home — what the session records and the counts report.</summary>
    public IEnumerable<RepositoryTabViewModel> Repositories => Tabs.OfType<RepositoryTabViewModel>();

    public UiMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value))
            {
                return;
            }

            foreach (RepositoryTabViewModel tab in Repositories)
            {
                tab.Mode = value;
            }

            OnPropertyChanged(nameof(ToolbarVisibility));
            OnPropertyChanged(nameof(IsZen));
            OnPropertyChanged(nameof(IsAdvancedSelected));
            OnPropertyChanged(nameof(AdvancedVisibility));
        }
    }

    /// <summary>Zen hides the shell chrome entirely — the commit list is all that's left.</summary>
    public Visibility ToolbarVisibility => Mode == UiMode.Zen ? Visibility.Collapsed : Visibility.Visible;

    public bool IsZen => Mode == UiMode.Zen;

    /// <summary>Drives the Simple/Advanced toggle; Zen isn't represented there.</summary>
    public bool IsAdvancedSelected => Mode == UiMode.Advanced;

    /// <summary>Toolbar entries that only Advanced mode exposes.</summary>
    public Visibility AdvancedVisibility => Mode == UiMode.Advanced ? Visibility.Visible : Visibility.Collapsed;

    // ---- Repository list layout ------------------------------------------------------------------
    // Tabs across the top or a column down the left. Which one suits depends on how many repositories
    // are open and how long their names are, so it is a preference rather than a fixed choice.

    public RepositoryLayout Layout
    {
        get => AppOptions.Layout;
        set
        {
            if (AppOptions.Layout == value)
            {
                return;
            }

            AppOptions.Layout = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSidebarLayout));
            OnPropertyChanged(nameof(TabStripVisibility));
            OnPropertyChanged(nameof(SidebarVisibility));
        }
    }

    public bool IsSidebarLayout => Layout == RepositoryLayout.Sidebar;

    public Visibility TabStripVisibility =>
        Layout == RepositoryLayout.Tabs && Mode != UiMode.Zen ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SidebarVisibility =>
        Layout == RepositoryLayout.Sidebar && Mode != UiMode.Zen ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Most-recently-opened repository paths, newest first.</summary>
    public ObservableCollection<string> Recent { get; } = [];

    public bool IsValidRepository(string path) => GitModule.IsValidGitWorkingDir(path);

    /// <summary>
    ///  Opens <paramref name="workingDir"/> in a new tab and starts loading it. If the repository is
    ///  already open, the existing tab is selected instead of duplicating it.
    /// </summary>
    public async Task OpenRepositoryAsync(string workingDir)
    {
        RepositoryTabViewModel? existing = Repositories.FirstOrDefault(
            tab => string.Equals(tab.WorkingDir, workingDir, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        RepositoryTabViewModel tab = new(_executorProvider, workingDir, Mode);

        // Applied before the first load so a restored branch scope does not cost a second read of
        // the whole history.
        ApplySavedState(tab);

        Tabs.Add(tab);
        SelectedTab = tab;
        AddRecent(workingDir);
        RegroupTabs();

        await tab.LoadAsync();
    }

    /// <summary>
    ///  Clones a repository and opens it. The clone runs off the UI thread — it is the one operation
    ///  here that routinely takes minutes.
    /// </summary>
    public async Task<GitOperationResult> CloneAsync(string url, string parentDirectory, string folderName, bool recurseSubmodules)
    {
        (GitOperationResult result, string workingDirectory) = await Task.Run(
            () => _creator.Clone(url, parentDirectory, folderName, recurseSubmodules));

        if (result.Succeeded && workingDirectory.Length > 0)
        {
            await OpenRepositoryAsync(workingDirectory);
        }

        return result;
    }

    /// <summary>Initialises a repository and opens it, unless it is bare and so has no working tree.</summary>
    public async Task<GitOperationResult> InitAsync(string directory, bool bare)
    {
        (GitOperationResult result, string workingDirectory) = await Task.Run(() => _creator.Init(directory, bare));

        if (result.Succeeded && workingDirectory.Length > 0)
        {
            await OpenRepositoryAsync(workingDirectory);
        }

        return result;
    }

    /// <summary>
    ///  Rebuilds the Home page's recent list, then fills in each entry's branch and last commit in the
    ///  background.
    /// </summary>
    /// <remarks>
    ///  The reads are per repository and hit the disk, so they happen after the list is on screen. A
    ///  repository that has been moved or deleted is marked rather than dropped — silently removing it
    ///  would look like it had never been opened.
    /// </remarks>
    public async Task RefreshRecentAsync()
    {
        RecentRepositories.Clear();

        foreach (string path in Recent)
        {
            RecentRepositories.Add(new RecentRepository(path));
        }

        // Sort them into their groups before the per-repository detail is read, so the page can
        // draw the structure immediately and fill in branches as they arrive.
        Regroup();

        foreach (RecentRepository entry in RecentRepositories)
        {
            if (!IsValidRepository(entry.Path))
            {
                entry.IsMissing = true;
                continue;
            }

            try
            {
                RepositoryLoader loader = new(_executorProvider, entry.Path);

                (string branch, string subject) = await Task.Run(
                    () => (loader.GetCurrentBranch(), loader.GetLastCommitMessage()));

                entry.Branch = branch;

                // Only the first line: the recent list has one line to spend on it.
                entry.LastCommit = subject.Split('\n', 2)[0].Trim();
            }
            catch (Exception)
            {
                // A repository that cannot be read is still worth listing; it just shows no detail.
                entry.IsMissing = true;
            }
        }
    }

    // ---- Groups ----------------------------------------------------------------------------------
    // Membership is stored as paths, so a group survives its repositories being closed or temporarily
    // unavailable. The RecentRepository objects are rebuilt on every refresh and sorted into the
    // groups from that stored membership.

    /// <summary>Which group each repository path belongs to; the authority for membership.</summary>
    private readonly Dictionary<string, string> _groupByPath = new(StringComparer.OrdinalIgnoreCase);

    public RepositoryGroup CreateGroup(string name, string glyph, string colorKey)
    {
        RepositoryGroup group = new(name, glyph, colorKey);
        Groups.Add(group);
        return group;
    }

    /// <summary>
    ///  Removes a group. Its repositories are not touched — they simply become ungrouped, since the
    ///  group was only ever a statement about how they are organised.
    /// </summary>
    public void DeleteGroup(RepositoryGroup group)
    {
        foreach (string path in _groupByPath
            .Where(pair => string.Equals(pair.Value, group.Name, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToList())
        {
            _groupByPath.Remove(path);
        }

        Groups.Remove(group);
        Regroup();
    }

    /// <summary>Renames a group, keeping the membership that is keyed by its name.</summary>
    public void RenameGroup(RepositoryGroup group, string name)
    {
        string previous = group.Name;

        foreach (string path in _groupByPath
            .Where(pair => string.Equals(pair.Value, previous, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToList())
        {
            _groupByPath[path] = name;
        }

        group.Name = name;
    }

    /// <summary>Moves a repository into a group, or out of every group when it is null.</summary>
    public void AssignToGroup(string path, RepositoryGroup? group)
    {
        if (group is null)
        {
            _groupByPath.Remove(path);
        }
        else
        {
            _groupByPath[path] = group.Name;
        }

        Regroup();
    }

    /// <summary>
    ///  Redistributes the recent repositories into their groups.
    /// </summary>
    /// <remarks>
    ///  Rebuilt wholesale rather than moved item by item: the recent list is small, and reconstructing
    ///  it keeps the groups consistent with the stored membership no matter how it was reached —
    ///  a drag, a rename, or a repository appearing for the first time.
    /// </remarks>
    private void Regroup()
    {
        foreach (RepositoryGroup group in Groups)
        {
            group.Repositories.Clear();
        }

        Ungrouped.Repositories.Clear();

        foreach (RecentRepository entry in RecentRepositories)
        {
            RepositoryGroup? target = _groupByPath.TryGetValue(entry.Path, out string? name)
                ? Groups.FirstOrDefault(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase))
                : null;

            (target ?? Ungrouped).Repositories.Add(entry);
        }

        foreach (RepositoryGroup group in Groups)
        {
            group.RaiseCountChanged();
        }

        Ungrouped.RaiseCountChanged();
        RegroupTabs();
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(UngroupedVisibility));
    }

    /// <summary>
    ///  Re-sections the open repositories and puts the tab strip in project order.
    /// </summary>
    /// <remarks>
    ///  The strip cannot show sections — it is one row of tabs — so grouping is expressed there by
    ///  ordering, which keeps a project's repositories adjacent, and by the colour each tab carries.
    ///  Home always leads, since it is the one tab that is not a repository.
    /// </remarks>
    private void RegroupTabs()
    {
        foreach (RepositoryTabViewModel tab in Repositories)
        {
            tab.Group = _groupByPath.TryGetValue(tab.WorkingDir, out string? name)
                ? Groups.FirstOrDefault(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        TabGroups.Clear();

        foreach (RepositoryGroup group in Groups)
        {
            ShellTabGroup section = new(group);

            if (group.IsExpanded)
            {
                foreach (RepositoryTabViewModel tab in Repositories.Where(tab => ReferenceEquals(tab.Group, group)))
                {
                    section.Items.Add(tab);
                }
            }

            // Empty projects are still listed: the header is the drop target that puts one back.
            TabGroups.Add(section);
        }

        ShellTabGroup ungrouped = new(null);

        foreach (RepositoryTabViewModel tab in Repositories.Where(tab => tab.Group is null))
        {
            ungrouped.Items.Add(tab);
        }

        if (ungrouped.Items.Count > 0 || TabGroups.Count == 0)
        {
            TabGroups.Add(ungrouped);
        }

        ReorderTabs();
    }

    /// <summary>Puts the strip in project order without disturbing the selection.</summary>
    private void ReorderTabs()
    {
        List<ShellTab> ordered = [Home];

        foreach (RepositoryGroup group in Groups)
        {
            ordered.AddRange(Repositories.Where(tab => ReferenceEquals(tab.Group, group)));
        }

        ordered.AddRange(Repositories.Where(tab => tab.Group is null));

        for (int index = 0; index < ordered.Count; index++)
        {
            int current = Tabs.IndexOf(ordered[index]);

            if (current >= 0 && current != index)
            {
                Tabs.Move(current, index);
            }
        }
    }

    /// <summary>Collapses or expands a project in the repository column.</summary>
    public void ToggleGroupExpanded(RepositoryGroup group)
    {
        group.IsExpanded = !group.IsExpanded;
        RegroupTabs();
    }

    public bool HasGroups => Groups.Count > 0;

    /// <summary>
    ///  The ungrouped list is only worth a heading once there is something to contrast it with.
    /// </summary>
    public Visibility UngroupedVisibility =>
        Ungrouped.Repositories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void RestoreGroups(IEnumerable<SavedGroup> saved)
    {
        Groups.Clear();
        _groupByPath.Clear();

        foreach (SavedGroup stored in saved)
        {
            RepositoryGroup group = new(
                stored.Name,
                stored.Glyph.Length > 0 ? stored.Glyph : GroupIcons.Default,
                stored.ColorKey.Length > 0 ? stored.ColorKey : GroupPalette.Default)
            {
                IsExpanded = stored.IsExpanded
            };

            Groups.Add(group);

            foreach (string path in stored.Repositories)
            {
                _groupByPath[path] = group.Name;
            }
        }
    }

    private List<SavedGroup> CaptureGroups() =>
    [
        .. Groups.Select(group => new SavedGroup
        {
            Name = group.Name,
            Glyph = group.Glyph,
            ColorKey = group.ColorKey,
            IsExpanded = group.IsExpanded,
            Repositories = _groupByPath
                .Where(pair => string.Equals(pair.Value, group.Name, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToList()
        })
    ];

    /// <summary>Drops a repository from the recent list without touching anything on disk.</summary>
    public void RemoveRecent(string path)
    {
        string? existing = Recent.FirstOrDefault(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            Recent.Remove(existing);
        }

        RecentRepository? entry = RecentRepositories.FirstOrDefault(candidate => candidate.Path == path);

        if (entry is not null)
        {
            RecentRepositories.Remove(entry);
        }
    }

    /// <summary>Moves a repository to the front of the recent list, capped at ten.</summary>
    private void AddRecent(string workingDir)
    {
        string? existing = Recent.FirstOrDefault(path => string.Equals(path, workingDir, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Recent.Remove(existing);
        }

        Recent.Insert(0, workingDir);

        while (Recent.Count > MaxRecent)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }
    }

    /// <summary>
    ///  How many repositories Home remembers.
    /// </summary>
    /// <remarks>
    ///  This was ten when the list was purely most-recently-used. Projects changed what the list is
    ///  for: it is now the set of repositories you work on, organised into sections, and the setup
    ///  wizard imports a whole folder of them in one go. Ten would silently throw most of an import
    ///  away.
    /// </remarks>
    private const int MaxRecent = 60;

    /// <summary>
    ///  Adds repositories found by the setup wizard to Home, creating the projects they go into.
    /// </summary>
    /// <remarks>
    ///  None of them are opened. Importing thirty repositories and having thirty tabs appear would be
    ///  a worse first impression than the empty shell this is meant to fix; they are on Home, which is
    ///  where opening one starts from anyway.
    /// </remarks>
    public void ImportRepositories(
        IReadOnlyList<(string Path, string ProjectName)> repositories,
        IReadOnlyList<(string Name, string ColorKey)> projects)
    {
        Dictionary<string, RepositoryGroup> created = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, string colorKey) in projects)
        {
            if (name.Length == 0 || created.ContainsKey(name))
            {
                continue;
            }

            RepositoryGroup? existing = Groups.FirstOrDefault(
                group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));

            created[name] = existing ?? CreateGroup(name, GroupIcons.Default, colorKey);
        }

        // Added oldest first, because each one goes to the front of the list.
        foreach ((string path, string _) in repositories.Reverse())
        {
            AddRecent(path);
        }

        foreach ((string path, string projectName) in repositories)
        {
            if (projectName.Length > 0 && created.TryGetValue(projectName, out RepositoryGroup? group))
            {
                _groupByPath[path] = group.Name;
            }
        }

        Regroup();
    }

    // ---- Per-repository state --------------------------------------------------------------------
    // Keyed by working directory, so it follows a repository across being closed and reopened, and
    // across tabs being reordered by their project.

    private readonly Dictionary<string, SavedRepositoryState> _savedStates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Restores how a repository was last left, if this session has seen it before.</summary>
    private void ApplySavedState(RepositoryTabViewModel tab)
    {
        if (!_savedStates.TryGetValue(tab.WorkingDir, out SavedRepositoryState? saved))
        {
            return;
        }

        tab.LastSection = saved.Section;
        tab.Changes.Message = saved.CommitDraft;
        tab.HistoryDiff.IsSideBySide = saved.DiffSideBySide;

        if (saved.AllBranches)
        {
            // Set directly rather than through Query, whose setter triggers a reload the caller is
            // about to perform anyway.
            tab.SetInitialScope(RevisionScope.AllBranches);
        }
    }

    /// <summary>
    ///  Records the state of every repository this session knows about.
    /// </summary>
    /// <remarks>
    ///  Open repositories are read from their tabs; ones since closed keep whatever was recorded when
    ///  they were, so closing a repository does not throw away how it was arranged.
    /// </remarks>
    private List<SavedRepositoryState> CaptureRepositoryStates()
    {
        foreach (RepositoryTabViewModel tab in Repositories)
        {
            _savedStates[tab.WorkingDir] = new SavedRepositoryState
            {
                Path = tab.WorkingDir,
                Section = tab.LastSection,
                CommitDraft = tab.Changes.Message,
                DiffSideBySide = tab.HistoryDiff.IsSideBySide,
                AllBranches = tab.Query.Scope == RevisionScope.AllBranches
            };
        }

        return [.. _savedStates.Values];
    }

    public void CloseTab(ShellTab tab)
    {
        if (tab is not RepositoryTabViewModel repository)
        {
            return;
        }

        repository.Close();
        Tabs.Remove(repository);

        // Fall back to Home rather than to nothing, so the shell always has something selected.
        if (ReferenceEquals(SelectedTab, repository))
        {
            SelectedTab = Tabs.LastOrDefault() ?? Home;
        }
    }

    /// <summary>
    ///  Reopens the tabs and mode from the previous run. Repositories that have since been moved or
    ///  deleted are skipped silently rather than reported — they're stale state, not user errors.
    /// </summary>
    public async Task RestoreSessionAsync(SessionState state)
    {
        Mode = state.Mode == UiMode.Zen ? UiMode.Simple : state.Mode;

        RestoreGroups(state.Groups);

        foreach (SavedRepositoryState saved in state.RepositoryStates)
        {
            _savedStates[saved.Path] = saved;
        }

        foreach (string recent in state.Recent)
        {
            Recent.Add(recent);
        }

        foreach (string workingDir in state.Repositories)
        {
            if (IsValidRepository(workingDir))
            {
                await OpenRepositoryAsync(workingDir);
            }
        }

        List<RepositoryTabViewModel> opened = [.. Repositories];

        // The stored index counts repositories, not strip entries, and Home occupies the first slot.
        SelectedTab = state.SelectedIndex >= 0 && state.SelectedIndex < opened.Count
            ? opened[state.SelectedIndex]
            : Home;
    }

    public SessionState CaptureSession(WindowBounds? window)
    {
        List<RepositoryTabViewModel> opened = [.. Repositories];

        return new SessionState
        {
            // Zen is a transient view state, not something to be trapped in on next launch.
            Mode = Mode == UiMode.Zen ? _modeBeforeZen : Mode,
            Repositories = opened.Select(tab => tab.WorkingDir).ToList(),
            SelectedIndex = SelectedRepository is null ? -1 : opened.IndexOf(SelectedRepository),
            Recent = Recent.ToList(),
            Groups = CaptureGroups(),
            RepositoryStates = CaptureRepositoryStates(),
            Window = window
        };
    }

    /// <summary>
    ///  Toggles Zen, restoring whichever mode was active before it was entered.
    /// </summary>
    public void ToggleZen()
    {
        if (Mode == UiMode.Zen)
        {
            Mode = _modeBeforeZen;
            RaiseChromeChanged();
            return;
        }

        _modeBeforeZen = Mode;
        Mode = UiMode.Zen;
        RaiseChromeChanged();
    }

    public void ExitZen()
    {
        if (Mode == UiMode.Zen)
        {
            Mode = _modeBeforeZen;
            RaiseChromeChanged();
        }
    }

    /// <summary>Zen hides the strip and the column, both of which depend on the mode as well as the layout.</summary>
    private void RaiseChromeChanged()
    {
        OnPropertyChanged(nameof(TabStripVisibility));
        OnPropertyChanged(nameof(SidebarVisibility));
    }
}
