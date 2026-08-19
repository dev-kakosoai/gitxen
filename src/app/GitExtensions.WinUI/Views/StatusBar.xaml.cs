using GitCommands.Git;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  The window's status bar: branch, divergence, pending changes and repository state in one glance,
///  in the accent colour so it reads as chrome rather than content.
/// </summary>
/// <remarks>
///  Every segment is a shortcut as well as a readout — branch to Branches, changes to Changes, the
///  sync counts to a fetch — because a status bar whose facts cannot be acted on is just a caption.
///  While Home is selected there is no repository to report, so the bar shows the application's own
///  facts instead; only Zen hides it, with the rest of the chrome.
/// </remarks>
public sealed partial class StatusBar : UserControl
{
    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab),
        typeof(RepositoryTabViewModel),
        typeof(StatusBar),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell),
        typeof(MainViewModel),
        typeof(StatusBar),
        new PropertyMetadata(null));

    public StatusBar()
    {
        InitializeComponent();
        Loaded += StatusBar_Loaded;
    }

    /// <summary>The repository whose state the bar shows. Null while Home is selected.</summary>
    public RepositoryTabViewModel? Tab
    {
        get => (RepositoryTabViewModel?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    /// <summary>The window's view model, for the facts the Home side of the bar reports.</summary>
    public MainViewModel? Shell
    {
        get => (MainViewModel?)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>The two sides of the bar swap on the one fact that separates them: is a repository selected.</summary>
    public Visibility WhenRepository(RepositoryTabViewModel? tab) =>
        tab is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WhenHome(RepositoryTabViewModel? tab) =>
        tab is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>"Gitxen 1.2.3", from the assembly, so the bar never disagrees with the build.</summary>
    public string AppVersionText { get; } = BuildAppVersionText();

    private static string BuildAppVersionText()
    {
        Version? version = typeof(StatusBar).Assembly.GetName().Version;
        return version is null ? "Gitxen" : $"Gitxen {version.ToString(3)}";
    }

    /// <summary>Guards the git-version read, which is per process, not per load.</summary>
    private bool _gitVersionRead;

    /// <summary>
    ///  Fills in the git version once the bar is up.
    /// </summary>
    /// <remarks>
    ///  Read on a worker because the first touch of <see cref="GitVersion.Current"/> shells out to
    ///  <c>git --version</c>; the bar simply shows nothing there until the answer arrives.
    /// </remarks>
    private async void StatusBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (_gitVersionRead)
        {
            return;
        }

        _gitVersionRead = true;

        GitVersionText.Text = await Task.Run(static () =>
        {
            try
            {
                return $"git {GitVersion.Current}";
            }
            catch (Exception)
            {
                // No git found is Home-page news the InfoBars already break; the bar stays quiet.
                return "";
            }
        });
    }

    /// <summary>Raised with a navigation tag when a segment asks for its section.</summary>
    public event EventHandler<string>? SectionRequested;

    /// <summary>Raised when the console segment asks for the git activity pane.</summary>
    public event EventHandler? ConsoleRequested;

    private void Console_Click(object sender, RoutedEventArgs e) =>
        ConsoleRequested?.Invoke(this, EventArgs.Empty);

    private void Branch_Click(object sender, RoutedEventArgs e) => SectionRequested?.Invoke(this, "branches");

    private void Changes_Click(object sender, RoutedEventArgs e) => SectionRequested?.Invoke(this, "changes");

    private void Conflicts_Click(object sender, RoutedEventArgs e) => SectionRequested?.Invoke(this, "conflicts");

    /// <summary>Conflicts is where a paused operation is resolved, so that is where its segment goes.</summary>
    private void Operation_Click(object sender, RoutedEventArgs e) => SectionRequested?.Invoke(this, "conflicts");

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            tab.Report(await tab.FetchAsync());
        }
    }
}
