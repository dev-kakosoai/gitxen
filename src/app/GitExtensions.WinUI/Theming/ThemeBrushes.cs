using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static GitExtensions.WinUI.Theming.ColorMath;

namespace GitExtensions.WinUI.Theming;

/// <summary>
///  The live brushes every themed surface in the app draws with.
/// </summary>
/// <remarks>
///  <para>
///   Each brush is created once and then only ever has its <c>Color</c> changed. That is what makes
///   switching themes instant and complete: a <c>SolidColorBrush</c> repaints everything that
///   references it, so a diff that is already on screen, a graph that is already drawn and a flyout
///   that is already open all recolour without being rebuilt. Swapping in new brush instances instead
///   would leave every one of those holding the old colour until something happened to recreate it.
///  </para>
///  <para>
///   The WinUI brushes are registered as entries on <c>Application.Resources</c> itself rather than in
///   a merged dictionary, because a dictionary's own entries take precedence over the theme
///   dictionaries that <c>XamlControlsResources</c> brings in. That is what lets a palette override the
///   system look instead of losing to it.
///  </para>
/// </remarks>
internal static class ThemeBrushes
{
    private static readonly Color _transparent = Color.FromArgb(0, 0, 0, 0);

    /// <summary>The WinUI brushes this class owns, so a system brush is never mutated by accident.</summary>
    private static readonly Dictionary<string, SolidColorBrush> _system = [];

    // ---- The app's own semantics, which no WinUI key covers ---------------------------------------

    /// <summary>The window background, for themes that paint it themselves instead of using Mica.</summary>
    public static SolidColorBrush Window { get; } = new();

    /// <summary>Primary text, for the few places that need the brush rather than the resource key.</summary>
    public static SolidColorBrush Text { get; } = new();

    public static SolidColorBrush FileAdded { get; } = new();

    public static SolidColorBrush FileModified { get; } = new();

    public static SolidColorBrush FileDeleted { get; } = new();

    public static SolidColorBrush FileRenamed { get; } = new();

    public static SolidColorBrush DiffAddedBackground { get; } = new();

    public static SolidColorBrush DiffRemovedBackground { get; } = new();

    public static SolidColorBrush DiffHunkHeader { get; } = new();

    public static SolidColorBrush CodeKeyword { get; } = new();

    public static SolidColorBrush CodeString { get; } = new();

    public static SolidColorBrush CodeComment { get; } = new();

    public static SolidColorBrush CodeNumber { get; } = new();

    /// <summary>
    ///  The eight categorical colours, at full strength.
    /// </summary>
    /// <remarks>
    ///  Used for graph lanes and, through <c>GroupPalette</c>, for repository group accents.
    /// </remarks>
    public static IReadOnlyList<SolidColorBrush> Lanes { get; } = CreateSet(8);

    /// <summary>The same eight, as a wash for a chip or a group header to sit on.</summary>
    public static IReadOnlyList<SolidColorBrush> LaneTints { get; } = CreateSet(8);

    /// <summary>Ref badge fills, in the order local branch, remote, tag, HEAD.</summary>
    public static IReadOnlyList<SolidColorBrush> RefFills { get; } = CreateSet(4);

    /// <summary>Ref badge text, in the same order as <see cref="RefFills"/>.</summary>
    public static IReadOnlyList<SolidColorBrush> RefTexts { get; } = CreateSet(4);

    /// <summary>
    ///  Points every brush at a new palette.
    /// </summary>
    /// <remarks>
    ///  Safe to call before any window exists, and safe to call repeatedly: the first call registers
    ///  the WinUI brushes, later ones only change colours.
    /// </remarks>
    public static void Apply(ThemePalette palette)
    {
        foreach ((string key, Color color) in MapSystemBrushes(palette))
        {
            if (_system.TryGetValue(key, out SolidColorBrush? existing))
            {
                existing.Color = color;
            }
            else
            {
                SolidColorBrush brush = new(color);
                _system[key] = brush;
                Application.Current.Resources[key] = brush;
            }
        }

        Window.Color = palette.Window;
        Text.Color = palette.TextPrimary;

        FileAdded.Color = palette.Success;
        FileModified.Color = palette.Warning;
        FileDeleted.Color = palette.Danger;
        FileRenamed.Color = palette.Info;

        DiffAddedBackground.Color = palette.DiffAdded;
        DiffRemovedBackground.Color = palette.DiffRemoved;
        DiffHunkHeader.Color = palette.AccentText;
        CodeKeyword.Color = palette.CodeKeyword;
        CodeString.Color = palette.CodeString;
        CodeComment.Color = palette.CodeComment;
        CodeNumber.Color = palette.CodeNumber;

        for (int lane = 0; lane < Lanes.Count; lane++)
        {
            Color color = palette.Lanes[lane % palette.Lanes.Count];
            Lanes[lane].Color = color;
            LaneTints[lane].Color = Alpha(color, palette.IsDark ? (byte)48 : (byte)36);
        }

        // Local branch, remote, tag, HEAD — the four kinds of RefBadge, mapped onto the status colours
        // so a badge means the same thing as the marker letter next to a file.
        Color[] refColors = [palette.Info, palette.Lanes[3], palette.Warning, palette.Success];

        for (int kind = 0; kind < refColors.Length; kind++)
        {
            RefFills[kind].Color = Alpha(refColors[kind], palette.IsDark ? (byte)56 : (byte)40);
            RefTexts[kind].Color = refColors[kind];
        }
    }

