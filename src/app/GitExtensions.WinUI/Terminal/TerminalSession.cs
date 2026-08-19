using Microsoft.UI.Dispatching;

namespace GitExtensions.WinUI.Terminal;

/// <summary>
///  One running shell in the terminal pane: the process, its transcript, and its command history.
/// </summary>
/// <remarks>
///  The PTY reports output on its reader thread; this is the layer that owns the locking and puts
///  the result on the UI thread. Renders are coalesced — a build spraying output enqueues one UI
///  update at a time, not one per pipe read.
/// </remarks>
public sealed class TerminalSession : ObservableObject, IDisposable
{
    private readonly ConPtySession _pty;
    private readonly TerminalOutputBuffer _buffer = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly Lock _bufferLock = new();
    private int _renderQueued;
    private string _text = "";
    private bool _isRunning = true;

    /// <summary>Commands entered in this session, oldest first, for Up/Down in the input box.</summary>
    public List<string> History { get; } = [];

    /// <summary>Where Up/Down currently is in <see cref="History"/>; equal to Count when not browsing.</summary>
    public int HistoryIndex { get; set; }

    public TerminalShell Shell { get; }

    /// <summary>What the session picker shows: the shell and the folder it was opened on.</summary>
    public string Title { get; }

    public string WorkingDirectory { get; }

    public TerminalSession(TerminalShell shell, string workingDirectory, DispatcherQueue dispatcher, short columns, short rows)
    {
        Shell = shell;
        WorkingDirectory = workingDirectory;
        _dispatcher = dispatcher;

        string folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(workingDirectory));
        Title = folder.Length > 0 ? $"{shell.Name} — {folder}" : shell.Name;

        _buffer.SetScreenRows(rows);
        _pty = new ConPtySession(shell.ExecutablePath, shell.Arguments, workingDirectory, columns, rows);
        _pty.OutputReceived += OnOutputReceived;
        _pty.Exited += OnExited;
    }

    /// <summary>The transcript. Only ever assigned on the UI thread.</summary>
    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    /// <summary>Sends one command line, as Enter in a console would.</summary>
    public void SendLine(string command)
    {
        _pty.Write(command + "\r");

        if (command.Length > 0 && (History.Count == 0 || History[^1] != command))
        {
            History.Add(command);
        }

        HistoryIndex = History.Count;
    }

    /// <summary>Ctrl+C: interrupts whatever the shell is running without ending the session.</summary>
    public void SendInterrupt() => _pty.Write("\x03");

    public void Clear()
    {
        lock (_bufferLock)
        {
            _buffer.Clear();
        }

        ScheduleRender();
    }

    public void Resize(short columns, short rows)
    {
        if (IsRunning)
        {
            lock (_bufferLock)
            {
                _buffer.SetScreenRows(rows);
            }

            _pty.Resize(columns, rows);
        }
    }

    public void Dispose() => _pty.Dispose();

    private void OnOutputReceived(string chunk)
    {
        lock (_bufferLock)
        {
            _buffer.Append(chunk);
        }

        ScheduleRender();
    }

    private void OnExited(int exitCode)
    {
        lock (_bufferLock)
        {
            _buffer.Append($"\r\n[{Shell.Name} exited with code {exitCode}]\r\n");
        }

        ScheduleRender();
        _dispatcher.TryEnqueue(() => IsRunning = false);
    }

    /// <summary>
    ///  Puts one transcript refresh on the UI queue, unless one is already waiting — the flag is
    ///  what stops a chatty build enqueueing thousands of updates.
    /// </summary>
    private void ScheduleRender()
    {
        if (Interlocked.CompareExchange(ref _renderQueued, 1, 0) != 0)
        {
            return;
        }

        _dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _renderQueued, 0);

            string text;
            lock (_bufferLock)
            {
                text = _buffer.GetText();
            }

            Text = text;
        });
    }
}
