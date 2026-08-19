using System.Text;

namespace GitExtensions.WinUI.Terminal;

/// <summary>
///  Turns a ConPTY VT stream back into plain lines of text.
/// </summary>
/// <remarks>
///  <para>
///   Not a terminal emulator. The pane renders selectable text, not a character grid, so colours and
///   cursor addressing have nowhere to land; what this keeps is what a transcript keeps. The
///   sequences that do change the transcript are honoured: carriage return overwrites the current
///   line (progress bars), backspace steps back, erase-in-line truncates, and erase-in-display
///   clears — which is what makes <c>cls</c> and <c>clear</c> work. Everything else in the escape
///   grammar is parsed and dropped.
///  </para>
///  <para>
///   The parser state persists across <see cref="Append"/> calls because an escape sequence can be
///   split across two pipe reads.
///  </para>
///  <para>
///   Not thread-safe; the caller locks. The reader thread appends and the UI thread snapshots.
///  </para>
/// </remarks>
public sealed class TerminalOutputBuffer
{
    /// <summary>Scrollback cap. An unbounded shell transcript is an unbounded TextBlock.</summary>
    private const int MaxLines = 2000;

    private const int TabWidth = 8;

    private enum ParserState
    {
        Ground,

        /// <summary>Seen ESC, deciding what kind of sequence follows.</summary>
        Escape,

        /// <summary>Seen ESC and a character-set/charset prefix; the next character is consumed.</summary>
        EscapeIntermediate,

        /// <summary>Inside ESC [ … , collecting until a final byte.</summary>
        Csi,

        /// <summary>Inside ESC ] … (window title and friends), collecting until BEL or ESC \.</summary>
        Osc,

        /// <summary>Seen ESC inside an OSC string; a backslash ends the string.</summary>
        OscEscape
    }

    private readonly List<StringBuilder> _lines = [new StringBuilder()];
    private readonly StringBuilder _csiParameters = new();
    private ParserState _state = ParserState.Ground;

    /// <summary>The cursor's column on the last line — where \r, \b and overwriting land.</summary>
    private int _column;

    /// <summary>
    ///  Where the cursor is on the pseudoconsole's screen, 1-based, tracked so that explicit cursor
    ///  positioning can be told apart: conhost frequently addresses the next row with ESC[row;colH
    ///  instead of emitting a newline, and dropping those joins lines that were never joined.
    /// </summary>
    private int _screenRow = 1;

    /// <summary>The pseudoconsole's height; the row where a newline scrolls instead of descending.</summary>
    private int _screenRows = 30;

    /// <summary>Keeps the screen-position model in step with the pseudoconsole's size.</summary>
    public void SetScreenRows(int rows)
    {
        _screenRows = Math.Max(1, rows);
        _screenRow = Math.Min(_screenRow, _screenRows);
    }

    public void Append(string chunk)
    {
        foreach (char c in chunk)
        {
            switch (_state)
            {
                case ParserState.Ground:
                    AppendGround(c);
                    break;

                case ParserState.Escape:
                    _state = c switch
                    {
                        '[' => ParserState.Csi,
                        ']' => ParserState.Osc,
                        '(' or ')' or '#' or '%' or '*' or '+' => ParserState.EscapeIntermediate,
                        _ => ParserState.Ground
                    };

                    if (_state == ParserState.Csi)
                    {
                        _csiParameters.Clear();
                    }

                    break;

                case ParserState.EscapeIntermediate:
                    _state = ParserState.Ground;
                    break;

                case ParserState.Csi:
                    if (c >= 0x40 && c <= 0x7E)
                    {
                        DispatchCsi(c);
                        _state = ParserState.Ground;
                    }
                    else
                    {
                        _csiParameters.Append(c);
                    }

                    break;

                case ParserState.Osc:
                    if (c == '\a')
                    {
                        _state = ParserState.Ground;
                    }
                    else if (c == '\x1B')
                    {
                        _state = ParserState.OscEscape;
                    }

                    break;

                case ParserState.OscEscape:
                    _state = c == '\\' ? ParserState.Ground : ParserState.Osc;
                    break;
            }
        }
    }

    /// <summary>Empties the transcript, as ESC[2J does.</summary>
    public void Clear()
    {
        _lines.Clear();
        _lines.Add(new StringBuilder());
        _column = 0;
        _screenRow = 1;
    }

