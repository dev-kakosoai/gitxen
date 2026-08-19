using GitCommands.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  A console for what the app is actually doing: every process it launches, live, with the
///  selected one's full detail — arguments, working directory, duration, exit code, captured error.
/// </summary>
/// <remarks>
///  Fed by <see cref="CommandLog"/>, which the backend already writes for every
///  <c>Executable</c> launch, so nothing new is instrumented. The log records the error stream for
///  failures but deliberately not standard output — a <c>git log</c> here can produce megabytes —
///  so output is shown where it is consumed: the pages, and each operation's InfoBar.
/// </remarks>
public sealed partial class GitActivityPane : UserControl
{
    /// <summary>Coalesces change bursts: one queued refresh at a time, however many events arrive.</summary>
    private bool _refreshQueued;

    public GitActivityPane()
    {
        InitializeComponent();
    }

    public void Toggle()
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    /// <summary>Shows the pane and starts following the log. Subscribed only while visible.</summary>
    public void Show()
    {
        Visibility = Visibility.Visible;

        CommandLog.CommandsChanged -= OnCommandsChanged;
        CommandLog.CommandsChanged += OnCommandsChanged;

        Refresh();
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        CommandLog.CommandsChanged -= OnCommandsChanged;
    }

    /// <summary>Raised from whichever thread ran the process; the refresh belongs to the UI thread.</summary>
    private void OnCommandsChanged()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;

        DispatcherQueue.TryEnqueue(() =>
        {
            _refreshQueued = false;
            Refresh();
        });
    }

    /// <summary>
    ///  Re-reads the log. The list is reassigned rather than updated in place: entries mutate as
    ///  their processes finish (duration, exit code), and rebinding is what re-renders their rows.
    /// </summary>
    private void Refresh()
    {
        CommandLogEntry? selected = List.SelectedItem as CommandLogEntry;
        List<CommandLogEntry> entries = [.. CommandLog.Commands];
        List.ItemsSource = entries;

        if (selected is not null && entries.Contains(selected))
        {
            List.SelectedItem = selected;
        }
        else if (entries.Count > 0)
        {
            // Following the newest, like a console does, until a row is being inspected.
            List.ScrollIntoView(entries[^1]);
        }
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is CommandLogEntry entry)
        {
            DetailBox.Text = entry.Detail;
            DetailBox.Visibility = Visibility.Visible;
        }
        else
        {
            DetailBox.Visibility = Visibility.Collapsed;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => CommandLog.Clear();

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
}