    private static IReadOnlyList<SolidColorBrush> CreateSet(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => new SolidColorBrush())];

    /// <summary>
    ///  Derives the WinUI brush table from a palette's seeds.
    /// </summary>
    /// <remarks>
    ///  The derivations follow what the WinUI theme dictionaries do with their own colour ramps: hover
    ///  and press states are translucent overlays of the text colour so they read correctly on any
    ///  surface underneath, recessed states blend towards the window, and disabled states blend towards
    ///  it further still.
    /// </remarks>
    private static (string Key, Color Color)[] MapSystemBrushes(ThemePalette palette)
    {
        bool dark = palette.IsDark;
        Color text = palette.TextPrimary;

        // WinUI's own subtle fills are roughly 6% and 4% white on dark, 4% and 2% black on light.
        Color hover = Alpha(text, dark ? (byte)15 : (byte)10);
        Color pressed = Alpha(text, dark ? (byte)10 : (byte)6);

        return
        [

            // Page and window backgrounds.
            ("ApplicationPageBackgroundThemeBrush", palette.Window),
            ("SolidBackgroundFillColorBaseBrush", palette.Window),
            ("SolidBackgroundFillColorBaseAltBrush", dark
                ? Mix(palette.Window, Color.FromArgb(255, 0, 0, 0), 0.45)
                : Mix(palette.Window, palette.Stroke, 0.5)),
            ("SolidBackgroundFillColorSecondaryBrush", palette.Surface),
            ("SolidBackgroundFillColorTertiaryBrush", palette.Card),
            ("SolidBackgroundFillColorQuarternaryBrush", palette.Control),

            // Raised surfaces: cards, list backgrounds, flyouts.
            ("LayerFillColorDefaultBrush", palette.Card),
            ("LayerFillColorAltBrush", palette.Card),
            ("LayerOnAcrylicFillColorDefaultBrush", Alpha(palette.Card, 200)),
            ("CardBackgroundFillColorDefaultBrush", palette.Card),
            ("CardBackgroundFillColorSecondaryBrush", Mix(palette.Card, palette.Window, 0.5)),

            // Chrome that frames the content: navigation pane, tab strip, command bars.
            ("LayerOnMicaBaseAltFillColorDefaultBrush", palette.Surface),
            ("LayerOnMicaBaseAltFillColorSecondaryBrush", hover),
            ("LayerOnMicaBaseAltFillColorTertiaryBrush", palette.Card),
            ("LayerOnMicaBaseAltFillColorTransparentBrush", _transparent),

            // Controls at rest, hovered, pressed, disabled.
            ("ControlFillColorDefaultBrush", palette.Control),
            ("ControlFillColorSecondaryBrush", Mix(palette.Control, text, dark ? 0.06 : 0.04)),
            ("ControlFillColorTertiaryBrush", Mix(palette.Control, palette.Window, 0.45)),
            ("ControlFillColorDisabledBrush", Mix(palette.Control, palette.Window, 0.7)),
            ("ControlFillColorTransparentBrush", _transparent),
            ("ControlFillColorInputActiveBrush", dark
                ? Mix(palette.Window, palette.Control, 0.25)
                : palette.Card),
            ("ControlSolidFillColorDefaultBrush", palette.Card),
            ("ControlAltFillColorTransparentBrush", _transparent),
            ("ControlAltFillColorSecondaryBrush", Mix(palette.Card, palette.Window, 0.6)),
            ("ControlAltFillColorTertiaryBrush", Mix(palette.Card, palette.Window, 0.4)),
            ("ControlAltFillColorQuarternaryBrush", Mix(palette.Card, palette.Window, 0.25)),
            ("ControlAltFillColorDisabledBrush", _transparent),
            ("ControlStrongFillColorDefaultBrush", palette.TextSecondary),
            ("ControlStrongFillColorDisabledBrush", palette.TextDisabled),

            // Hover and press on things that have no fill of their own: list rows, subtle buttons.
            ("SubtleFillColorTransparentBrush", _transparent),
            ("SubtleFillColorSecondaryBrush", hover),
            ("SubtleFillColorTertiaryBrush", pressed),
            ("SubtleFillColorDisabledBrush", _transparent),

            // Borders.
            ("ControlStrokeColorDefaultBrush", palette.Stroke),
            ("ControlStrokeColorSecondaryBrush", Mix(palette.Stroke, text, dark ? 0.12 : 0.1)),
            ("ControlStrokeColorOnAccentDefaultBrush", Alpha(palette.TextOnAccent, 20)),
            ("ControlStrokeColorOnAccentSecondaryBrush", Alpha(palette.Window, 100)),
            ("ControlStrokeColorOnAccentTertiaryBrush", Alpha(palette.Window, 60)),
            ("ControlStrokeColorOnAccentDisabledBrush", Alpha(palette.Window, 30)),
            ("ControlStrongStrokeColorDefaultBrush", palette.TextSecondary),
            ("ControlStrongStrokeColorDisabledBrush", palette.TextDisabled),
            ("CardStrokeColorDefaultBrush", palette.Stroke),
            ("CardStrokeColorDefaultSolidBrush", palette.Stroke),
            ("DividerStrokeColorDefaultBrush", palette.Divider),
            ("SurfaceStrokeColorDefaultBrush", palette.Stroke),
            ("SurfaceStrokeColorFlyoutBrush", palette.Stroke),
            ("FocusStrokeColorOuterBrush", palette.TextPrimary),
            ("FocusStrokeColorInnerBrush", palette.Window),

            // Text.
            ("TextFillColorPrimaryBrush", palette.TextPrimary),
            ("TextFillColorSecondaryBrush", palette.TextSecondary),
            ("TextFillColorTertiaryBrush", palette.TextTertiary),
            ("TextFillColorDisabledBrush", palette.TextDisabled),
            ("TextFillColorInverseBrush", palette.Window),
            ("AccentTextFillColorPrimaryBrush", palette.AccentText),
            ("AccentTextFillColorSecondaryBrush", palette.AccentText),
            ("AccentTextFillColorTertiaryBrush", palette.Accent),
            ("AccentTextFillColorDisabledBrush", palette.TextDisabled),
            ("TextOnAccentFillColorPrimaryBrush", palette.TextOnAccent),
            ("TextOnAccentFillColorSecondaryBrush", Alpha(palette.TextOnAccent, 200)),
            ("TextOnAccentFillColorDisabledBrush", Alpha(palette.TextOnAccent, 120)),
            ("TextOnAccentFillColorSelectedTextBrush", palette.TextOnAccent),

            // Accent fills.
            ("AccentFillColorDefaultBrush", palette.Accent),
            ("AccentFillColorSecondaryBrush", palette.AccentHover),
            ("AccentFillColorTertiaryBrush", palette.AccentPressed),
            ("AccentFillColorDisabledBrush", Mix(palette.Accent, palette.Window, 0.65)),
            ("AccentFillColorSelectedTextBackgroundBrush", palette.Accent),

            // Status: InfoBar severities, badges, the health checks.
            ("SystemFillColorSuccessBrush", palette.Success),
            ("SystemFillColorCautionBrush", palette.Warning),
            ("SystemFillColorCriticalBrush", palette.Danger),
            ("SystemFillColorAttentionBrush", palette.Info),
            ("SystemFillColorNeutralBrush", palette.TextSecondary),
            ("SystemFillColorSolidNeutralBrush", Mix(palette.Card, palette.TextSecondary, 0.6)),
            ("SystemFillColorSolidAttentionBrush", palette.Info),
            ("SystemFillColorSuccessBackgroundBrush", Mix(palette.Card, palette.Success, dark ? 0.16 : 0.14)),
            ("SystemFillColorCautionBackgroundBrush", Mix(palette.Card, palette.Warning, dark ? 0.16 : 0.14)),
            ("SystemFillColorCriticalBackgroundBrush", Mix(palette.Card, palette.Danger, dark ? 0.16 : 0.14)),
            ("SystemFillColorAttentionBackgroundBrush", Mix(palette.Card, palette.Info, dark ? 0.16 : 0.14)),
            ("SystemFillColorNeutralBackgroundBrush", hover),

            // The scrim behind a ContentDialog.
            ("SmokeFillColorDefaultBrush", Alpha(Color.FromArgb(255, 0, 0, 0), dark ? (byte)153 : (byte)102))
        ];
    }
}
