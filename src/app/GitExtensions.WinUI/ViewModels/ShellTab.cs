namespace GitExtensions.WinUI.ViewModels;

/// <summary>
///  One entry in the shell: the Home tab, or an open repository.
/// </summary>
/// <remarks>
///  A common base lets the tab strip and the repository column bind them from a single template,
///  which they need — a template can only bind against one type.
/// </remarks>
public abstract class ShellTab : ObservableObject
{
    /// <summary>Shown on the tab or row.</summary>
    public abstract string Title { get; }

    /// <summary>Full path or description, shown underneath and as the tooltip.</summary>
    public abstract string Description { get; }

    /// <summary>Home cannot be closed; there would be nothing left to return to.</summary>
    public abstract bool IsClosable { get; }

    /// <summary>
    ///  Which icon to show. A flag rather than a glyph string, so the glyphs stay in the XAML with
    ///  the rest of the app's icons.
    /// </summary>
    public abstract bool IsHome { get; }
}

/// <summary>
///  The start page: where you open a repository from, and what the window shows before you have.
/// </summary>
/// <remarks>
///  A tab rather than a screen the shell falls back to when nothing is open, so it stays reachable
///  while repositories are open — the recent list and the open/clone actions are just as useful then.
///  The same reason a code editor keeps its welcome tab around.
/// </remarks>
public sealed class HomeTabViewModel : ShellTab
{
    public override string Title => "Home";

    public override string Description => "Open a repository, or pick up where you left off";

    public override bool IsClosable => false;

    public override bool IsHome => true;
}
