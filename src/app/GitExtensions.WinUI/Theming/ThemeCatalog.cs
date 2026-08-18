using GitExtensions.WinUI.Services;
using Windows.UI;
using static GitExtensions.WinUI.Theming.ColorMath;

namespace GitExtensions.WinUI.Theming;

/// <summary>
///  The themes that ship with Gitxen.
/// </summary>
/// <remarks>
///  Each is a flat list of seed colours, which makes them comparable: the same line means the same
///  thing in all eight, so a contrast mistake shows up as an outlier rather than having to be hunted.
///  The derived brushes are in <see cref="ThemeBrushes"/>.
/// </remarks>
public static class ThemeCatalog
{
    /// <summary>
    ///  Gitxen's own dark theme, and the look the rest of the shell was designed against.
    /// </summary>
    /// <remarks>
    ///  A cool near-black with a violet cast, so the brand blue and purple sit on it without looking
    ///  bolted on. Close enough to the WinUI defaults that it keeps the Mica backdrop.
    /// </remarks>
    public static ThemePalette GitxenDark { get; } = new()
    {
        Id = "gitxen-dark",
        Name = "Gitxen Dark",
        Family = ThemeFamily.Gitxen,
        IsDark = true,
        UsesMica = true,

        Window = Rgb(0x14141A),
        Surface = Rgb(0x1A1A22),
        Card = Rgb(0x1E1E27),
        Control = Rgb(0x24242E),
        Stroke = Rgb(0x2F2F3B),
        Divider = Rgb(0x282833),

        TextPrimary = Rgb(0xF2F2F7),
        TextSecondary = Rgb(0xAAAABF),
        TextTertiary = Rgb(0x7C7C90),
        TextDisabled = Rgb(0x5A5A6B),

        Accent = Rgb(0x58A6FF),
        AccentHover = Rgb(0x74B6FF),
        AccentPressed = Rgb(0x4189DB),
        TextOnAccent = Rgb(0x0B0B10),
        AccentText = Rgb(0x8CC2FF),

        Success = Rgb(0x3FB950),
        Warning = Rgb(0xD29922),
        Danger = Rgb(0xF85149),
        Info = Rgb(0x58A6FF),

        DiffAdded = Alpha(Rgb(0x3FB950), 40),
        DiffRemoved = Alpha(Rgb(0xF85149), 40),
        CodeKeyword = Rgb(0xFF7B72),
        CodeString = Rgb(0xA5D6FF),
        CodeComment = Rgb(0x8B949E),
        CodeNumber = Rgb(0x79C0FF),

        Lanes =
        [
            Rgb(0x58A6FF), Rgb(0x3FB950), Rgb(0xDB6D28), Rgb(0xBC8CFF),
            Rgb(0xE96986), Rgb(0x56D3C8), Rgb(0xD2A841), Rgb(0x9198A1)
        ]
    };

    /// <summary>Gitxen's own light theme: the same hues, darkened enough to hold 4.5:1 on white.</summary>
    public static ThemePalette GitxenLight { get; } = new()
    {
        Id = "gitxen-light",
        Name = "Gitxen Light",
        Family = ThemeFamily.Gitxen,
        IsDark = false,
        UsesMica = true,

        Window = Rgb(0xF4F4F9),
        Surface = Rgb(0xFAFAFE),
        Card = Rgb(0xFFFFFF),
        Control = Rgb(0xFFFFFF),
        Stroke = Rgb(0xDFDFEA),
        Divider = Rgb(0xE9E9F1),

        TextPrimary = Rgb(0x16161D),
        TextSecondary = Rgb(0x55556B),
        TextTertiary = Rgb(0x7A7A90),
        TextDisabled = Rgb(0xA5A5B6),

        Accent = Rgb(0x3B6FE8),
        AccentHover = Rgb(0x2F60D4),
        AccentPressed = Rgb(0x2450B8),
        TextOnAccent = Rgb(0xFFFFFF),
        AccentText = Rgb(0x2A5FD8),

        Success = Rgb(0x1A7F37),
        Warning = Rgb(0x9A6700),
        Danger = Rgb(0xCF222E),
        Info = Rgb(0x0969DA),

        DiffAdded = Alpha(Rgb(0x1A7F37), 30),
        DiffRemoved = Alpha(Rgb(0xCF222E), 26),
        CodeKeyword = Rgb(0xCF222E),
        CodeString = Rgb(0x0A3069),
        CodeComment = Rgb(0x6E7781),
        CodeNumber = Rgb(0x0550AE),

        Lanes =
        [
            Rgb(0x1F6FEB), Rgb(0x1A7F37), Rgb(0xBC4C00), Rgb(0x8250DF),
            Rgb(0xBF3989), Rgb(0x0F8A8A), Rgb(0x9A6700), Rgb(0x6E7781)
        ]
    };

