using GitExtensions.WinUI.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace GitExtensions.WinUI.ViewModels;

/// <summary>
///  One repository row in the repository column or the tab strip, open or not.
/// </summary>
/// <remarks>
///  The column and the strip list the same repositories Home does — every project member, not only
///  the open ones — so the three views always agree. A row for a repository that is not open opens it
///  when clicked; a row for an open one switches to its tab, carries its close button, and shows its
///  title bold. The strip also carries a Home row, since Home is a tab there rather than a button.
/// </remarks>
public sealed class SidebarRow
{
    public SidebarRow(RecentRepository entry, RepositoryTabViewModel? openTab, RepositoryGroup? group)
    {
        Entry = entry;
        OpenTab = openTab;
        Group = group;
    }

    private SidebarRow()
    {
    }

    /// <summary>The strip's Home entry; the column has a Home button instead.</summary>
    public static SidebarRow ForHome() => new();

    public RecentRepository? Entry { get; }

    /// <summary>The open tab this row stands for, null while the repository is not open.</summary>
    public RepositoryTabViewModel? OpenTab { get; }

    /// <summary>The project the row is sectioned under, for the colour stripe.</summary>
    public RepositoryGroup? Group { get; }

    public bool IsHome => Entry is null && OpenTab is null && Group is null;

    public string Title => Entry?.Name ?? "Home";

    public string Path => Entry?.Path ?? "";

    public string GroupName => Group?.Name ?? "";

    public string Glyph => IsHome ? "\uE80F" : "\uE8B7";

    public string ToolTipText => IsHome ? "Open a repository, or pick up where you left off" : Path;

    public bool IsOpen => OpenTab is not null;

    /// <summary>Bold marks the repositories that are actually open among their project's members.</summary>
    public FontWeight TitleWeight => IsOpen ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>
    ///  Every repository row can be removed from the lists — closing its tab first when it is open.
    ///  Home is the one row that cannot leave.
    /// </summary>
    public Visibility RemoveVisibility => IsHome ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The close cross belongs only to rows that stand for an open tab.</summary>
    public Visibility CloseVisibility => IsOpen ? Visibility.Visible : Visibility.Collapsed;

    public Brush Accent => Group?.Accent ?? GroupPalette.Accent("Slate");

    /// <summary>The colour cue is only meaningful when the repository is actually in a project.</summary>
    public Visibility GroupVisibility => Group is null ? Visibility.Collapsed : Visibility.Visible;
}
