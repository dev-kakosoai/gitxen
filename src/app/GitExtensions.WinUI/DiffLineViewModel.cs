using GitExtensions.WinUI.Diff;
using GitExtensions.WinUI.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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

    /// <summary>
    ///  The hunk this line belongs to, set on the hunk's own header row so that row can offer to stage
    ///  or unstage it. Null everywhere else, and on diffs where staging makes no sense.
    /// </summary>
    public DiffHunk? Hunk { get; set; }

    /// <summary>Only the header row of a hunk carries the stage/unstage affordance.</summary>
    public Visibility HunkActionVisibility => Hunk is null ? Visibility.Collapsed : Visibility.Visible;

    public string HunkSummary => Hunk?.Summary ?? "";

    public static DiffLineViewModel Create(string line, string fileName, int oldNumber = 0, int newNumber = 0)
    {
        DiffLineKind kind = Classify(line);

        // Headers and hunk markers aren't code, so they get a flat colour and no tokenizing.
        if (kind is DiffLineKind.Hunk or DiffLineKind.Header)
        {
            Brush flat = kind == DiffLineKind.Hunk ? DiffPalette.Hunk : DiffPalette.Context;
            return new DiffLineViewModel(kind, line, [new DiffRun(line, flat)], background: null, oldNumber, newNumber);
        }

        Brush? background = kind switch
        {
            DiffLineKind.Added => DiffPalette.AddedBackground,
            DiffLineKind.Removed => DiffPalette.RemovedBackground,
            _ => null
        };

        // Keep the +/- marker as plain text; only the code after it is highlighted.
        string marker = line.Length > 0 && kind != DiffLineKind.Context ? line[..1] : "";
        string code = line.Length > 0 ? line[marker.Length..] : "";

        List<DiffRun> runs = [];
        if (marker.Length > 0)
        {
            runs.Add(new DiffRun(marker, DiffPalette.Context));
        }

        foreach (SyntaxToken token in SyntaxHighlighter.Tokenize(code, fileName))
        {
            runs.Add(new DiffRun(token.Text, DiffPalette.For(token.Kind)));
        }

        return new DiffLineViewModel(kind, line, runs, background, oldNumber, newNumber);
    }

    /// <summary>A plain, unhighlighted line — used for messages and raw output such as blame.</summary>
    public static DiffLineViewModel CreatePlain(string line)
    {
        return new DiffLineViewModel(DiffLineKind.Context, line, [new DiffRun(line, DiffPalette.Context)], null, 0, 0);
    }

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

    /// <summary>
    ///  The colours a diff line is drawn with, as a view onto the theme's brushes.
    /// </summary>
    /// <remarks>
    ///  Nothing is cached or copied here. The brushes are the theme's own instances, which only ever
    ///  have their colour reassigned, so a diff that is already on screen recolours when the theme
    ///  changes instead of keeping the colours it was built with until it is read again.
    /// </remarks>
    private static class DiffPalette
    {
        public static Brush Context => ThemeBrushes.Text;

        public static Brush Hunk => ThemeBrushes.DiffHunkHeader;

        public static Brush AddedBackground => ThemeBrushes.DiffAddedBackground;

        public static Brush RemovedBackground => ThemeBrushes.DiffRemovedBackground;

        public static Brush For(TokenKind kind) => kind switch
        {
            TokenKind.Keyword => ThemeBrushes.CodeKeyword,
            TokenKind.String => ThemeBrushes.CodeString,
            TokenKind.Comment => ThemeBrushes.CodeComment,
            TokenKind.Number => ThemeBrushes.CodeNumber,
            _ => Context
        };
    }
}