    /// <summary>GitHub's dark theme, taken from Primer's canvas, fg, accent and status scales.</summary>
    public static ThemePalette GitHubDark { get; } = new()
    {
        Id = "github-dark",
        Name = "GitHub Dark",
        Family = ThemeFamily.GitHub,
        IsDark = true,
        UsesMica = false,

        Window = Rgb(0x0D1117),
        Surface = Rgb(0x010409),
        Card = Rgb(0x161B22),
        Control = Rgb(0x21262D),
        Stroke = Rgb(0x30363D),
        Divider = Rgb(0x21262D),

        TextPrimary = Rgb(0xE6EDF3),
        TextSecondary = Rgb(0x8B949E),
        TextTertiary = Rgb(0x6E7681),
        TextDisabled = Rgb(0x484F58),

        Accent = Rgb(0x1F6FEB),
        AccentHover = Rgb(0x388BFD),
        AccentPressed = Rgb(0x1158C7),
        TextOnAccent = Rgb(0xFFFFFF),
        AccentText = Rgb(0x2F81F7),

        Success = Rgb(0x3FB950),
        Warning = Rgb(0xD29922),
        Danger = Rgb(0xF85149),
        Info = Rgb(0x58A6FF),

        DiffAdded = Alpha(Rgb(0x2EA043), 38),
        DiffRemoved = Alpha(Rgb(0xF85149), 38),
        CodeKeyword = Rgb(0xFF7B72),
        CodeString = Rgb(0xA5D6FF),
        CodeComment = Rgb(0x8B949E),
        CodeNumber = Rgb(0x79C0FF),

        Lanes =
        [
            Rgb(0x58A6FF), Rgb(0x3FB950), Rgb(0xDB6D28), Rgb(0xA371F7),
            Rgb(0xF778BA), Rgb(0x39C5CF), Rgb(0xD29922), Rgb(0x8B949E)
        ]
    };

    /// <summary>GitHub's light theme. White content on a grey frame, as the site has it.</summary>
    public static ThemePalette GitHubLight { get; } = new()
    {
        Id = "github-light",
        Name = "GitHub Light",
        Family = ThemeFamily.GitHub,
        IsDark = false,
        UsesMica = false,

        Window = Rgb(0xFFFFFF),
        Surface = Rgb(0xF6F8FA),
        Card = Rgb(0xFFFFFF),
        Control = Rgb(0xF6F8FA),
        Stroke = Rgb(0xD0D7DE),
        Divider = Rgb(0xD8DEE4),

        TextPrimary = Rgb(0x1F2328),
        TextSecondary = Rgb(0x656D76),
        TextTertiary = Rgb(0x6E7781),
        TextDisabled = Rgb(0x8C959F),

        Accent = Rgb(0x0969DA),
        AccentHover = Rgb(0x0860CA),
        AccentPressed = Rgb(0x0757BA),
        TextOnAccent = Rgb(0xFFFFFF),
        AccentText = Rgb(0x0969DA),

        Success = Rgb(0x1A7F37),
        Warning = Rgb(0x9A6700),
        Danger = Rgb(0xCF222E),
        Info = Rgb(0x0969DA),

        DiffAdded = Alpha(Rgb(0x1F883D), 33),
        DiffRemoved = Alpha(Rgb(0xCF222E), 28),
        CodeKeyword = Rgb(0xCF222E),
        CodeString = Rgb(0x0A3069),
        CodeComment = Rgb(0x6E7781),
        CodeNumber = Rgb(0x0550AE),

        Lanes =
        [
            Rgb(0x0969DA), Rgb(0x1A7F37), Rgb(0xBC4C00), Rgb(0x8250DF),
            Rgb(0xBF3989), Rgb(0x1B7C83), Rgb(0x9A6700), Rgb(0x656D76)
        ]
    };

