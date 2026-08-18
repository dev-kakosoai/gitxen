using System.Collections.ObjectModel;
using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Views;

/// <summary>One repository the wizard is importing, and the project it goes into.</summary>
/// <param name="Path">The working directory.</param>
/// <param name="ProjectName">The project name, or empty for no project.</param>
public sealed record SetupImport(string Path, string ProjectName);

/// <summary>What the shell has to act on once the wizard is finished.</summary>
/// <remarks>
///  Only the repositories and projects: everything else the wizard collects is a setting, and it has
///  already written those to <see cref="AppOptions"/> and to git's global configuration. Creating
///  groups and opening repositories is the shell's job, so those come back as data instead.
/// </remarks>
/// <param name="Repositories">Repositories to add to Home, with their project.</param>
/// <param name="Projects">Projects to create, with the colour they were proposed in.</param>
/// <param name="Layout">Tabs or the repository column.</param>
/// <param name="Mode">Simple or Advanced.</param>
public sealed record SetupOutcome(
    IReadOnlyList<SetupImport> Repositories,
    IReadOnlyList<ProposedProject> Projects,
    RepositoryLayout Layout,
    UiMode Mode);

/// <summary>
///  The first-run wizard: identity, appearance, behaviour, and importing the repositories that are
///  already on the machine.
/// </summary>
/// <remarks>
///  <para>
///   A first run currently drops someone into an empty shell with a Home page that says nothing, no
///   git identity (so the first commit fails), and no idea that projects or the layout options
///   exist. This walks through exactly those things once.
///  </para>
///  <para>
///   Steps are panels switched by visibility rather than pages in a Frame, for the same reason the
///   repository sections are: going back a step must not discard what was typed into the step you
///   are returning to.
///  </para>
/// </remarks>
public sealed partial class SetupWizardView : UserControl
{
    private const int LastStep = 6;
    private const int RepositoriesStepIndex = 4;
    private const int ProjectsStepIndex = 5;

    private readonly ObservableCollection<DiscoveredRepository> _discovered = [];
    private readonly ObservableCollection<ProposedProject> _projects = [];

    /// <summary>The theme entries, in the order the dropdown lists them.</summary>
    private IReadOnlyList<ThemeOption> _themes = [];

    private GlobalGitSettings? _git;
    private Func<Task<string?>>? _pickFolder;
    private CancellationTokenSource? _scan;
    private bool _projectDefaultApplied;
    private int _step;

    public SetupWizardView()
    {
        InitializeComponent();

        RepositoryList.ItemsSource = _discovered;
        ProjectList.ItemsSource = _projects;

        NoProjectsOption.IsChecked = true;
    }

    /// <summary>Raised when the theme is changed, so the shell can apply it while the wizard is open.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>Raised when the wizard finishes. Skipping raises it with nothing to import.</summary>
    public event EventHandler<SetupOutcome>? Completed;

    /// <summary>
    ///  Supplies what the wizard cannot reach on its own.
    /// </summary>
    /// <param name="git">Reads and writes the global git configuration.</param>
    /// <param name="mode">The mode in force, so the wizard offers it rather than overriding it.</param>
    /// <param name="pickFolder">
    ///  Shows the folder picker. It has to be initialised with the window handle, which a
    ///  <see cref="UserControl"/> does not have, so the shell provides it.
    /// </param>
    /// <remarks>
    ///  Internal because <see cref="GlobalGitSettings"/> is: the services in this project are not
    ///  part of its public surface, and only the shell calls this.
    /// </remarks>
    internal void Initialize(GlobalGitSettings git, UiMode mode, Func<Task<string?>> pickFolder)
    {
        _git = git;
        _pickFolder = pickFolder;

        SidebarLayoutOption.IsChecked = AppOptions.Layout == RepositoryLayout.Sidebar;
        TabsLayoutOption.IsChecked = AppOptions.Layout != RepositoryLayout.Sidebar;
        AdvancedModeOption.IsChecked = mode == UiMode.Advanced;
        SimpleModeOption.IsChecked = mode != UiMode.Advanced;

        // A dropdown here rather than the settings page's gallery: the wizard is a narrow column of
        // one-line decisions, and the theme can be looked at properly in Settings afterwards.
        _themes = ThemeOption.Build(ThemeService.IsSystemDark);
        ThemeBox.ItemsSource = _themes.Select(option => option.Name);
        ThemeBox.SelectedIndex = Math.Max(0, _themes.ToList().FindIndex(
            option => option.Theme == AppOptions.Theme));

        SelectByTag(AutoFetchBox, AppOptions.AutoFetchMinutes.ToString(), fallbackIndex: 2);
        SelectByTag(CommitsBox, AppOptions.MaxCommits.ToString(), fallbackIndex: 1);
        SelectByTag(DepthBox, RepositoryScanner.DefaultMaxDepth.ToString(), fallbackIndex: 1);

        _ = LoadGitDetailsAsync();
    }

