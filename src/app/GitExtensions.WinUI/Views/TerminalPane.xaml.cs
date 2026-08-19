using System.Collections.ObjectModel;
using System.ComponentModel;
using GitExtensions.WinUI.Terminal;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  The bottom terminal: real shells — PowerShell, cmd, Git Bash, WSL — opened on the repository's
///  working directory, without leaving the app.
/// </summary>
/// <remarks>
///  <para>
///   Each session is a shell on a ConPTY, so the shell believes it is interactive and prompts,
///   aliases and profiles all behave. The transcript is rendered as selectable text and commands are
///   typed into a line editor underneath it, which is the honest version of what this control is: a
///   transcript-and-a-prompt, not a character-grid emulator. Full-screen programs (vim, htop) want
///   the latter and are better off in Windows Terminal via "Open in".
///  </para>
///  <para>
///   The pane is shared by every repository tab, like the panel in an editor: the shell instance
///   hosting this control is the same one across tabs, so sessions survive switching. Each session
///   is pinned to the working directory it was opened on and says so in its title.
///  </para>
/// </remarks>
public sealed partial class TerminalPane : UserControl
{
    /// <summary>Fallback size for a session started before the pane has been laid out.</summary>
    private const short DefaultColumns = 120;
    private const short DefaultRows = 30;

    /// <summary>One monospace cell at the output font, measured once on load.</summary>
    private Size _cell;

    /// <summary>The session whose Text changes are currently being shown.</summary>
    private TerminalSession? _displayed;

    public TerminalPane()
    {
        InitializeComponent();
        Loaded += TerminalPane_Loaded;
    }

    /// <summary>The open sessions, in the order they were started.</summary>
    public ObservableCollection<TerminalSession> Sessions { get; } = [];

    /// <summary>Where a new session starts; the shell keeps this pointed at the active repository.</summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>The user asked to hide the pane.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Starts the default shell if nothing is running yet. Called when the pane is shown.</summary>
    public void EnsureSession()
    {
        if (Sessions.Count == 0 && TerminalShells.Default is TerminalShell shell)
        {
            StartSession(shell);
        }
    }

    public void FocusInput() => InputBox.Focus(FocusState.Programmatic);

    /// <summary>Ends every session. The window calls this on close so no shell outlives the app.</summary>
    public void DisposeAll()
    {
        foreach (TerminalSession session in Sessions.ToList())
        {
            session.PropertyChanged -= Session_PropertyChanged;
            session.Dispose();
        }

        Sessions.Clear();
        _displayed = null;
    }

    private void TerminalPane_Loaded(object sender, RoutedEventArgs e)
    {
        if (ShellFlyout.Items.Count > 0)
        {
            return;
        }

        _cell = MeasureCell();

        if (TerminalShells.Available.Count == 0)
        {
            ShellFlyout.Items.Add(new MenuFlyoutItem { Text = "No shells found", IsEnabled = false });
            return;
        }

        foreach (TerminalShell shell in TerminalShells.Available)
        {
            MenuFlyoutItem item = new() { Text = shell.Name };
            TerminalShell target = shell;
            item.Click += (_, _) => StartSession(target);
            ShellFlyout.Items.Add(item);
        }
    }

    private void StartSession(TerminalShell shell)
    {
        string workingDirectory = Directory.Exists(WorkingDirectory)
            ? WorkingDirectory
            : Environment.CurrentDirectory;

        (short columns, short rows) = ViewportSize();

        TerminalSession session;
        try
        {
            session = new TerminalSession(shell, workingDirectory, DispatcherQueue, columns, rows);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            OutputText.Text = $"Could not start {shell.Name}: {ex.Message}";
            return;
        }

        session.PropertyChanged += Session_PropertyChanged;
        Sessions.Add(session);
        SessionPicker.SelectedItem = session;
        FocusInput();
    }

    private TerminalSession? Active => SessionPicker.SelectedItem as TerminalSession;

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalSession.Text) && ReferenceEquals(sender, _displayed))
        {
            ShowTranscript(_displayed);
        }
    }

    private void SessionPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _displayed = Active;
        ShowTranscript(_displayed);
    }

    /// <summary>
    ///  Puts a session's transcript in the output, keeping the view pinned to the bottom — but only
    ///  when it already was: someone scrolled up reading old output stays where they are.
    /// </summary>
    private void ShowTranscript(TerminalSession? session)
    {
        bool wasAtBottom = OutputScroller.VerticalOffset >= OutputScroller.ScrollableHeight - 40;

        OutputText.Text = session?.Text ?? "";

        if (wasAtBottom)
        {
            OutputScroller.UpdateLayout();
            OutputScroller.ChangeView(null, OutputScroller.ScrollableHeight, null, disableAnimation: true);
        }
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Active is not TerminalSession session)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                session.SendLine(InputBox.Text);
                InputBox.Text = "";
                e.Handled = true;
                break;

            case VirtualKey.Up:
                RecallHistory(session, -1);
                e.Handled = true;
                break;

            case VirtualKey.Down:
                RecallHistory(session, +1);
                e.Handled = true;
                break;

            case VirtualKey.C when IsControlDown() && InputBox.Text.Length == 0:
                // Ctrl+C in an empty box is the console gesture: interrupt. With text in the box it
                // stays what a text box means by it.
                session.SendInterrupt();
                e.Handled = true;
                break;
        }
    }

    private void RecallHistory(TerminalSession session, int direction)
    {
        if (session.History.Count == 0)
        {
            return;
        }

        int index = Math.Clamp(session.HistoryIndex + direction, 0, session.History.Count);
        session.HistoryIndex = index;

        InputBox.Text = index < session.History.Count ? session.History[index] : "";
        InputBox.SelectionStart = InputBox.Text.Length;
    }

    private void Interrupt_Click(object sender, RoutedEventArgs e) => Active?.SendInterrupt();

    private void Clear_Click(object sender, RoutedEventArgs e) => Active?.Clear();

    private void CloseSession_Click(object sender, RoutedEventArgs e)
    {
        if (Active is not TerminalSession session)
        {
            return;
        }

        session.PropertyChanged -= Session_PropertyChanged;
        session.Dispose();
        Sessions.Remove(session);

        if (Sessions.Count > 0)
        {
            SessionPicker.SelectedItem = Sessions[^1];
        }
        else
        {
            _displayed = null;
            OutputText.Text = "";
        }
    }

    private void ClosePane_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Tells every session the pane's new size, so output wraps at the real width.</summary>
    private void OutputScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        (short columns, short rows) = ViewportSize();

        foreach (TerminalSession session in Sessions)
        {
            session.Resize(columns, rows);
        }
    }

    private (short Columns, short Rows) ViewportSize()
    {
        if (_cell.Width <= 0 || OutputScroller.ActualWidth <= 0)
        {
            return (DefaultColumns, DefaultRows);
        }

        short columns = (short)Math.Max(20, (OutputScroller.ActualWidth - 24) / _cell.Width);
        short rows = (short)Math.Max(5, OutputScroller.ActualHeight / _cell.Height);
        return (columns, rows);
    }

    /// <summary>
    ///  Measures one cell of the output font. Measured rather than assumed, so a machine that falls
    ///  back from Cascadia Mono to Consolas still gets accurate column counts.
    /// </summary>
    private static Size MeasureCell()
    {
        TextBlock probe = new()
        {
            Text = new string('M', 10),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12
        };

        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(probe.DesiredSize.Width / 10, probe.DesiredSize.Height);
    }

    private static bool IsControlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
}
