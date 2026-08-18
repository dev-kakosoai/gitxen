using System.ComponentModel.Design;
using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.Theming;
using GitExtensions.WinUI.ViewModels;
using GitExtensions.WinUI.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GitExtensions.WinUI;

/// <summary>
///  The shell: a custom title bar, the open repositories as tabs, and the empty state.
/// </summary>
/// <remarks>
///  Everything that acts on a repository now lives in <see cref="Views.RepositoryView"/> and the
///  section pages under it, so this window is left with only what is genuinely window-scoped —
///  opening and closing repositories, the UI mode, the theme, and session persistence.
/// </remarks>
public sealed partial class MainWindow : Window
{
    /// <summary>
    ///  Drives the periodic background fetch.
    /// </summary>
    /// <remarks>
    ///  Owned by the window rather than by each repository so that the interval is applied once and
    ///  every open tab is refreshed on the same tick, instead of each keeping its own timer running.
    /// </remarks>
    private DispatcherQueueTimer? _autoFetchTimer;

    /// <summary>
    ///  Saves the session periodically rather than only on close.
    /// </summary>
    /// <remarks>
    ///  Saving on close alone means a crash, a forced quit or a machine losing power throws away
    ///  everything arranged since launch — the groups, the layout, a half-written commit message.
    ///  A periodic save bounds that loss to the interval instead.
    /// </remarks>
    private DispatcherQueueTimer? _sessionSaveTimer;

