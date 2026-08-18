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
///  It shows the selected repository only; the window hides it entirely on Home and in Zen.
/// </remarks>
public sealed partial class StatusBar : UserControl
{
    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab),
        typeof(RepositoryTabViewModel),
        typeof(StatusBar),
        new PropertyMetadata(null));

    public StatusBar()
    {
        InitializeComponent();
    }

    /// <summary>The repository whose state the bar shows. Null while Home is selected.</summary>
    public RepositoryTabViewModel? Tab
    {
        get => (RepositoryTabViewModel?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    /// <summary>Raised with a navigation tag when a segment asks for its section.</summary>
    public event EventHandler<string>? SectionRequested;

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
