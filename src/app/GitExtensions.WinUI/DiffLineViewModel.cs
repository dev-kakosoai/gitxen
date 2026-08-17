using GitExtensions.WinUI.Diff;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GitExtensions.WinUI;

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
    Hunk,
    Header
}

/// <summary>A run of characters sharing one colour.</summary>
public sealed record DiffRun(string Text, Brush Foreground);

/// <summary>
///  One line of a unified diff: classified, syntax-highlighted, and carrying its line numbers.
/// </summary>
/// <remarks>
///  Add/remove is shown as a background tint rather than a text colour, which leaves the foreground
///  free for syntax highlighting — the same split GitHub uses.
/// </remarks>
public sealed class DiffLineViewModel
{
    private static DiffPalette? _palette;

    private DiffLineViewModel(DiffLineKind kind, string text, IReadOnlyList<DiffRun> runs, Brush? background, int oldNumber, int newNumber)
    {
        Kind = kind;
        Text = text;
        Runs = runs;
        Background = background;
        OldLineNumber = oldNumber > 0 ? oldNumber.ToString() : "";
        NewLineNumber = newNumber > 0 ? newNumber.ToString() : "";
    }

    public DiffLineKind Kind { get; }

    public string Text { get; }

    public IReadOnlyList<DiffRun> Runs { get; }

    public Brush? Background { get; }

    public string OldLineNumber { get; }

    public string NewLineNumber { get; }

    public static DiffLineViewModel Create(string line, string fileName, int oldNumber = 0, int newNumber = 0)
    {
        DiffPalette palette = _palette ??= DiffPalette.ForCurrentTheme();
        DiffLineKind kind = Classify(line);

        // Headers and hunk markers aren't code, so they get a flat colour and no tokenizing.
        if (kind is DiffLineKind.Hunk or DiffLineKind.Header)
        {
            Brush flat = kind == DiffLineKind.Hunk ? palette.Hunk : palette.Context;
            return new DiffLineViewModel(kind, line, [new DiffRun(line, flat)], background: null, oldNumber, newNumber);
        }

        Brush? background = kind switch
        {
            DiffLineKind.Added => palette.AddedBackground,
            DiffLineKind.Removed => palette.RemovedBackground,
            _ => null
        };

        // Keep the +/- marker as plain text; only the code after it is highlighted.
        string marker = line.Length > 0 && kind != DiffLineKind.Context ? line[..1] : "";
        string code = line.Length > 0 ? line[marker.Length..] : "";

        List<DiffRun> runs = [];
        if (marker.Length > 0)
        {
            runs.Add(new DiffRun(marker, palette.Context));
        }

        foreach (SyntaxToken token in SyntaxHighlighter.Tokenize(code, fileName))
        {
            runs.Add(new DiffRun(token.Text, palette.For(token.Kind)));
        }

        return new DiffLineViewModel(kind, line, runs, background, oldNumber, newNumber);
    }

    /// <summary>A plain, unhighlighted line — used for messages and raw output such as blame.</summary>
    public static DiffLineViewModel CreatePlain(string line)
    {
        DiffPalette palette = _palette ??= DiffPalette.ForCurrentTheme();
        return new DiffLineViewModel(DiffLineKind.Context, line, [new DiffRun(line, palette.Context)], null, 0, 0);
    }

    /// <summary>Discards the cached palette so the next diff picks up a theme change.</summary>
    public static void InvalidatePalette() => _palette = null;

    private static DiffLineKind Classify(string line)
    {
        if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            return DiffLineKind.Hunk;
        }

        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)
            || line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal))
        {
            return DiffLineKind.Header;
        }

        if (line.StartsWith('+'))
        {
            return DiffLineKind.Added;
        }

        return line.StartsWith('-') ? DiffLineKind.Removed : DiffLineKind.Context;
    }

    private sealed record DiffPalette(
        Brush Context,
        Brush Hunk,
        Brush Keyword,
        Brush String,
        Brush Comment,
        Brush Number,
        Brush AddedBackground,
        Brush RemovedBackground)
    {
        public Brush For(TokenKind kind) => kind switch
        {
            TokenKind.Keyword => Keyword,
            TokenKind.String => String,
            TokenKind.Comment => Comment,
            TokenKind.Number => Number,
            _ => Context
        };

        public static DiffPalette ForCurrentTheme()
        {
            // Dark-theme colours wash out on white and vice versa, so pick per theme rather than
            // compromising on one set.
            bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;

            return new DiffPalette(
                Context: ThemeBrush(),
                Hunk: Solid(isDark ? (88, 166, 255) : (9, 105, 218)),
                Keyword: Solid(isDark ? (255, 123, 114) : (207, 34, 46)),
                String: Solid(isDark ? (165, 214, 255) : (10, 48, 105)),
                Comment: Solid(isDark ? (139, 148, 158) : (110, 119, 129)),
                Number: Solid(isDark ? (121, 192, 255) : (5, 80, 174)),
                AddedBackground: Solid(isDark ? (46, 90, 60) : (218, 251, 225), isDark ? (byte)90 : (byte)255),
                RemovedBackground: Solid(isDark ? (103, 46, 46) : (255, 235, 233), isDark ? (byte)90 : (byte)255));
        }

        private static SolidColorBrush Solid((int R, int G, int B) rgb, byte alpha = 255) =>
            new(Color.FromArgb(alpha, (byte)rgb.R, (byte)rgb.G, (byte)rgb.B));

        private static Brush ThemeBrush() =>
            Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out object? brush) && brush is Brush themed
                ? themed
                : new SolidColorBrush(Colors.Gray);
    }
}
