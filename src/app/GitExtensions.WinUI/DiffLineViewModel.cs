using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GitExtensions.WinUI;

/// <summary>
///  One line of a unified diff, already classified so the view only has to bind a brush.
/// </summary>
public sealed class DiffLineViewModel
{
    // Resolved once, lazily, on first use — a large diff would otherwise allocate a brush per line.
    private static DiffPalette? _palette;

    private DiffLineViewModel(string text, Brush foreground)
    {
        Text = text;
        Foreground = foreground;
    }

    public string Text { get; }

    public Brush Foreground { get; }

    public static DiffLineViewModel Create(string line)
    {
        DiffPalette palette = _palette ??= DiffPalette.ForCurrentTheme();

        if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            return new DiffLineViewModel(line, palette.Hunk);
        }

        // "+++"/"---" are file headers, not content changes, so they stay neutral.
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
        {
            return new DiffLineViewModel(line, palette.Context);
        }

        if (line.StartsWith('+'))
        {
            return new DiffLineViewModel(line, palette.Added);
        }

        if (line.StartsWith('-'))
        {
            return new DiffLineViewModel(line, palette.Removed);
        }

        return new DiffLineViewModel(line, palette.Context);
    }

    /// <summary>Discards the cached palette so the next diff picks up a theme change.</summary>
    public static void InvalidatePalette() => _palette = null;

    private sealed record DiffPalette(Brush Added, Brush Removed, Brush Hunk, Brush Context)
    {
        public static DiffPalette ForCurrentTheme()
        {
            // Dark-theme greens/blues wash out on white, and the light-theme equivalents are too
            // dark on black, so pick per theme rather than compromising on one set.
            bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;

            Brush added = new SolidColorBrush(isDark
                ? Color.FromArgb(255, 63, 185, 80)
                : Color.FromArgb(255, 26, 127, 55));
            Brush removed = new SolidColorBrush(isDark
                ? Color.FromArgb(255, 248, 81, 73)
                : Color.FromArgb(255, 207, 34, 46));
            Brush hunk = new SolidColorBrush(isDark
                ? Color.FromArgb(255, 88, 166, 255)
                : Color.FromArgb(255, 9, 105, 218));

            return new DiffPalette(added, removed, hunk, GetContextBrush());
        }

        /// <summary>Unchanged lines follow the theme rather than a hard-coded colour.</summary>
        private static Brush GetContextBrush()
        {
            if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out object? brush) && brush is Brush themed)
            {
                return themed;
            }

            return new SolidColorBrush(Colors.Gray);
        }
    }
}