    /// <summary>
    ///  The transcript as one string, for the pane's TextBlock.
    /// </summary>
    /// <remarks>
    ///  Cleaned up at read time rather than in the buffer, so the cleanup never corrupts the
    ///  positioning model: trailing spaces are dropped (cursor positioning pads lines to wherever
    ///  the prompt last painted) and runs of blank lines are capped (a repainting prompt like
    ///  PSReadLine walks the cursor across empty rows it never writes to).
    /// </remarks>
    public string GetText()
    {
        StringBuilder text = new();
        int blankRun = 0;

        foreach (StringBuilder line in _lines)
        {
            int length = line.Length;
            while (length > 0 && line[length - 1] == ' ')
            {
                length--;
            }

            blankRun = length == 0 ? blankRun + 1 : 0;
            if (blankRun > 2)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(line, 0, length);
        }

        return text.ToString();
    }

    private void AppendGround(char c)
    {
        switch (c)
        {
            case '\x1B':
                _state = ParserState.Escape;
                break;

            case '\r':
                _column = 0;
                break;

            case '\n':
                StartNewLine();
                break;

            case '\b':
                _column = Math.Max(0, _column - 1);
                break;

            case '\t':
                // Expanded to spaces so overwriting after a \r stays column-accurate.
                int next = ((_column / TabWidth) + 1) * TabWidth;
                while (_column < next)
                {
                    PutChar(' ');
                }

                break;

            case '\a':
                break;

            default:
                if (!char.IsControl(c))
                {
                    PutChar(c);
                }

                break;
        }
    }

    /// <summary>Writes at the cursor, overwriting what a progress bar left there.</summary>
    private void PutChar(char c)
    {
        StringBuilder line = _lines[^1];

        while (line.Length < _column)
        {
            line.Append(' ');
        }

        if (_column < line.Length)
        {
            line[_column] = c;
        }
        else
        {
            line.Append(c);
        }

        _column++;
    }

    private void StartNewLine()
    {
        _lines.Add(new StringBuilder());
        _column = 0;
        _screenRow = Math.Min(_screenRow + 1, _screenRows);

        if (_lines.Count > MaxLines)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
    }

    private void DispatchCsi(char final)
    {
        switch (final)
        {
            case 'K':
                EraseInLine();
                break;

            case 'J':
                // Erase-in-display 2 and 3 are what cls and clear send.
                if (FirstParameter() >= 2)
                {
                    Clear();
                }

                break;

            case 'H' or 'f':
                MoveCursor(FirstParameter(), SecondParameter());
                break;

            case 'd':
                // VPA: vertical move only, column kept.
                MoveCursor(FirstParameter(), _column + 1);
                break;

            case 'E':
                // CNL: down n rows, column 1.
                for (int i = Math.Max(1, FirstParameter()); i > 0; i--)
                {
                    StartNewLine();
                }

                break;

            // Colours, modes, horizontal-only moves: nothing a transcript can represent.
            default:
                break;
        }
    }

    /// <summary>
    ///  ESC[row;colH. A move below the current row is a line break by another name; a move on the
    ///  current row repositions within it. A move upward cannot edit rows this transcript has
    ///  already scrolled past, so its text lands on the last line instead — but the tracked row is
    ///  still resynchronised, because every later move is judged relative to it and a stale value
    ///  turns genuine line breaks into joins.
    /// </summary>
    private void MoveCursor(int row, int column)
    {
        row = Math.Max(1, row);
        column = Math.Max(1, column);

        for (int i = row - _screenRow; i > 0; i--)
        {
            StartNewLine();
        }

        _screenRow = Math.Min(row, _screenRows);
        _column = column - 1;
    }

    private void EraseInLine()
    {
        StringBuilder line = _lines[^1];

        switch (FirstParameter())
        {
            case 0:
                if (_column < line.Length)
                {
                    line.Length = _column;
                }

                break;

            case 1:
                for (int i = 0; i < Math.Min(_column, line.Length); i++)
                {
                    line[i] = ' ';
                }

                break;

            case 2:
                line.Clear();
                break;
        }
    }

    /// <summary>The first numeric CSI parameter; 0 when absent, as VT defines the default.</summary>
    private int FirstParameter() => Parameter(0);

    private int SecondParameter() => Parameter(1);

    private int Parameter(int index)
    {
        int value = 0;
        bool any = false;
        int current = 0;

        foreach (char c in _csiParameters.ToString())
        {
            if (c == '?')
            {
                // Private-mode sequences (cursor visibility and the like) have no transcript effect.
                return -1;
            }

            if (c == ';')
            {
                if (current == index)
                {
                    break;
                }

                current++;
                continue;
            }

            if (current == index && char.IsAsciiDigit(c))
            {
                value = Math.Min((value * 10) + (c - '0'), 9999);
                any = true;
            }
        }

        return any ? value : 0;
    }
}
