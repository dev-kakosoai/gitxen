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
///  The shell: the set of open repository tabs and the current <see cref="UiMode"/>.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IGitExecutorProvider _executorProvider;
    private UiMode _mode = UiMode.Simple;
    private UiMode _modeBeforeZen = UiMode.Simple;
    private RepositoryTabViewModel? _selectedTab;

    private readonly RepositoryCreator _creator;

    public MainViewModel(ServiceContainer serviceContainer)
    {
        _executorProvider = serviceContainer.GetRequiredService<IGitExecutorProvider>();
        _creator = new RepositoryCreator(_executorProvider);
    }

    public ObservableCollection<RepositoryTabViewModel> Tabs { get; } = [];

    public RepositoryTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public UiMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value))
            {
                return;
            }

            foreach (RepositoryTabViewModel tab in Tabs)
            {
                tab.Mode = value;
            }

            OnPropertyChanged(nameof(ToolbarVisibility));
            OnPropertyChanged(nameof(IsZen));
            OnPropertyChanged(nameof(IsAdvancedSelected));
            OnPropertyChanged(nameof(AdvancedVisibility));
            OnPropertyChanged(nameof(IsAddTabButtonVisible));
        }
    }

    /// <summary>Zen hides the toolbar entirely — the commit list is all that's left.</summary>
    public Visibility ToolbarVisibility => Mode == UiMode.Zen ? Visibility.Collapsed : Visibility.Visible;

    public bool IsZen => Mode == UiMode.Zen;

    /// <summary>Drives the Simple/Advanced toggle; Zen isn't represented there.</summary>
    public bool IsAdvancedSelected => Mode == UiMode.Advanced;

    public Visibility EmptyStateVisibility => Tabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    ///  The TabView is collapsed rather than merely empty when nothing is open: an empty TabView still
    ///  draws its strip and content border, which would frame the empty state instead of getting out
    ///  of its way.
    /// </summary>
    public Visibility TabsVisibility => Tabs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Toolbar entries that only Advanced mode exposes (Fetch, Push, branch/stash menu).</summary>
    public Visibility AdvancedVisibility => Mode == UiMode.Advanced ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Zen drops the new-tab affordance along with the rest of the chrome.</summary>
    public bool IsAddTabButtonVisible => Mode != UiMode.Zen;

    /// <summary>Most-recently-opened repositories, newest first.</summary>
    public ObservableCollection<string> Recent { get; } = [];

    public bool IsValidRepository(string path) => GitModule.IsValidGitWorkingDir(path);

    /// <summary>
    ///  Opens <paramref name="workingDir"/> in a new tab and starts loading it. If the repository is
    ///  already open, the existing tab is selected instead of duplicating it.
    /// </summary>
    public async Task OpenRepositoryAsync(string workingDir)
    {
        RepositoryTabViewModel? existing = Tabs.FirstOrDefault(
            tab => string.Equals(tab.WorkingDir, workingDir, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        RepositoryTabViewModel tab = new(_executorProvider, workingDir, Mode);
        Tabs.Add(tab);
        SelectedTab = tab;
        AddRecent(workingDir);
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(TabsVisibility));

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

    /// <summary>Moves a repository to the front of the recent list, capped at ten.</summary>
    private void AddRecent(string workingDir)
    {
        string? existing = Recent.FirstOrDefault(path => string.Equals(path, workingDir, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Recent.Remove(existing);
        }

        Recent.Insert(0, workingDir);

        while (Recent.Count > 10)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }
    }

    public void CloseTab(RepositoryTabViewModel tab)
    {
        tab.Close();
        Tabs.Remove(tab);
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(TabsVisibility));
    }

    /// <summary>
    ///  Reopens the tabs and mode from the previous run. Repositories that have since been moved or
    ///  deleted are skipped silently rather than reported — they're stale state, not user errors.
    /// </summary>
    public async Task RestoreSessionAsync(SessionState state)
    {
        Mode = state.Mode == UiMode.Zen ? UiMode.Simple : state.Mode;

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

        if (state.SelectedIndex >= 0 && state.SelectedIndex < Tabs.Count)
        {
            SelectedTab = Tabs[state.SelectedIndex];
        }
    }

    public SessionState CaptureSession(WindowBounds? window)
    {
        return new SessionState
        {
            // Zen is a transient view state, not something to be trapped in on next launch.
            Mode = Mode == UiMode.Zen ? _modeBeforeZen : Mode,
            Repositories = Tabs.Select(tab => tab.WorkingDir).ToList(),
            SelectedIndex = SelectedTab is null ? 0 : Tabs.IndexOf(SelectedTab),
            Recent = Recent.ToList(),
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
            return;
        }

        _modeBeforeZen = Mode;
        Mode = UiMode.Zen;
    }

    public void ExitZen()
    {
        if (Mode == UiMode.Zen)
        {
            Mode = _modeBeforeZen;
        }
    }
}
