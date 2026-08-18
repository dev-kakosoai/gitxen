using GitExtensions.WinUI.Services;
using Microsoft.UI.Xaml.Media;

namespace GitExtensions.WinUI.Theming;

/// <summary>
///  One entry in the theme picker, carrying the brushes its own preview is drawn with.
/// </summary>
/// <remarks>
///  These brushes are fixed at construction and never reassigned, unlike <see cref="ThemeBrushes"/>.
///  A picker has to show all nine themes at once, so each swatch needs its own colours rather than the
///  active theme's — the whole point is comparing them before choosing.
/// </remarks>
public sealed class ThemeOption
{
    private ThemeOption(ThemePalette palette, AppTheme theme, string name, string detail)
    {
        Theme = theme;
        Name = name;
        Detail = detail;

        WindowBrush = new SolidColorBrush(palette.Window);
        SurfaceBrush = new SolidColorBrush(palette.Surface);
        CardBrush = new SolidColorBrush(palette.Card);
        StrokeBrush = new SolidColorBrush(palette.Stroke);
        TextBrush = new SolidColorBrush(palette.TextPrimary);
        TextMutedBrush = new SolidColorBrush(palette.TextTertiary);
        AccentBrush = new SolidColorBrush(palette.Accent);
        SuccessBrush = new SolidColorBrush(palette.Success);
        WarningBrush = new SolidColorBrush(palette.Warning);
        DangerBrush = new SolidColorBrush(palette.Danger);
    }

    /// <summary>What selecting this entry stores.</summary>
    public AppTheme Theme { get; }

    public string Name { get; }

    /// <summary>A line under the name: the family, or what the System entry follows.</summary>
    public string Detail { get; }

    public Brush WindowBrush { get; }

    public Brush SurfaceBrush { get; }

    public Brush CardBrush { get; }

    public Brush StrokeBrush { get; }

    public Brush TextBrush { get; }

    public Brush TextMutedBrush { get; }

    public Brush AccentBrush { get; }

    public Brush SuccessBrush { get; }

    public Brush WarningBrush { get; }

    public Brush DangerBrush { get; }

    /// <summary>
    ///  The picker's entries: follow Windows first, then the eight themes.
    /// </summary>
    /// <remarks>
    ///  Built fresh on each call so the System entry previews whatever Windows is set to right now.
    /// </remarks>
    public static IReadOnlyList<ThemeOption> Build(bool systemIsDark)
    {
        ThemePalette system = ThemeCatalog.Resolve(AppTheme.System, systemIsDark);

        List<ThemeOption> options =
        [
            new(system, AppTheme.System, "Follow Windows", $"Currently {system.Name}")
        ];

        options.AddRange(ThemeCatalog.All.Select(palette => new ThemeOption(
            palette,
            ThemeCatalog.ThemeFor(palette),
            palette.Name,
            palette.IsDark ? "Dark" : "Light")));

        return options;
    }
}