    /// <summary>
    ///  Visual Studio 2022's dark theme, with the editor's syntax colours for code and lanes.
    /// </summary>
    public static ThemePalette VisualStudioDark { get; } = new()
    {
        Id = "vs-dark",
        Name = "Visual Studio Dark",
        Family = ThemeFamily.VisualStudio,
        IsDark = true,
        UsesMica = false,

        Window = Rgb(0x1F1F1F),
        Surface = Rgb(0x2D2D30),
        Card = Rgb(0x252526),
        Control = Rgb(0x333337),
        Stroke = Rgb(0x3F3F46),
        Divider = Rgb(0x3F3F46),

        TextPrimary = Rgb(0xF1F1F1),
        TextSecondary = Rgb(0xB9B9B9),
        TextTertiary = Rgb(0x9B9B9B),
        TextDisabled = Rgb(0x6D6D6D),

        Accent = Rgb(0x0078D4),
        AccentHover = Rgb(0x1A88DE),
        AccentPressed = Rgb(0x005A9E),
        TextOnAccent = Rgb(0xFFFFFF),
        AccentText = Rgb(0x569CD6),

        Success = Rgb(0x73C991),
        Warning = Rgb(0xCCA700),
        Danger = Rgb(0xF14C4C),
        Info = Rgb(0x569CD6),

        DiffAdded = Alpha(Rgb(0x9BB955), 45),
        DiffRemoved = Alpha(Rgb(0xE04A4A), 45),
        CodeKeyword = Rgb(0x569CD6),
        CodeString = Rgb(0xCE9178),
        CodeComment = Rgb(0x6A9955),
        CodeNumber = Rgb(0xB5CEA8),

        Lanes =
        [
            Rgb(0x569CD6), Rgb(0x73C991), Rgb(0xCE9178), Rgb(0xC586C0),
            Rgb(0xD16969), Rgb(0x4EC9B0), Rgb(0xDCDCAA), Rgb(0x9B9B9B)
        ]
    };

    /// <summary>
    ///  Visual Studio 2022's light theme, including its long-standing syntax colours — blue keywords,
    ///  dark red strings, green comments.
    /// </summary>
    public static ThemePalette VisualStudioLight { get; } = new()
    {
        Id = "vs-light",
        Name = "Visual Studio Light",
        Family = ThemeFamily.VisualStudio,
        IsDark = false,
        UsesMica = false,

        Window = Rgb(0xEEEEF2),
        Surface = Rgb(0xF5F5F5),
        Card = Rgb(0xFFFFFF),
        Control = Rgb(0xFFFFFF),
        Stroke = Rgb(0xCCCEDB),
        Divider = Rgb(0xE0E0E6),

        TextPrimary = Rgb(0x1E1E1E),
        TextSecondary = Rgb(0x605E5C),
        TextTertiary = Rgb(0x8A8886),
        TextDisabled = Rgb(0xA19F9D),

        Accent = Rgb(0x005FB8),
        AccentHover = Rgb(0x0078D4),
        AccentPressed = Rgb(0x004C99),
        TextOnAccent = Rgb(0xFFFFFF),
        AccentText = Rgb(0x0066B4),

        Success = Rgb(0x107C10),
        Warning = Rgb(0x9D5D00),
        Danger = Rgb(0xA1260D),
        Info = Rgb(0x005FB8),

        DiffAdded = Alpha(Rgb(0x107C10), 30),
        DiffRemoved = Alpha(Rgb(0xA1260D), 28),
        CodeKeyword = Rgb(0x0000FF),
        CodeString = Rgb(0xA31515),
        CodeComment = Rgb(0x008000),
        CodeNumber = Rgb(0x098658),

        Lanes =
        [
            Rgb(0x005FB8), Rgb(0x107C10), Rgb(0xA31515), Rgb(0x8F3FBF),
            Rgb(0xC2185B), Rgb(0x00706B), Rgb(0x9D5D00), Rgb(0x605E5C)
        ]
    };

    /// <summary>
    ///  Neon on near-black: magenta accent, cyan links, a violet cast through the greys.
    /// </summary>
    public static ThemePalette CyberpunkDark { get; } = new()
    {
        Id = "cyberpunk-dark",
        Name = "Cyberpunk Dark",
        Family = ThemeFamily.Cyberpunk,
        IsDark = true,
        UsesMica = false,

        Window = Rgb(0x0A0613),
        Surface = Rgb(0x110A22),
        Card = Rgb(0x17102C),
        Control = Rgb(0x1E1538),
        Stroke = Rgb(0x33235C),
        Divider = Rgb(0x281C4A),

        TextPrimary = Rgb(0xEDE7FF),
        TextSecondary = Rgb(0xA99BD9),
        TextTertiary = Rgb(0x7F70B0),
        TextDisabled = Rgb(0x5B4E84),

        Accent = Rgb(0xFF2E88),
        AccentHover = Rgb(0xFF5CA3),
        AccentPressed = Rgb(0xD6206E),
        TextOnAccent = Rgb(0x0A0613),
        AccentText = Rgb(0x00F0FF),

        Success = Rgb(0x00FF9C),
        Warning = Rgb(0xFFD166),
        Danger = Rgb(0xFF3864),
        Info = Rgb(0x00F0FF),

        DiffAdded = Alpha(Rgb(0x00FF9C), 34),
        DiffRemoved = Alpha(Rgb(0xFF3864), 34),
        CodeKeyword = Rgb(0xFF2E88),
        CodeString = Rgb(0x00FFC6),
        CodeComment = Rgb(0x6E5FA0),
        CodeNumber = Rgb(0xFFE347),

        Lanes =
        [
            Rgb(0x00F0FF), Rgb(0xFF2E88), Rgb(0xB6FF00), Rgb(0xA855F7),
            Rgb(0xFF7A00), Rgb(0x00FFC6), Rgb(0xFFE347), Rgb(0x8A7CC0)
        ]
    };

