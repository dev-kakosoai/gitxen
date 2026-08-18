using System.ComponentModel.Design;
using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
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

    public MainWindow(ServiceContainer serviceContainer)
    {
        ViewModel = new MainViewModel(serviceContainer);
        InitializeComponent();

        Title = "Git Extensions";

        // Draw into the title bar so the window reads as one surface rather than a WinUI app wearing a
        // system caption. The drag region is the empty strip left of the caption buttons.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);

        // The grouped source is a resource, so it cannot be bound with x:Bind from the markup.
        ((CollectionViewSource)RootGrid.Resources["GroupedTabsSource"]).Source = ViewModel.TabGroups;

        Closed += MainWindow_Closed;
    }

    public MainViewModel ViewModel { get; }

    /// <summary>
    ///  Restores the previous session. Called after the window is shown so the tabs stream in visibly
    ///  rather than delaying first paint.
    /// </summary>
    public async Task RestoreSessionAsync()
    {
        // Nothing awaits this, so an escaping exception would vanish without trace and simply leave
        // the shell looking like a first run. Record it instead.
        try
        {
            SessionState state = SessionStore.Load();
            AppOptions.Apply(state);
            ApplyTheme();

            if (state.Window is WindowBounds bounds && bounds.Width > 0 && bounds.Height > 0)
            {
                AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            }

            await ViewModel.RestoreSessionAsync(state);

            SyncModeBar();
            StartAutoFetch();
            await Home.RefreshAsync();
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

    private async Task FetchAllQuietlyAsync()
    {
        foreach (RepositoryTabViewModel tab in ViewModel.Repositories.ToList())
        {
            await tab.AutoFetchAsync();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        RectInt32 position = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        SessionState state = ViewModel.CaptureSession(new WindowBounds
        {
            X = position.X,
            Y = position.Y,
            Width = position.Width,
            Height = position.Height
        });

        AppOptions.CopyTo(state);
        SessionStore.Save(state);
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
        }
    }

    private async void Home_OpenRequested(object? sender, string path) => await OpenRecentAsync(path);

    // ---- Repository column -----------------------------------------------------------------------

    /// <summary>The repository being dragged between projects in the column.</summary>
    private ShellTab? _draggingTab;

    private void SelectHome_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedTab = ViewModel.Home;

    private void ToggleSidebarGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ShellTabGroup { Group: RepositoryGroup group })
        {
            ViewModel.ToggleGroupExpanded(group);
        }
    }

    private void SidebarItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        _draggingTab = (sender as FrameworkElement)?.DataContext as ShellTab;

        // Something has to be offered or the drop targets never light up.
        args.Data.SetText(_draggingTab?.Description ?? "");
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void SidebarGroup_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _draggingTab is null ? DataPackageOperation.None : DataPackageOperation.Move;
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
            || _draggingTab is not RepositoryTabViewModel repository)
        {
            return;
        }

        _draggingTab = null;
        ViewModel.AssignToGroup(repository.WorkingDir, section.Group);
    }

    private void CloseRepository_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ShellTab tab })
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
        if (args.Item is RepositoryTabViewModel tab)
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

    /// <summary>
    ///  Applied to the root element rather than the app: Application.RequestedTheme can only be set
    ///  before the first window exists.
    /// </summary>
    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = AppOptions.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        // Diff colours are chosen per theme and cached, so they have to be recomputed.
        DiffLineViewModel.InvalidatePalette();
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
