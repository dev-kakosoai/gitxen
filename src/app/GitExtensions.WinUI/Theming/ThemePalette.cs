using Windows.UI;

namespace GitExtensions.WinUI.Theming;

/// <summary>
///  Which product a theme is imitating, so the picker can group its variants together.
/// </summary>
public enum ThemeFamily
{
    Gitxen,
    GitHub,
    VisualStudio,
    Cyberpunk
}

/// <summary>
///  Every colour a theme decides, and nothing more.
/// </summary>
/// <remarks>
///  Deliberately small. WinUI needs about seventy brushes to look coherent, but they are not seventy
///  independent decisions — most are one of these seeds with a documented tint, shade or alpha applied
///  (see <see cref="ThemeBrushes"/>). Defining a theme as the seeds keeps each one short enough to
///  read in one screen and makes two themes comparable side by side, which is the only way to notice
///  that one of them has a contrast problem.
/// </remarks>
public sealed record ThemePalette
{
    /// <summary>Stable identifier, used in the session file so a theme survives being renamed.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required ThemeFamily Family { get; init; }

    /// <summary>
    ///  Whether the un-seeded parts of WinUI should resolve dark.
    /// </summary>
    /// <remarks>
    ///  Drives <c>ElementTheme</c>, which still matters even though most brushes are overridden:
    ///  control templates pick shadows, glyph weights and the default focus visual from it.
    /// </remarks>
    public required bool IsDark { get; init; }

    /// <summary>
    ///  Whether the window keeps its Mica backdrop.
    /// </summary>
    /// <remarks>
    ///  Mica tints from the desktop wallpaper, which is exactly right for a theme that means to look
    ///  like part of Windows and exactly wrong for one that commits to its own background colour — the
    ///  wallpaper would show through and drag the whole palette towards whatever is behind the window.
    /// </remarks>
    public required bool UsesMica { get; init; }

    // ---- Surfaces, back to front -----------------------------------------------------------------

    /// <summary>The window itself, behind everything.</summary>
    public required Color Window { get; init; }

    /// <summary>Chrome that frames the content: navigation pane, tab strip, command bars.</summary>
    public required Color Surface { get; init; }

    /// <summary>Cards, list backgrounds, flyouts — the raised surfaces content sits on.</summary>
    public required Color Card { get; init; }

    /// <summary>A control at rest: button faces, text box interiors, combo boxes.</summary>
    public required Color Control { get; init; }

    /// <summary>Borders around cards and controls.</summary>
    public required Color Stroke { get; init; }

    /// <summary>Hairlines between rows and sections.</summary>
    public required Color Divider { get; init; }

    // ---- Text ------------------------------------------------------------------------------------

    public required Color TextPrimary { get; init; }

    public required Color TextSecondary { get; init; }

    public required Color TextTertiary { get; init; }

    public required Color TextDisabled { get; init; }

    // ---- Accent ----------------------------------------------------------------------------------

    /// <summary>Filled buttons, selection, the checked state of a toggle.</summary>
    public required Color Accent { get; init; }

    public required Color AccentHover { get; init; }

    public required Color AccentPressed { get; init; }

    /// <summary>Text drawn on top of <see cref="Accent"/>.</summary>
    public required Color TextOnAccent { get; init; }

    /// <summary>
    ///  Accent-coloured text on a normal surface: links, hyperlink buttons, the accent glyphs.
    /// </summary>
    /// <remarks>
    ///  Separate from <see cref="Accent"/> because they have different jobs. Accent is a background
    ///  and wants to be saturated; this is 4.5:1 text on <see cref="Card"/> and usually has to be
    ///  lightened on a dark theme or darkened on a light one to get there.
    /// </remarks>
    public required Color AccentText { get; init; }

    // ---- Status ----------------------------------------------------------------------------------

    /// <summary>Also the added-file marker, the HEAD badge and the ahead count.</summary>
    public required Color Success { get; init; }

    /// <summary>Also the modified-file marker and the tag badge.</summary>
    public required Color Warning { get; init; }

    /// <summary>Also the deleted-file marker and conflict markers.</summary>
    public required Color Danger { get; init; }

    /// <summary>Also the renamed-file marker and the local-branch badge.</summary>
    public required Color Info { get; init; }

    // ---- Diff and code ---------------------------------------------------------------------------

    /// <summary>Row background for an added line. Translucent, so selection still shows through.</summary>
    public required Color DiffAdded { get; init; }

    public required Color DiffRemoved { get; init; }

    public required Color CodeKeyword { get; init; }

    public required Color CodeString { get; init; }

    public required Color CodeComment { get; init; }

    public required Color CodeNumber { get; init; }

    /// <summary>
    ///  Eight categorical colours, used for graph lanes and for repository group accents.
    /// </summary>
    /// <remarks>
    ///  One list for both because the requirement is the same — hues that are told apart at a glance
    ///  and legible on this theme's surfaces — and two lists would drift. Order matters: lane 0 is the
    ///  first lane in the graph and therefore the one most commits sit on.
    /// </remarks>
    public required IReadOnlyList<Color> Lanes { get; init; }
}

/// <summary>
///  The blending the brush table needs, and a short way to write a hex colour.
/// </summary>
internal static class ColorMath
{
    /// <summary>0xRRGGBB, so palettes read like the hex codes they were designed as.</summary>
    public static Color Rgb(uint value) =>
        Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    /// <summary>The colour with a different alpha, for fills that must let a surface show through.</summary>
    public static Color Alpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>
    ///  <paramref name="amount"/> of the way from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        double t = Math.Clamp(amount, 0, 1);

        return Color.FromArgb(
            255,
            (byte)Math.Round((from.R * (1 - t)) + (to.R * t)),
            (byte)Math.Round((from.G * (1 - t)) + (to.G * t)),
            (byte)Math.Round((from.B * (1 - t)) + (to.B * t)));
    }
}