    /// <summary>
    ///  Reads git's version and the existing global identity.
    /// </summary>
    /// <remarks>
    ///  Off the UI thread: each of these starts a git process, and three of them on the dispatcher is
    ///  a visible stall on the wizard's very first frame.
    /// </remarks>
    private async Task LoadGitDetailsAsync()
    {
        if (_git is not GlobalGitSettings git)
        {
            return;
        }

        (string version, string name, string email, string branch) = await Task.Run(() =>
            (git.GetVersion(), git.Get("user.name"), git.Get("user.email"), git.Get("init.defaultBranch")));

        if (version.Length > 0)
        {
            GitStatusGlyph.Glyph = "\uE73E";
            GitStatusText.Text = "git is installed";
            GitStatusDetail.Text = version;
        }
        else
        {
            GitStatusGlyph.Glyph = "\uE783";
            GitStatusText.Text = "git was not found";
            GitStatusDetail.Text =
                "Gitxen runs the real git, so almost nothing will work until it is installed and on PATH. "
                + "You can finish this wizard and install it afterwards.";
        }

        UserNameBox.Text = name;
        UserEmailBox.Text = email;

        if (branch.Length > 0)
        {
            DefaultBranchBox.Text = branch;
        }
    }

    // ---- Navigation ------------------------------------------------------------------------------

