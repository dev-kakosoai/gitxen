namespace GitExtensions.WinUI.Services;

public enum AppTheme
{
    System,
    Light,
    Dark
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

    /// <summary>Tabs across the top, or a column down the left.</summary>
    public static RepositoryLayout Layout { get; set; } = RepositoryLayout.Tabs;

    public static void Apply(SessionState state)
    {
        // Guard against a hand-edited session file putting in something unusable.
        MaxCommits = Math.Clamp(state.MaxCommits, 100, 50_000);
        MaxDiffLines = Math.Clamp(state.MaxDiffLines, 100, 200_000);
        Theme = state.Theme;
        Layout = state.Layout;
    }

    public static void CopyTo(SessionState state)
    {
        state.MaxCommits = MaxCommits;
        state.MaxDiffLines = MaxDiffLines;
        state.Theme = Theme;
        state.Layout = Layout;
    }
}
