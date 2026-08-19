namespace GitExtensions.WinUI.Services;

/// <summary>
///  Which theme the shell uses.
/// </summary>
/// <remarks>
///  Serialized by name into the session file, so members can be added freely but not renamed.
///  <c>Light</c> and <c>Dark</c> are Gitxen's own two themes and keep those names because sessions
///  written before there were eight themes still hold them.
/// </remarks>
public enum AppTheme
{
    /// <summary>Follow the Windows light/dark setting, using Gitxen Light and Gitxen Dark.</summary>
    System,

    /// <summary>Gitxen Light.</summary>
    Light,

    /// <summary>Gitxen Dark.</summary>
    Dark,

    GitHubDark,
    GitHubLight,
    VisualStudioDark,
    VisualStudioLight,
    CyberpunkDark,
    CyberpunkLight
}

/// <summary>How tightly the object lists pack their rows.</summary>
public enum UiDensity
{
    /// <summary>The shipped spacing.</summary>
    Comfortable,

    /// <summary>Tighter rows: roughly a fifth more commits on the same screen.</summary>
    Compact
}

/// <summary>How the open repositories are listed.</summary>
public enum RepositoryLayout
{
    /// <summary>A tab strip across the top, like a browser.</summary>
    Tabs,

    /// <summary>A column down the left, which suits long repository names and many repositories.</summary>
    Sidebar
}

/// <summary>
///  User-adjustable settings, loaded from the session file at startup.
/// </summary>
/// <remarks>
///  Static because the values are process-wide and read from view models that are created per tab;
///  threading them through every constructor would buy nothing for a single-window personal app.
/// </remarks>
public static class AppOptions
{
    /// <summary>Commits read per page. See RepositoryTabViewModel for why this is capped at all.</summary>
    public static int MaxCommits { get; set; } = 2000;

    public static int MaxDiffLines { get; set; } = 5000;

    public static AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    ///  Minutes between background fetches; 0 turns them off.
    /// </summary>
    /// <remarks>
    ///  Ahead/behind counts come from the remote-tracking refs, which only move when something
    ///  fetches. Without this the toolbar reports "up to date" indefinitely while the remote moves on.
    /// </remarks>
    public static int AutoFetchMinutes { get; set; } = 10;

    /// <summary>Tabs across the top, or a column down the left.</summary>
    public static RepositoryLayout Layout { get; set; } = RepositoryLayout.Tabs;

    /// <summary>
    ///  Width of the staging pane on the Changes page.
    /// </summary>
    /// <remarks>
    ///  Pane widths are options rather than per-repository state: a splitter is dragged to suit the
    ///  monitor and the reader, not the repository, so every tab shares one value. The clamps match
    ///  the PaneSplitter Minimum/Maximum in the XAML that drags them.
    /// </remarks>
    public static double StagingPaneWidth { get; set; } = 400;

    /// <summary>Width of the commit-details pane on the History page.</summary>
    public static double HistoryDetailsWidth { get; set; } = 380;

    /// <summary>Height of the bottom terminal pane.</summary>
    public static double TerminalPaneHeight { get; set; } = 280;

    /// <summary>Row spacing for the object lists and the commit graph.</summary>
    public static UiDensity Density { get; set; } = UiDensity.Comfortable;

    public static void Apply(SessionState state)
    {
        // Guard against a hand-edited session file putting in something unusable.
        MaxCommits = Math.Clamp(state.MaxCommits, 100, 50_000);
        MaxDiffLines = Math.Clamp(state.MaxDiffLines, 100, 200_000);
        Theme = state.Theme;
        AutoFetchMinutes = Math.Clamp(state.AutoFetchMinutes, 0, 240);
        Layout = state.Layout;
        StagingPaneWidth = Math.Clamp(state.StagingPaneWidth, 300, 800);
        HistoryDetailsWidth = Math.Clamp(state.HistoryDetailsWidth, 260, 900);
        TerminalPaneHeight = Math.Clamp(state.TerminalPaneHeight, 140, 700);
        Density = state.Density;
    }

    public static void CopyTo(SessionState state)
    {
        state.MaxCommits = MaxCommits;
        state.MaxDiffLines = MaxDiffLines;
        state.Theme = Theme;
        state.AutoFetchMinutes = AutoFetchMinutes;
        state.Layout = Layout;
        state.StagingPaneWidth = StagingPaneWidth;
        state.HistoryDetailsWidth = HistoryDetailsWidth;
        state.TerminalPaneHeight = TerminalPaneHeight;
        state.Density = Density;
    }
}