    /// <summary>
    ///  The same palette in daylight: the neons darkened until they hold contrast on white, over a
    ///  faintly violet paper.
    /// </summary>
    public static ThemePalette CyberpunkLight { get; } = new()
    {
        Id = "cyberpunk-light",
        Name = "Cyberpunk Light",
        Family = ThemeFamily.Cyberpunk,
        IsDark = false,
        UsesMica = false,

        Window = Rgb(0xF7F2FF),
        Surface = Rgb(0xFDFAFF),
        Card = Rgb(0xFFFFFF),
        Control = Rgb(0xFFFFFF),
        Stroke = Rgb(0xDCC8FF),
        Divider = Rgb(0xEBE0FF),

        TextPrimary = Rgb(0x1A0B2E),
        TextSecondary = Rgb(0x584379),
        TextTertiary = Rgb(0x8073A6),
        TextDisabled = Rgb(0xA79CC4),

        Accent = Rgb(0xD6006E),
        AccentHover = Rgb(0xB80059),
        AccentPressed = Rgb(0x96004A),
        TextOnAccent = Rgb(0xFFFFFF),
        AccentText = Rgb(0x0086A8),

        Success = Rgb(0x00875A),
        Warning = Rgb(0xB45309),
        Danger = Rgb(0xD10F45),
        Info = Rgb(0x0086A8),

        DiffAdded = Alpha(Rgb(0x00875A), 30),
        DiffRemoved = Alpha(Rgb(0xD10F45), 26),
        CodeKeyword = Rgb(0xD6006E),
        CodeString = Rgb(0x00707F),
        CodeComment = Rgb(0x8073A6),
        CodeNumber = Rgb(0xB45309),

        Lanes =
        [
            Rgb(0x0086A8), Rgb(0x00875A), Rgb(0xD6006E), Rgb(0x7A00C2),
            Rgb(0xB45309), Rgb(0x0F766E), Rgb(0xA16207), Rgb(0x6B5B95)
        ]
    };

    /// <summary>Every theme, in the order the picker shows them: dark then light, family by family.</summary>
    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        GitxenDark, GitxenLight,
        GitHubDark, GitHubLight,
        VisualStudioDark, VisualStudioLight,
        CyberpunkDark, CyberpunkLight
    ];

    /// <summary>
    ///  The palette for a stored choice.
    /// </summary>
    /// <param name="theme">The stored choice.</param>
    /// <param name="systemIsDark">
    ///  What Windows is currently set to, which is what <see cref="AppTheme.System"/> follows.
    /// </param>
    public static ThemePalette Resolve(AppTheme theme, bool systemIsDark) => theme switch
    {
        AppTheme.Light => GitxenLight,
        AppTheme.Dark => GitxenDark,
        AppTheme.GitHubDark => GitHubDark,
        AppTheme.GitHubLight => GitHubLight,
        AppTheme.VisualStudioDark => VisualStudioDark,
        AppTheme.VisualStudioLight => VisualStudioLight,
        AppTheme.CyberpunkDark => CyberpunkDark,
        AppTheme.CyberpunkLight => CyberpunkLight,

        // System, and anything a hand-edited session file invented.
        _ => systemIsDark ? GitxenDark : GitxenLight
    };

    /// <summary>The stored choice that selects a given palette outright, never following Windows.</summary>
    public static AppTheme ThemeFor(ThemePalette palette) => palette.Id switch
    {
        "gitxen-light" => AppTheme.Light,
        "github-dark" => AppTheme.GitHubDark,
        "github-light" => AppTheme.GitHubLight,
        "vs-dark" => AppTheme.VisualStudioDark,
        "vs-light" => AppTheme.VisualStudioLight,
        "cyberpunk-dark" => AppTheme.CyberpunkDark,
        "cyberpunk-light" => AppTheme.CyberpunkLight,
        _ => AppTheme.Dark
    };
}