    private void Back_Click(object sender, RoutedEventArgs e) => GoTo(PreviousStep());

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == LastStep)
        {
            await FinishAsync();
            return;
        }

        GoTo(NextStep());
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        // Nothing is written and nothing is imported, but the wizard is still marked done by the
        // shell: being asked again on every launch after saying no is the worst of both.
        CancelScan();
        Completed?.Invoke(this, new SetupOutcome([], [], AppOptions.Layout, CurrentMode()));
    }

    /// <summary>
    ///  The projects step is skipped when there is nothing to organise.
    /// </summary>
    /// <remarks>
    ///  Asking how to group repositories when none were imported is a step that can only be answered
    ///  one way, and it makes the wizard look longer than it is.
    /// </remarks>
    private int NextStep() =>
        _step + 1 == ProjectsStepIndex && SelectedRepositories().Count == 0
            ? ProjectsStepIndex + 1
            : _step + 1;

    private int PreviousStep() =>
        _step - 1 == ProjectsStepIndex && SelectedRepositories().Count == 0
            ? ProjectsStepIndex - 1
            : _step - 1;

    private void GoTo(int step)
    {
        _step = Math.Clamp(step, 0, LastStep);

        WelcomeStep.Visibility = VisibleOn(0);
        IdentityStep.Visibility = VisibleOn(1);
        AppearanceStep.Visibility = VisibleOn(2);
        BehaviourStep.Visibility = VisibleOn(3);
        RepositoriesStep.Visibility = VisibleOn(RepositoriesStepIndex);
        ProjectsStep.Visibility = VisibleOn(ProjectsStepIndex);
        FinishStep.Visibility = VisibleOn(LastStep);

        (StepTitle.Text, StepSubtitle.Text) = _step switch
        {
            0 => ("Welcome to Gitxen", "A few questions, then you are set up."),
            1 => ("Who are your commits from?", "Written to your global git configuration."),
            2 => ("How should it look?", "All of this can be changed later in Settings."),
            3 => ("How should it behave?", "Two settings that are easier to choose now than to find later."),
            RepositoriesStepIndex => ("Find your repositories", "Scan a folder and pick what to import."),
            ProjectsStepIndex => ("Organise them", "Projects group repositories that belong together."),
            _ => ("Ready", "Here is what will happen.")
        };

        StepProgress.Value = _step;
        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == LastStep ? "Finish" : "Next";
        SkipButton.Visibility = _step == LastStep ? Visibility.Collapsed : Visibility.Visible;

        if (_step == 1)
        {
            IdentityWarning.IsOpen = UserNameBox.Text.Trim().Length == 0 || UserEmailBox.Text.Trim().Length == 0;
        }

        if (_step == ProjectsStepIndex)
        {
            BuildProposedProjects();
        }

        if (_step == LastStep)
        {
            BuildSummary();
        }
    }

    private Visibility VisibleOn(int index) =>
        _step == index ? Visibility.Visible : Visibility.Collapsed;

    // ---- Appearance ------------------------------------------------------------------------------

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard the call raised while the control is still being constructed, before Initialize.
        if (!IsLoaded && _git is null)
        {
            return;
        }

        AppOptions.Theme = ThemeBox.SelectedIndex >= 0 && ThemeBox.SelectedIndex < _themes.Count
            ? _themes[ThemeBox.SelectedIndex].Theme
            : AppTheme.System;

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Scanning --------------------------------------------------------------------------------

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_pickFolder is null || await _pickFolder() is not string root)
        {
            return;
        }

        await ScanAsync(root);
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e) => CancelScan();

    private void CancelScan()
    {
        _scan?.Cancel();
        _scan = null;
    }

    private async Task ScanAsync(string root)
    {
        CancelScan();

        _discovered.Clear();
        _scan = new CancellationTokenSource();
        CancellationToken token = _scan.Token;

        ChooseFolderButton.IsEnabled = false;
        CancelScanButton.Visibility = Visibility.Visible;
        ScanProgressBar.Visibility = Visibility.Visible;
        SelectionButtons.Visibility = Visibility.Collapsed;
        ScanStatus.Text = $"Scanning {root}...";

        // Progress<T> posts to the context it was created on, which here is the UI thread.
        Progress<ScanProgress> progress = new(report =>
            ScanStatus.Text = report.Found == 1
                ? $"1 repository found, {report.FoldersVisited:N0} folders looked at"
                : $"{report.Found:N0} repositories found, {report.FoldersVisited:N0} folders looked at");

        try
        {
            int depth = TagValue(DepthBox, RepositoryScanner.DefaultMaxDepth);
            IReadOnlyList<DiscoveredRepository> found =
                await RepositoryScanner.ScanAsync(root, depth, progress, token);

            foreach (DiscoveredRepository repository in found)
            {
                _discovered.Add(repository);
            }

            ScanStatus.Text = found.Count switch
            {
                0 => $"No repositories found under {root}. Try looking deeper, or choose another folder.",
                1 => $"1 repository found under {root}.",
                _ => $"{found.Count} repositories found under {root}."
            };
        }
        catch (OperationCanceledException)
        {
            ScanStatus.Text = _discovered.Count == 0
                ? "Scan stopped."
                : $"Scan stopped after finding {_discovered.Count}.";
        }
        catch (Exception ex)
        {
            // A scan is best-effort over a filesystem nobody controls; it should never take the
            // wizard down with it.
            ScanStatus.Text = $"The scan could not be completed: {ex.Message}";
        }
        finally
        {
            ChooseFolderButton.IsEnabled = true;
            CancelScanButton.Visibility = Visibility.Collapsed;
            ScanProgressBar.Visibility = Visibility.Collapsed;
            SelectionButtons.Visibility = _discovered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _scan = null;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllSelected(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAllSelected(false);

    private void SetAllSelected(bool selected)
    {
        foreach (DiscoveredRepository repository in _discovered)
        {
            repository.IsSelected = selected;
        }
    }

    private List<DiscoveredRepository> SelectedRepositories() =>
        [.. _discovered.Where(repository => repository.IsSelected)];

    // ---- Projects --------------------------------------------------------------------------------

    private void ProjectMode_Changed(object sender, RoutedEventArgs e)
    {
        // Raised while the markup is being parsed, before the other controls exist.
        if (ProjectList is null || SingleProjectName is null)
        {
            return;
        }

        ProjectList.Visibility = FolderProjectsOption.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        SingleProjectName.Visibility = SingleProjectOption.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    ///  Works out one project per folder from what was found, and picks the default mode.
    /// </summary>
    /// <remarks>
    ///  The folders someone keeps repositories in are already how they think about them — a client, a
    ///  product, "work" and "personal" — so proposing exactly those beats asking them to invent
    ///  names. It only becomes the default when the scan actually found more than one folder;
    ///  otherwise a project per folder is one project, which is not organising anything.
    /// </remarks>
    private void BuildProposedProjects()
    {
        List<DiscoveredRepository> selected = SelectedRepositories();

        List<IGrouping<string, DiscoveredRepository>> folders =
        [
            .. selected
                .Where(repository => repository.SuggestedProject.Length > 0)
                .GroupBy(repository => repository.SuggestedProject, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        ];

        _projects.Clear();

        IReadOnlyList<string> palette = GroupPalette.Keys;

        for (int index = 0; index < folders.Count; index++)
        {
            _projects.Add(new ProposedProject(
                folders[index].Key,
                palette[index % palette.Count],
                folders[index].Count()));
        }

        FolderProjectsOption.IsEnabled = folders.Count > 0;
        FolderProjectsOption.Content = folders.Count switch
        {
            0 => "A project for each folder (the scan found no subfolders)",
            1 => "A project for each folder (1 project)",
            _ => $"A project for each folder ({folders.Count} projects)"
        };

        // Only the first time the step is reached: going back and forward again must not overwrite
        // what was chosen.
        if (_projectDefaultApplied)
        {
            return;
        }

        _projectDefaultApplied = true;

        if (folders.Count > 1)
        {
            FolderProjectsOption.IsChecked = true;
        }
    }

    /// <summary>Applies the chosen mode to each repository, giving it the project it will go into.</summary>
    private void AssignProjects()
    {
        List<DiscoveredRepository> selected = SelectedRepositories();

        if (SingleProjectOption.IsChecked == true)
        {
            string name = SingleProjectName.Text.Trim();

            foreach (DiscoveredRepository repository in selected)
            {
                repository.ProjectName = name;
            }

            _projects.Clear();

            if (name.Length > 0)
            {
                _projects.Add(new ProposedProject(name, GroupPalette.Default, selected.Count));
            }

            return;
        }

        if (FolderProjectsOption.IsChecked != true)
        {
            foreach (DiscoveredRepository repository in selected)
            {
                repository.ProjectName = "";
            }

            _projects.Clear();
            return;
        }

        // The proposed names are editable, so map each repository's folder back to whatever the row
        // for that folder now says.
        List<ProposedProject> proposed = [.. _projects];
        List<string> folders =
        [
            .. selected
                .Where(repository => repository.SuggestedProject.Length > 0)
                .Select(repository => repository.SuggestedProject)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
        ];

        Dictionary<string, string> renamed = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < folders.Count && index < proposed.Count; index++)
        {
            renamed[folders[index]] = proposed[index].Name.Trim();
        }

        foreach (DiscoveredRepository repository in selected)
        {
            repository.ProjectName =
                renamed.TryGetValue(repository.SuggestedProject, out string? name) ? name : "";
        }
    }

    // ---- Finishing -------------------------------------------------------------------------------

    private void BuildSummary()
    {
        AssignProjects();

        List<DiscoveredRepository> selected = SelectedRepositories();
        int projects = selected
            .Select(repository => repository.ProjectName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        SummaryPanel.Children.Clear();

        AddSummaryLine("\uE77B", UserNameBox.Text.Trim().Length > 0 || UserEmailBox.Text.Trim().Length > 0
            ? $"Commits from {UserNameBox.Text.Trim()} <{UserEmailBox.Text.Trim()}>"
            : "No git identity set — you will be asked again before your first commit");

        AddSummaryLine("\uE771", $"{ThemeName()} theme, {(SidebarLayoutOption.IsChecked == true ? "repository column" : "tabs")}, "
            + $"{(AdvancedModeOption.IsChecked == true ? "Advanced" : "Simple")} mode");

        int minutes = TagValue(AutoFetchBox, AppOptions.AutoFetchMinutes);
        AddSummaryLine("\uE895", minutes == 0
            ? "No background fetching"
            : $"Fetching every {minutes} minutes");

        AddSummaryLine("\uE8B7", selected.Count switch
        {
            0 => "No repositories imported",
            1 => "1 repository added to Home",
            _ => $"{selected.Count} repositories added to Home"
        });

        if (projects > 0)
        {
            AddSummaryLine("\uE821", projects == 1 ? "1 project created" : $"{projects} projects created");
        }
    }

    private void AddSummaryLine(string glyph, string text)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 12 };

        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });

        SummaryPanel.Children.Add(row);
    }

    private UiMode CurrentMode() =>
        AdvancedModeOption.IsChecked == true ? UiMode.Advanced : UiMode.Simple;

    private string ThemeName() => ThemeBox.SelectedIndex switch
    {
        1 => "Light",
        2 => "Dark",
        _ => "System"
    };

    private async Task FinishAsync()
    {
        NextButton.IsEnabled = false;
        BackButton.IsEnabled = false;

        // The layout and the mode are applied by the shell through its view model, which is what
        // raises the notifications the strip and the column are bound to; writing AppOptions here
        // would change the value without anything redrawing.
        AppOptions.AutoFetchMinutes = TagValue(AutoFetchBox, AppOptions.AutoFetchMinutes);
        AppOptions.MaxCommits = TagValue(CommitsBox, AppOptions.MaxCommits);

        await WriteGitConfigurationAsync();

        AssignProjects();

        List<SetupImport> imports =
        [
            .. SelectedRepositories().Select(repository => new SetupImport(repository.Path, repository.ProjectName))
        ];

        List<ProposedProject> projects =
        [
            .. _projects.Where(project => project.Name.Trim().Length > 0)
        ];

        Completed?.Invoke(this, new SetupOutcome(
            imports,
            projects,
            SidebarLayoutOption.IsChecked == true ? RepositoryLayout.Sidebar : RepositoryLayout.Tabs,
            CurrentMode()));
    }

    /// <summary>
    ///  Writes the identity to git's global configuration.
    /// </summary>
    /// <remarks>
    ///  Failures are deliberately not reported. This runs three <c>git config</c> writes as the last
    ///  act of a wizard that is about to close; a modal error about one of them, at that moment, tells
    ///  someone nothing they can act on. The values are visible and editable afterwards in Settings,
    ///  where a failure to write does surface.
    /// </remarks>
    private async Task WriteGitConfigurationAsync()
    {
        if (_git is not GlobalGitSettings git)
        {
            return;
        }

        string name = UserNameBox.Text.Trim();
        string email = UserEmailBox.Text.Trim();
        string branch = DefaultBranchBox.Text.Trim();

        await Task.Run(() =>
        {
            // Only write what was actually filled in: clearing an existing global identity because a
            // box was left empty would be a destructive answer to a question nobody asked.
            if (name.Length > 0)
            {
                git.Set("user.name", name);
            }

            if (email.Length > 0)
            {
                git.Set("user.email", email);
            }

            if (branch.Length > 0)
            {
                git.Set("init.defaultBranch", branch);
            }
        });
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static void SelectByTag(ComboBox box, string tag, int fallbackIndex)
    {
        for (int index = 0; index < box.Items.Count; index++)
        {
            if (box.Items[index] is ComboBoxItem item && (item.Tag as string) == tag)
            {
                box.SelectedIndex = index;
                return;
            }
        }

        box.SelectedIndex = fallbackIndex;
    }

    private static int TagValue(ComboBox box, int fallback) =>
        box.SelectedItem is ComboBoxItem item
        && item.Tag is string tag
        && int.TryParse(tag, out int value)
            ? value
            : fallback;
}