    public MainWindow(ServiceContainer serviceContainer)
    {
        ViewModel = new MainViewModel(serviceContainer);
        InitializeComponent();

        Title = "Gitxen";
        SetWindowIcon();

        // Draw into the title bar so the window reads as one surface rather than a WinUI app wearing a
        // system caption. The drag region is the empty strip left of the caption buttons.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);

        // The grouped source is a resource, so it cannot be bound with x:Bind from the markup.
        ((CollectionViewSource)RootGrid.Resources["GroupedTabsSource"]).Source = ViewModel.TabGroups;

        // The column's and the strip's highlights are driven from here rather than bound: rebuilding
        // their lists clears their selections, and selecting a tab elsewhere has to be reflected too.
        // While a rebuild runs, its selection-changed noise is ignored; the sync at the end resets it.
        ViewModel.TabGroupsRebuilding += (_, _) => _syncingSidebarSelection = true;
        ViewModel.TabGroupsRebuilt += (_, _) => SyncRepositorySelection();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab))
            {
                SyncRepositorySelection();
            }
        };

        SyncRepositorySelection();

        // Takes over the backdrop, the root element's theme and the system caption buttons. The
        // palette itself is already registered; this is the part that needs a window to exist.
        ThemeService.Attach(this, RootGrid);

        Closed += MainWindow_Closed;
    }

    public MainViewModel ViewModel { get; }

    /// <summary>
    ///  Restores the previous session. Called after the window is shown so the tabs stream in visibly
    ///  rather than delaying first paint.
    /// </summary>
    /// <param name="state">
    ///  The session, already read by <c>App</c> so that the theme could be applied before the window
    ///  was built.
    /// </param>
    public async Task RestoreSessionAsync(SessionState state)
    {
        // Nothing awaits this, so an escaping exception would vanish without trace and simply leave
        // the shell looking like a first run. Record it instead.
        try
        {
            // The column keeps whatever width it was dragged to.
            if (state.SidebarWidth > 120)
            {
                Sidebar.Width = state.SidebarWidth;
            }

            if (state.Window is WindowBounds bounds && bounds.Width > 0 && bounds.Height > 0)
            {
                AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            }

            await ViewModel.RestoreSessionAsync(state);

            SyncModeBar();
            StartAutoFetch();
            StartSessionSaves();
            await Home.RefreshAsync();

            _hasCompletedSetup = state.HasCompletedSetup;

            if (!_hasCompletedSetup)
            {
                ShowSetupWizard();
            }
        }
        catch (Exception ex)
        {
            SessionStore.LogRestoreFailure(ex);
        }
    }

    private void StartAutoFetch()
    {
        _autoFetchTimer?.Stop();

        if (AppOptions.AutoFetchMinutes <= 0)
        {
            return;
        }

        _autoFetchTimer = DispatcherQueue.CreateTimer();
        _autoFetchTimer.Interval = TimeSpan.FromMinutes(AppOptions.AutoFetchMinutes);

        // Not an async lambda: an exception escaping a void-returning delegate would crash the
        // process, so the work goes through a method that handles its own failures.
        _autoFetchTimer.Tick += (_, _) => _ = FetchAllQuietlyAsync();
        _autoFetchTimer.Start();
    }

    /// <summary>
    ///  Puts the application icon on the window itself.
    /// </summary>
    /// <remarks>
    ///  ApplicationIcon only embeds the icon in the executable, which the shell reads for the
    ///  file and the shortcut. The taskbar button and Alt+Tab read the icon the window carries,
    ///  and an unpackaged WinUI window has none until it is given one.
    /// </remarks>
    private void SetWindowIcon()
    {
        try
        {
            string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "gitxen.ico");

            if (File.Exists(icon))
            {
                AppWindow.SetIcon(icon);
            }
        }
        catch (Exception)
        {
            // A missing or unreadable icon is not worth refusing to start over.
        }
    }

    private void StartSessionSaves()
    {
        _sessionSaveTimer?.Stop();
        _sessionSaveTimer = DispatcherQueue.CreateTimer();
        _sessionSaveTimer.Interval = TimeSpan.FromSeconds(30);
        _sessionSaveTimer.Tick += (_, _) => SaveSession();
        _sessionSaveTimer.Start();
    }

    /// <summary>Writes the session as it stands, including the window's current bounds.</summary>
    private void SaveSession()
    {
        SessionState state = ViewModel.CaptureSession(new WindowBounds
        {
            X = AppWindow.Position.X,
            Y = AppWindow.Position.Y,
            Width = AppWindow.Size.Width,
            Height = AppWindow.Size.Height
        });

        AppOptions.CopyTo(state);
        state.SidebarWidth = Sidebar.Width;
        state.HasCompletedSetup = _hasCompletedSetup;
        SessionStore.Save(state);
    }

    private async Task FetchAllQuietlyAsync()
    {
        foreach (RepositoryTabViewModel tab in ViewModel.Repositories.ToList())
        {
            await tab.AutoFetchAsync();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _sessionSaveTimer?.Stop();
        SaveSession();
    }

    private async void RepositoryTabs_AddTabButtonClick(TabView sender, object args) =>
        await PickAndOpenRepositoryAsync();

    private async void AddRepository_Click(object sender, RoutedEventArgs e) =>
        await PickAndOpenRepositoryAsync();

    /// <summary>
    ///  Runs an action the Home page asked for. These need a window handle for their file pickers,
    ///  which only the window has.
    /// </summary>
    private async void Home_ActionRequested(object? sender, HomeAction action)
    {
        switch (action)
        {
            case HomeAction.OpenRepository:
                await PickAndOpenRepositoryAsync();
                break;

            case HomeAction.Clone:
                await CloneAsync();
                break;

            case HomeAction.Initialise:
                await InitAsync();
                break;

            case HomeAction.RunSetup:
                ShowSetupWizard();
                break;
        }
    }

    private async void Home_OpenRequested(object? sender, string path) => await OpenRecentAsync(path);

    // ---- Repository column -----------------------------------------------------------------------

    /// <summary>The repository being dragged between projects in the column.</summary>
    private SidebarRow? _draggingRow;

    /// <summary>
    ///  Suppresses the selection handler while the selection is being set from the view model, so a
    ///  programmatic sync is not mistaken for a click.
    /// </summary>
    private bool _syncingSidebarSelection;

    private void SelectHome_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedTab = ViewModel.Home;

    /// <summary>
    ///  A click in the column: switch to the repository when it is open, open it when it is not.
    /// </summary>
    /// <remarks>
    ///  Selection is driven from code rather than a two-way binding: the rows are not the tabs, and
    ///  rebuilding the sections clears the list's selection — a binding would write that clearance
    ///  into <see cref="MainViewModel.SelectedTab"/> and bounce the shell back to Home.
    /// </remarks>
    private async void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingSidebarSelection && SidebarList.SelectedItem is SidebarRow row)
        {
            await SelectRowAsync(row);
        }
    }

    /// <summary>A click in the strip: Home, an open repository, or one to open. Same as the column.</summary>
    private async void RepositoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingSidebarSelection && RepositoryTabs.SelectedItem is SidebarRow row)
        {
            await SelectRowAsync(row);
        }
    }

    private async Task SelectRowAsync(SidebarRow row)
    {
        if (row.IsHome)
        {
            ViewModel.SelectedTab = ViewModel.Home;
            return;
        }

        if (row.OpenTab is RepositoryTabViewModel tab)
        {
            ViewModel.SelectedTab = tab;
            return;
        }

        await OpenRecentAsync(row.Path);

        // On success the rebuild has already re-selected the new tab's row; on failure this puts the
        // highlight back where it belongs instead of leaving it on a repository that did not open.
        SyncRepositorySelection();
    }

    /// <summary>Points the column's and the strip's highlights at the selected tab's row, if any.</summary>
    private void SyncRepositorySelection()
    {
        _syncingSidebarSelection = true;

        ShellTab? selected = ViewModel.SelectedTab;

        SidebarList.SelectedItem = selected is RepositoryTabViewModel
            ? ViewModel.TabGroups
                .SelectMany(section => section.Items)
                .FirstOrDefault(row => ReferenceEquals(row.OpenTab, selected))
            : null;

        RepositoryTabs.SelectedItem = ViewModel.StripRows.FirstOrDefault(row =>
            selected is HomeTabViewModel ? row.IsHome : ReferenceEquals(row.OpenTab, selected));

        _syncingSidebarSelection = false;
    }

    private void ToggleSidebarGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ShellTabGroup { Group: RepositoryGroup group })
        {
            ViewModel.ToggleGroupExpanded(group);
        }
    }

    /// <summary>
    ///  The whole header collapses its section, not only the chevron. Clicks on the buttons the header
    ///  hosts do not reach here — they handle their own pointer events.
    /// </summary>
    private void SidebarGroupHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ShellTabGroup { Group: RepositoryGroup group })
        {
            ViewModel.ToggleGroupExpanded(group);
        }
    }

    private void SidebarItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        _draggingRow = (sender as FrameworkElement)?.DataContext as SidebarRow;

        // Something has to be offered or the drop targets never light up.
        args.Data.SetText(_draggingRow?.Path ?? "");
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void SidebarGroup_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _draggingRow is null ? DataPackageOperation.None : DataPackageOperation.Move;
        e.DragUIOverride.Caption = "Move to this project";
        e.DragUIOverride.IsGlyphVisible = false;
    }

    /// <summary>
    ///  Drops a repository onto a project header in the column.
    /// </summary>
    /// <remarks>
    ///  Goes through the same assignment the Home page uses, so a repository moved here is moved
    ///  everywhere — the column, the tab order and the Home page all read the one membership.
    /// </remarks>
    private void SidebarGroup_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ShellTabGroup section
            || _draggingRow is not SidebarRow row)
        {
            return;
        }

        _draggingRow = null;
        ViewModel.AssignToGroup(row.Path, section.Group);
    }

    private void CloseRepository_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SidebarRow { OpenTab: RepositoryTabViewModel tab } })
        {
            ViewModel.CloseTab(tab);
        }
    }

    /// <summary>
    ///  Clones a repository into a folder the user picks, then opens it.
    /// </summary>
    /// <remarks>
    ///  The folder picker chooses the <em>parent</em>: git creates the repository directory itself, and
    ///  cloning into an existing non-empty folder fails, so asking for the destination directly would
    ///  only invite that error.
    /// </remarks>
    private async Task CloneAsync()
    {
        TextBox url = new() { Header = "Repository URL", PlaceholderText = "https://github.com/owner/repo.git" };
        TextBox folder = new() { Header = "Folder name (optional)", PlaceholderText = "taken from the URL" };
        CheckBox submodules = new() { Content = "Also clone submodules", IsChecked = true };

        StackPanel panel = new() { Spacing = 12, Width = 460 };
        panel.Children.Add(url);
        panel.Children.Add(folder);
        panel.Children.Add(submodules);

        ContentDialog dialog = new()
        {
            Title = "Clone repository",
            Content = panel,
            PrimaryButtonText = "Choose folder…",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(url.Text))
        {
            return;
        }

        if (await PickFolderAsync() is not string parent)
        {
            return;
        }

        GitOperationResult result = await ViewModel.CloneAsync(
            url.Text.Trim(), parent, folder.Text.Trim(), submodules.IsChecked == true);

        if (result.Succeeded)
        {
            await Home.RefreshAsync();
        }
        else
        {
            await ShowMessageAsync("Clone failed", result.Output);
        }
    }

    private async Task InitAsync()
    {
        if (await PickFolderAsync() is not string directory)
        {
            return;
        }

        GitOperationResult result = await ViewModel.InitAsync(directory, bare: false);

        if (result.Succeeded)
        {
            await Home.RefreshAsync();
        }
        else
        {
            await ShowMessageAsync("Could not initialise", result.Output);
        }
    }

    private async Task OpenRecentAsync(string path)
    {
        try
        {
            await OpenRepositoryPathAsync(path);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Could not open repository", ex.Message);
        }
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = "Open repository";
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();

        foreach (IStorageItem item in items.OfType<StorageFolder>())
        {
            await OpenRepositoryPathAsync(item.Path);
        }
    }

    private void RepositoryTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is SidebarRow { OpenTab: RepositoryTabViewModel tab })
        {
            ViewModel.CloseTab(tab);
        }
    }

    private void CloseTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ViewModel.SelectedTab is RepositoryTabViewModel tab)
        {
            ViewModel.CloseTab(tab);
        }
    }

    private async void OpenRepositoryAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await PickAndOpenRepositoryAsync();
    }

    private async void RefreshAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ViewModel.SelectedTab is RepositoryTabViewModel tab)
        {
            await tab.LoadAsync();
        }
    }

    /// <summary>
    ///  Opens the command palette. Window-level so it works from any page, which is the whole point of
    ///  a palette.
    /// </summary>
    private async void PaletteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        // The palette acts on a repository, so it has nothing to offer while Home is selected.
        if (ViewModel.SelectedRepository is not null)
        {
            await Repository.ShowPaletteAsync();
        }
    }

    /// <summary>Ctrl+T: the go-to palette, which needs a repository for the same reason as Ctrl+Shift+P.</summary>
    private async void GoToAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (ViewModel.SelectedRepository is not null)
        {
            await Repository.ShowGoToAsync();
        }
    }

    /// <summary>
    ///  Lists the keyboard shortcuts.
    /// </summary>
    /// <remarks>
    ///  Shortcuts that cannot be discovered are shortcuts nobody uses. F1 rather than the more
    ///  fashionable Ctrl+/ because the latter is not a stable key across keyboard layouts.
    /// </remarks>
    private async void ShortcutsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        (string Keys, string Action)[] shortcuts =
        [
            ("Ctrl+O / Ctrl+T", "Open a repository"),
            ("Ctrl+W", "Close the current repository"),
            ("Ctrl+R", "Reload the current repository"),
            ("Ctrl+Shift+P", "Command palette"),
            ("Ctrl+T", "Go to anything: branch, tag, commit, stash, file"),
            ("Ctrl+Enter", "Commit (on the Changes page)"),
            ("Ctrl+F", "Filter commits (on the History page)"),
            ("F1", "This list"),
            ("Escape", "Leave Zen mode")
        ];

        StackPanel panel = new() { Spacing = 8, Width = 420 };

        foreach ((string keys, string action) in shortcuts)
        {
            Grid row = new() { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });

            TextBlock keyText = new()
            {
                Text = keys,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12
            };

            TextBlock actionText = new() { Text = action };
            Grid.SetColumn(actionText, 1);

            row.Children.Add(keyText);
            row.Children.Add(actionText);
            panel.Children.Add(row);
        }

        ContentDialog dialog = new()
        {
            Title = "Keyboard shortcuts",
            Content = panel,
            CloseButtonText = "Close",
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync();
    }

    /// <summary>A status bar segment asking for its section — branch to Branches, changes to Changes.</summary>
    private async void StatusBar_SectionRequested(object? sender, string tag) =>
        await Repository.NavigateToSectionAsync(tag);

    private void ModeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.Mode = ReferenceEquals(sender.SelectedItem, AdvancedModeItem) ? UiMode.Advanced : UiMode.Simple;

    private void ZenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.ToggleZen();
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Only meaningful in Zen; leave Escape alone otherwise so it keeps its normal behaviour.
        args.Handled = ViewModel.IsZen;
        ViewModel.ExitZen();
    }

    /// <summary>Reflects a restored mode in the selector without treating it as a user choice.</summary>
    private void SyncModeBar() =>
        ModeBar.SelectedItem = ViewModel.Mode == UiMode.Advanced ? AdvancedModeItem : SimpleModeItem;

    // ---- Setup wizard ----------------------------------------------------------------------------

    /// <summary>
    ///  Whether the wizard has been through, either finished or skipped.
    /// </summary>
    /// <remarks>
    ///  Held here rather than in the view model because it is a property of the installation rather
    ///  than of anything the view model owns, and this is the only place that reads or writes it.
    /// </remarks>
    private bool _hasCompletedSetup;

    private void ShowSetupWizard()
    {
        // The folder picker has to be initialised with a window handle, which a UserControl has no
        // way to get, so the wizard is handed the window's own picker.
        SetupWizard.Initialize(ViewModel.GlobalGit, ViewModel.Mode, PickFolderAsync);
        SetupWizard.Visibility = Visibility.Visible;
    }

    private void SetupWizard_ThemeChanged(object? sender, EventArgs e) => ThemeService.Apply();

    private async void SetupWizard_Completed(object? sender, SetupOutcome outcome)
    {
        SetupWizard.Visibility = Visibility.Collapsed;

        // Set before anything can fail, so a wizard that has been answered is never shown twice.
        _hasCompletedSetup = true;

        ViewModel.ImportRepositories(
            [.. outcome.Repositories.Select(import => (import.Path, import.ProjectName))],
            [.. outcome.Projects.Select(project => (project.Name.Trim(), project.ColorKey))]);

        // Set through the view model rather than AppOptions: these two drive bindings, and the
        // property setters are what raise the notifications that redraw the strip and the column.
        ViewModel.Layout = outcome.Layout;
        ViewModel.Mode = outcome.Mode;
        SyncModeBar();

        // The timer captured the old interval, so a changed one only takes effect once restarted.
        StartAutoFetch();

        await Home.RefreshAsync();

        // Written straight away: everything the wizard collected would otherwise be lost to a crash
        // before the next periodic save.
        SaveSession();
    }

    private async Task PickAndOpenRepositoryAsync()
    {
        if (await PickFolderAsync() is string path)
        {
            await OpenRepositoryPathAsync(path);
        }
    }

    /// <summary>Shows the folder picker. Returns null when the user cancels.</summary>
    private async Task<string?> PickFolderAsync()
    {
        FolderPicker picker = new();

        // Required even when picking folders — PickSingleFolderAsync throws without at least one filter.
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task OpenRepositoryPathAsync(string path)
    {
        if (!ViewModel.IsValidRepository(path))
        {
            await ShowMessageAsync("Not a git repository", $"{path} is not a git working directory.");
            return;
        }

        await ViewModel.OpenRepositoryAsync(path);
        await Home.RefreshAsync();
    }

    /// <summary>
    ///  Window-scoped failures only — anything a repository reports goes to that repository's InfoBar
    ///  instead, where it does not have to be dismissed before the next action.
    /// </summary>
    private async Task ShowMessageAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            },
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync();
    }
}
