using System.Collections.ObjectModel;
using GitExtensions.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace GitExtensions.WinUI.ViewModels;

/// <summary>
///  A run of open repositories that belong to the same project, for the repository column.
/// </summary>
/// <remarks>
///  The column groups its rows with a real grouped list rather than by nesting a list per project:
///  nesting would give each project its own selection, and the column has to have exactly one. The
///  grouped list keeps one selection across every section.
/// </remarks>
public sealed class ShellTabGroup : ObservableObject
{
    public ShellTabGroup(RepositoryGroup? group)
    {
        Group = group;
    }

    /// <summary>The project, or null for the repositories that are in none.</summary>
    public RepositoryGroup? Group { get; }

    public ObservableCollection<ShellTab> Items { get; } = [];

    public string Name => Group?.Name ?? "Not in a project";

    public string Glyph => Group?.Glyph ?? GroupIcons.Default;

    public Brush Accent => Group?.Accent ?? GroupPalette.Accent("Slate");

    public Brush Tint => Group?.Tint ?? GroupPalette.Tint("Slate");

    public int Count => Items.Count;

    /// <summary>The count as text, shown as a plain dim number rather than a badge.</summary>
    public string CountText => Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>
    ///  Collapsed groups still show their header — that is what you click to bring them back — so the
    ///  chevron is rotated rather than the header hidden.
    /// </summary>
    public bool IsExpanded => Group?.IsExpanded ?? true;

    public double ChevronRotation => IsExpanded ? 0 : -90;

    /// <summary>The ungrouped section has no project to edit, so it offers no controls.</summary>
    public Visibility ActionsVisibility => Group is null ? Visibility.Collapsed : Visibility.Visible;

    public void RaiseChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(ChevronRotation));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Accent));
        OnPropertyChanged(nameof(Tint));
    }
}
