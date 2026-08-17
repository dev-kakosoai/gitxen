using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  The start page: opening a repository, and what you were last working on.
/// </summary>
/// <remarks>
///  The open/clone/create actions live here rather than in the title bar because this page is always
///  reachable — it is the first tab and cannot be closed — and because a start page is where people
///  look for them. The recent list carries each repository's branch and last commit so you can tell
///  them apart without opening them.
/// </remarks>
public sealed partial class HomeView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(MainViewModel), typeof(HomeView), new PropertyMetadata(null, OnViewModelChanged));

    public HomeView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the page wants the shell to run one of its actions.</summary>
    public event EventHandler<HomeAction>? ActionRequested;

    public MainViewModel? ViewModel
    {
        get => (MainViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public Visibility RecentVisibility =>
        ViewModel?.RecentRepositories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyRecentVisibility =>
        ViewModel?.RecentRepositories.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

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
    }

    private void SyncLayoutBox() =>
        LayoutBox.SelectedIndex = ViewModel?.Layout == RepositoryLayout.Sidebar ? 1 : 0;

    private void Open_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, HomeAction.OpenRepository);

    private void Clone_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, HomeAction.Clone);

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

    /// <summary>Raised with the working directory the user picked from the recent list.</summary>
    public event EventHandler<string>? OpenRequested;

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
}

/// <summary>Something the Home page asks the shell to do, since only the shell owns a window handle.</summary>
public enum HomeAction
{
    OpenRepository,
    Clone,
    Initialise
}
