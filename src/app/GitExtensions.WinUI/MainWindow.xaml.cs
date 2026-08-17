using System.ComponentModel.Design;
using GitExtensions.WinUI.Dialogs;
using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GitExtensions.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow(ServiceContainer serviceContainer)
    {
        ViewModel = new MainViewModel(serviceContainer);
        InitializeComponent();
        Title = "Git Extensions";
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

            if (state.Window is WindowBounds bounds && bounds.Width > 0 && bounds.Height > 0)
            {
                AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            }

            await ViewModel.RestoreSessionAsync(state);
            RefreshRecentFlyout();
        }
        catch (Exception ex)
        {
            SessionStore.LogRestoreFailure(ex);
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        RectInt32 position = new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        SessionStore.Save(ViewModel.CaptureSession(new WindowBounds
        {
            X = position.X,
            Y = position.Y,
            Width = position.Width,
            Height = position.Height
        }));
    }

    private async void OpenRepository_Click(object sender, RoutedEventArgs e) => await PickAndOpenRepositoryAsync();

    private async void OpenRepository_SplitClick(SplitButton sender, SplitButtonClickEventArgs args) =>
        await PickAndOpenRepositoryAsync();

    /// <summary>Rebuilds the recent-repositories menu each time it opens, so it never goes stale.</summary>
    private void RefreshRecentFlyout()
    {
        RecentFlyout.Items.Clear();

        if (ViewModel.Recent.Count == 0)
        {
            RecentFlyout.Items.Add(new MenuFlyoutItem { Text = "No recent repositories", IsEnabled = false });
            return;
        }

        foreach (string path in ViewModel.Recent)
        {
            MenuFlyoutItem item = new() { Text = path };

            // Not an async lambda: exceptions escaping a void-returning delegate would crash the
            // process, so the work goes through a method that handles its own failures.
            item.Click += (_, _) => _ = OpenRecentAsync(path);
            RecentFlyout.Items.Add(item);
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

    private async void StageFile_Click(object sender, RoutedEventArgs e) =>
        await RunFileOperationAsync(sender, (tab, file) => tab.StageAsync(file));

    private async void UnstageFile_Click(object sender, RoutedEventArgs e) =>
        await RunFileOperationAsync(sender, (tab, file) => tab.UnstageAsync(file));

    private async Task RunFileOperationAsync(object sender, Func<RepositoryTabViewModel, string, Task<GitOperationResult>> operation)
    {
        if (sender is not Button { Tag: string fileName } || ViewModel.SelectedTab is not RepositoryTabViewModel tab)
        {
            return;
        }

        GitOperationResult result = await operation(tab, fileName);

        if (!result.Succeeded)
        {
            await ShowMessageAsync(result.Description, result.Output);
        }
    }

    private async void CreateBranch_Click(object sender, RoutedEventArgs e)
    {
        if (await PromptAsync("Create branch", "New branch name", "Create") is not string name || ViewModel.SelectedTab is not RepositoryTabViewModel tab)
        {
            return;
        }

        GitOperationResult result = await tab.CreateBranchAsync(name, checkout: true);
        await ReportAsync(result);
    }

    private async void DeleteBranch_Click(object sender, RoutedEventArgs e)
    {
        if (await PromptAsync("Delete branch", "Branch to delete", "Delete") is not string name || ViewModel.SelectedTab is not RepositoryTabViewModel tab)
        {
            return;
        }

        // -d (not -D): git refuses to delete anything unmerged, which is the safer default.
        GitOperationResult result = await tab.DeleteBranchAsync(name, force: false);
        await ReportAsync(result);
    }

    private async void MergeBranch_Click(object sender, RoutedEventArgs e)
    {
        if (await PromptAsync("Merge branch", "Branch to merge into the current one", "Merge") is not string name || ViewModel.SelectedTab is not RepositoryTabViewModel tab)
        {
            return;
        }

        GitOperationResult result = await tab.MergeBranchAsync(name);
        await ReportAsync(result);
    }

    private async void StashSave_Click(object sender, RoutedEventArgs e)
    {
        if (await PromptAsync("Stash changes", "Optional description", "Stash") is not string message || ViewModel.SelectedTab is not RepositoryTabViewModel tab)
        {
            return;
        }

        GitOperationResult result = await tab.StashSaveAsync(message);
        await ReportAsync(result);
    }

    private async void StashList_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTab is not RepositoryTabViewModel tab)
        {
            return;
        }

        GitOperationResult result = await tab.StashListAsync();
        await ShowMessageAsync("Stashes", string.IsNullOrWhiteSpace(result.Output) ? "No stashes." : result.Output);
    }

    /// <summary>Single-line text prompt. Returns null if cancelled.</summary>
    private async Task<string?> PromptAsync(string title, string placeholder, string acceptText)
    {
        TextBox input = new() { PlaceholderText = placeholder };

        ContentDialog dialog = new()
        {
            Title = title,
            Content = input,
            PrimaryButtonText = acceptText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }

    private Task ReportAsync(GitOperationResult result) => ShowMessageAsync(
        result.Succeeded ? $"{result.Description} succeeded" : $"{result.Description} failed",
        string.IsNullOrWhiteSpace(result.Output) ? "Done." : result.Output);

    private async void RepositoryTabs_AddTabButtonClick(TabView sender, object args) => await PickAndOpenRepositoryAsync();

    private async void OpenRepositoryAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await PickAndOpenRepositoryAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSelectedTabAsync();

    private async void RefreshAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await RefreshSelectedTabAsync();
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

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTab is RepositoryTabViewModel tab)
        {
            await tab.LoadMoreAsync();
        }
    }

    private async void Commit_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTab is not RepositoryTabViewModel tab)
        {
            await ShowMessageAsync("No repository", "Open a repository first.");
            return;
        }

        IReadOnlyList<string> pending = await tab.GetPendingChangesAsync();
        if (pending.Count == 0)
        {
            await ShowMessageAsync("Nothing to commit", "The working directory is clean.");
            return;
        }

        CommitDialog dialog = new(tab, RootGrid.XamlRoot);
        GitOperationResult? result = await dialog.ShowAndCommitAsync();

        if (result is not null)
        {
            await ReportAsync(result);
        }
    }

    private async void Fetch_Click(object sender, RoutedEventArgs e) =>
        await RunOperationAsync(tab => tab.FetchAsync());

    private async void Pull_Click(object sender, RoutedEventArgs e) =>
        await RunOperationAsync(tab => tab.PullAsync());

    private async void Push_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTab is not RepositoryTabViewModel tab)
        {
            return;
        }

        // Push is the one operation here that changes something outside this machine, so confirm it.
        ContentDialog confirm = new()
        {
            Title = "Push?",
            Content = $"Push '{tab.Branch}' from {tab.Title} to its remote?",
            PrimaryButtonText = "Push",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await RunOperationAsync(t => t.PushAsync());
    }

    private async Task RunOperationAsync(Func<RepositoryTabViewModel, Task<GitOperationResult>> operation)
    {
        if (ViewModel.SelectedTab is not RepositoryTabViewModel tab)
        {
            await ShowMessageAsync("No repository", "Open a repository first.");
            return;
        }

        GitOperationResult result = await operation(tab);

        string output = string.IsNullOrWhiteSpace(result.Output)
            ? (result.Succeeded ? "Done — nothing to report." : "git reported a failure with no output.")
            : result.Output;

        await ShowMessageAsync(
            result.Succeeded ? $"{result.Description} succeeded" : $"{result.Description} failed",
            output);
    }

    private void ModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.Mode = toggle.IsOn ? UiMode.Advanced : UiMode.Simple;
        }
    }

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

    private async Task PickAndOpenRepositoryAsync()
    {
        FolderPicker picker = new();

        // Required even when picking folders — PickSingleFolderAsync throws without at least one filter.
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        await OpenRepositoryPathAsync(folder.Path);
    }

    private async Task OpenRepositoryPathAsync(string path)
    {
        if (!ViewModel.IsValidRepository(path))
        {
            await ShowMessageAsync("Not a git repository", $"{path} is not a git working directory.");
            return;
        }

        await ViewModel.OpenRepositoryAsync(path);
        RefreshRecentFlyout();
    }

    private async Task RefreshSelectedTabAsync()
    {
        if (ViewModel.SelectedTab is RepositoryTabViewModel tab)
        {
            await tab.LoadAsync();
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        // git output can be long and is worth copying, so it gets a scrollable monospace block
        // rather than the plain string ContentDialog would otherwise render.
        ContentDialog dialog = new()
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 380,
                Content = new TextBlock
                {
                    Text = message,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            },
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync();
    }
}
