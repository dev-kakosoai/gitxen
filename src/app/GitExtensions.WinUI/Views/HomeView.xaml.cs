using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  The start page: opening a repository, and what you were last working on.
/// </summary>
/// <remarks>
///  The open/clone/create actions live here rather than in the title bar because this page is always
///  reachable — it is the first tab and cannot be closed — and because a start page is where people
///  look for them. The recent list carries each repository's branch and last commit so you can tell
///  them apart without opening them, and is organised into projects, since a project is rarely one
///  repository.
/// </remarks>
public sealed partial class HomeView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(MainViewModel), typeof(HomeView), new PropertyMetadata(null, OnViewModelChanged));

    /// <summary>
    ///  The repository being dragged.
    /// </summary>
    /// <remarks>
    ///  Held in a field rather than put in the DataPackage: the drop needs the RecentRepository object
    ///  itself, and a DataPackage carries text and storage items, not arbitrary objects. The drag never
    ///  leaves this window, so there is nothing for another application to receive.
    /// </remarks>
    private RecentRepository? _dragging;

    public HomeView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the page wants the shell to run one of its actions.</summary>
    public event EventHandler<HomeAction>? ActionRequested;

    /// <summary>Raised with the working directory the user picked from the recent list.</summary>
    public event EventHandler<string>? OpenRequested;

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public Visibility RecentVisibility =>
        ViewModel?.RecentRepositories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyRecentVisibility =>
        ViewModel?.RecentRepositories.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The ungrouped list only earns a heading once something is grouped.</summary>
    public Visibility UngroupedCaptionVisibility =>
        ViewModel?.Ungrouped.Repositories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Re-reads the recent list and its per-repository detail.</summary>
    public async Task RefreshAsync()
    {
        if (ViewModel is not MainViewModel viewModel)
        {
            return;
        }

        await viewModel.RefreshRecentAsync();
        Bindings.Update();
    }

    private static void OnViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        HomeView view = (HomeView)sender;
        view.SyncLayoutBox();

        // Home hosts the global git configuration page with no repository, so it is handed the
        // writer that exists without one.
        if (view.ViewModel is MainViewModel viewModel)
        {
            view.GlobalConfig.GlobalGit = viewModel.GlobalGit;
        }
    }

    /// <summary>Populated on expand rather than on load, so Home never runs git it is not showing.</summary>
    private async void AppSettings_Expanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await AppSettings.ActivateAsync();

    private async void GlobalConfig_Expanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await GlobalConfig.ActivateAsync();

    private void SyncLayoutBox() =>
        LayoutBox.SelectedIndex = ViewModel?.Layout == RepositoryLayout.Sidebar ? 1 : 0;

    private void Open_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, HomeAction.OpenRepository);

    private void Clone_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, HomeAction.Clone);

    private void Setup_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, HomeAction.RunSetup);

    private void Init_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, HomeAction.Initialise);

    private async void RefreshRecent_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void Recent_ItemClick(object sender, ItemClickEventArgs e) =>
        RequestOpen(e.ClickedItem as RecentRepository);

    private void OpenRecent_Click(object sender, RoutedEventArgs e) =>
        RequestOpen((sender as FrameworkElement)?.DataContext as RecentRepository);

    private void RequestOpen(RecentRepository? entry)
    {
        if (entry is not null)
        {
            OpenRequested?.Invoke(this, entry.Path);
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecentRepository entry)
        {
            return;
        }

        DataPackage package = new();
        package.SetText(entry.Path);
        Clipboard.SetContent(package);
    }

    private void RemoveRecent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecentRepository entry)
        {
            ViewModel?.RemoveRecent(entry.Path);
            Bindings.Update();
        }
    }

    private void Layout_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is MainViewModel viewModel && LayoutBox.SelectedIndex >= 0)
        {
            viewModel.Layout = LayoutBox.SelectedIndex == 1
                ? RepositoryLayout.Sidebar
                : RepositoryLayout.Tabs;
        }
    }

    // ---- Projects --------------------------------------------------------------------------------

    private void Repositories_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragging = e.Items.FirstOrDefault() as RecentRepository;

        // Something has to be offered or the drop targets never light up.
        e.Data.SetText(_dragging?.Path ?? "");
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void Group_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _dragging is null ? DataPackageOperation.None : DataPackageOperation.Move;
        e.DragUIOverride.Caption = "Move to this project";
        e.DragUIOverride.IsGlyphVisible = false;
    }

    private void Group_Drop(object sender, DragEventArgs e) =>
        MoveDragged((sender as FrameworkElement)?.Tag as RepositoryGroup);

    private void Ungrouped_Drop(object sender, DragEventArgs e) => MoveDragged(null);

    private void MoveDragged(RepositoryGroup? target)
    {
        if (ViewModel is not MainViewModel viewModel || _dragging is not RecentRepository dragged)
        {
            return;
        }

        _dragging = null;
        viewModel.AssignToGroup(dragged.Path, target);
        Bindings.Update();
    }

    private void ToggleGroup_Click(object sender, RoutedEventArgs e) =>
        ToggleGroup((sender as FrameworkElement)?.Tag as RepositoryGroup);

    /// <summary>
    ///  The whole header collapses its card, not only the chevron. Clicks on the buttons the header
    ///  hosts do not reach here — they handle their own pointer events.
    /// </summary>
    private void GroupHeader_Tapped(object sender, TappedRoutedEventArgs e) =>
        ToggleGroup((sender as FrameworkElement)?.Tag as RepositoryGroup);

    private void ToggleGroup(RepositoryGroup? group)
    {
        if (group is not null && ViewModel is MainViewModel viewModel)
        {
            // Through the view model, so the repository column's section collapses with the card —
            // both read the same IsExpanded, but the column only reflects it when rebuilt.
            viewModel.ToggleGroupExpanded(group);
        }
    }

    private async void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not MainViewModel viewModel)
        {
            return;
        }

        if (await EditGroupAsync("New project", "", GroupIcons.Default, GroupPalette.Default)
            is not (string name, string glyph, string colour))
        {
            return;
        }

        viewModel.CreateGroup(name, glyph, colour);
        Bindings.Update();
    }

    private async void EditGroup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not MainViewModel viewModel
            || (sender as FrameworkElement)?.Tag is not RepositoryGroup group)
        {
            return;
        }

        if (await EditGroupAsync("Edit project", group.Name, group.Glyph, group.ColorKey)
            is not (string name, string glyph, string colour))
        {
            return;
        }

        // Renaming goes through the view model, because membership is keyed by the name.
        if (!string.Equals(name, group.Name, StringComparison.Ordinal))
        {
            viewModel.RenameGroup(group, name);
        }

        // Restyling does too, so the repository column's header picks up the new look.
        viewModel.RestyleGroup(group, glyph, colour);
        Bindings.Update();
    }

    private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not MainViewModel viewModel
            || (sender as FrameworkElement)?.Tag is not RepositoryGroup group)
        {
            return;
        }

        ContentDialog confirm = new()
        {
            Title = $"Delete the {group.Name} project?",
            Content = new TextBlock
            {
                Text = "The repositories stay in the list; they simply stop being grouped. "
                    + "Nothing on disk is touched.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Delete group",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            viewModel.DeleteGroup(group);
            Bindings.Update();
        }
    }

    /// <summary>Moves a repository from its context menu, for anyone who would rather not drag.</summary>
    private async void MoveToGroup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not MainViewModel viewModel
            || (sender as FrameworkElement)?.DataContext is not RecentRepository entry)
        {
            return;
        }

        ComboBox picker = new() { Header = "Project", MinWidth = 280 };
        picker.Items.Add("Not in a project");

        foreach (RepositoryGroup group in viewModel.Groups)
        {
            picker.Items.Add(group.Name);
        }

        picker.SelectedIndex = 0;

        ContentDialog dialog = new()
        {
            Title = $"Move {entry.Name}",
            Content = picker,
            PrimaryButtonText = "Move",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        RepositoryGroup? target = picker.SelectedIndex <= 0
            ? null
            : viewModel.Groups[picker.SelectedIndex - 1];

        viewModel.AssignToGroup(entry.Path, target);
        Bindings.Update();
    }

    /// <summary>
    ///  The name, icon and colour editor, shared by creating and editing.
    /// </summary>
    /// <remarks>
    ///  Icon and colour come from fixed sets rather than free pickers: the point of them is telling
    ///  projects apart at a glance, which needs choices that stay legible on both themes. An arbitrary
    ///  colour picker mostly produces ones that do not.
    /// </remarks>
    private async Task<(string Name, string Glyph, string Colour)?> EditGroupAsync(
        string title,
        string name,
        string glyph,
        string colour)
    {
        TextBox nameBox = new() { Header = "Name", Text = name, MinWidth = 320 };
        ComboBox iconBox = new() { Header = "Icon", MinWidth = 200 };

        foreach ((string itemGlyph, string label) in GroupIcons.All)
        {
            StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new FontIcon { Glyph = itemGlyph, FontSize = 14 });
            row.Children.Add(new TextBlock { Text = label });
            iconBox.Items.Add(new ComboBoxItem { Content = row, Tag = itemGlyph });
        }

        ComboBox colourBox = new() { Header = "Colour", MinWidth = 200 };

        foreach (string key in GroupPalette.Keys)
        {
            StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 10 };

            row.Children.Add(new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = GroupPalette.Accent(key),
                VerticalAlignment = VerticalAlignment.Center
            });

            row.Children.Add(new TextBlock { Text = key });
            colourBox.Items.Add(new ComboBoxItem { Content = row, Tag = key });
        }

        iconBox.SelectedIndex = Math.Max(0, GroupIcons.All.ToList().FindIndex(item => item.Glyph == glyph));
        colourBox.SelectedIndex = Math.Max(0, GroupPalette.Keys.ToList().FindIndex(key => key == colour));

        StackPanel panel = new() { Spacing = 14, Width = 360 };
        panel.Children.Add(nameBox);
        panel.Children.Add(iconBox);
        panel.Children.Add(colourBox);

        ContentDialog dialog = new()
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || nameBox.Text.Trim().Length == 0)
        {
            return null;
        }

        return (
            nameBox.Text.Trim(),
            (iconBox.SelectedItem as ComboBoxItem)?.Tag as string ?? GroupIcons.Default,
            (colourBox.SelectedItem as ComboBoxItem)?.Tag as string ?? GroupPalette.Default);
    }
}

/// <summary>Something the Home page asks the shell to do, since only the shell owns a window handle.</summary>
public enum HomeAction
{
    OpenRepository,
    Clone,
    Initialise,

    /// <summary>Reopen the setup wizard, which is also how a folder is scanned for repositories.</summary>
    RunSetup
}
