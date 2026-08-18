using GitExtensions.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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
    private RepositoryGroup? _group;

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

    /// <summary>
    ///  The project this tab belongs to, assigned by the shell when the grouping changes.
    /// </summary>
    /// <remarks>
    ///  Held on the tab rather than looked up each time so a tab can show its project colour without
    ///  every template needing a reference to the view model.
    /// </remarks>
    public RepositoryGroup? Group
    {
        get => _group;
        set
        {
            if (SetProperty(ref _group, value))
            {
                OnPropertyChanged(nameof(Accent));
                OnPropertyChanged(nameof(GroupVisibility));
                OnPropertyChanged(nameof(GroupName));
            }
        }
    }

    public Brush Accent => Group?.Accent ?? GroupPalette.Accent("Slate");

    /// <summary>The colour cue is only meaningful when the tab is actually in a project.</summary>
    public Visibility GroupVisibility => Group is null ? Visibility.Collapsed : Visibility.Visible;

    public string GroupName => Group?.Name ?? "";
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
