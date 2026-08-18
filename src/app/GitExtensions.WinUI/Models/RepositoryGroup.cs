using System.Collections.ObjectModel;
using System.Globalization;
using GitExtensions.WinUI.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GitExtensions.WinUI.Models;

/// <summary>
///  A named set of repositories that are worked on together.
/// </summary>
/// <remarks>
///  Projects are rarely one repository. Grouping them means the Home page can be organised the way
///  the work actually is, rather than as one flat most-recently-used list in which the four
///  repositories of the thing you are building sit scattered among everything else you opened.
/// </remarks>
public sealed class RepositoryGroup : ObservableObject
{
    private string _name;
    private string _glyph;
    private string _colorKey;
    private bool _isExpanded = true;

    public RepositoryGroup(string name, string glyph, string colorKey)
    {
        _name = name;
        _glyph = glyph;
        _colorKey = colorKey;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Segoe Fluent glyph, chosen from a fixed set so no arbitrary text can end up here.</summary>
    public string Glyph
    {
        get => _glyph;
        set => SetProperty(ref _glyph, value);
    }

    /// <summary>Key into <see cref="GroupPalette"/>; stored by name so the file stays readable.</summary>
    public string ColorKey
    {
        get => _colorKey;
        set
        {
            if (SetProperty(ref _colorKey, value))
            {
                OnPropertyChanged(nameof(Accent));
                OnPropertyChanged(nameof(Tint));
            }
        }
    }

    /// <summary>Collapsed groups persist that way; a long list of projects is the point of collapsing.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ContentVisibility));
            }
        }
    }

    public Visibility ContentVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<RecentRepository> Repositories { get; } = [];

    public int Count => Repositories.Count;

    /// <summary>
    ///  The count as text, for a plain dim number beside the name.
    /// </summary>
    /// <remarks>
    ///  A header that carries a colour, an icon and a name is already saying enough; a filled badge
    ///  on top of that competes with all three for a number nobody is being alerted about.
    /// </remarks>
    public string CountText => Count.ToString(CultureInfo.CurrentCulture);

    public Brush Accent => GroupPalette.Accent(ColorKey);

    /// <summary>The header fill — the same hue, faint enough to sit behind text.</summary>
    public Brush Tint => GroupPalette.Tint(ColorKey);

    public void RaiseCountChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountText));
    }
}

/// <summary>
///  The colours a group can be given.
/// </summary>
/// <remarks>
///  A fixed palette rather than a full colour picker: the point is telling groups apart at a glance,
///  which needs hues that are distinct from each other and legible on both the light and dark
///  surfaces. An arbitrary picker mostly produces colours that fail one of those.
/// </remarks>
public static class GroupPalette
{
    /// <summary>
    ///  The colour names a group can be given, and which of the theme's eight categorical colours
    ///  each one maps to.
    /// </summary>
    /// <remarks>
    ///  Names rather than values, because the name is what gets written to the session file and has to
    ///  keep meaning the same thing. The values behind them come from the active theme, so a group
    ///  picked out in green stays legible when the surface it sits on changes from near-black to white.
    ///  Sharing the graph's lane colours is deliberate: both need hues that are told apart at a glance,
    ///  and two lists would drift.
    /// </remarks>
    private static readonly Dictionary<string, int> _lanes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blue"] = 0,
        ["Green"] = 1,
        ["Purple"] = 3,
        ["Amber"] = 6,
        ["Rose"] = 4,
        ["Teal"] = 5,
        ["Slate"] = 7
    };

    public static IReadOnlyList<string> Keys => [.. _lanes.Keys];

    public static string Default => "Blue";

    public static Brush Accent(string key) => ThemeBrushes.Lanes[Lane(key)];

    /// <summary>The same colour as a wash, for the group header to sit on.</summary>
    public static Brush Tint(string key) => ThemeBrushes.LaneTints[Lane(key)];

    private static int Lane(string key) =>
        _lanes.TryGetValue(key, out int lane) ? lane : _lanes[Default];
}

/// <summary>
///  The icons a group can be given, as glyph/label pairs for the picker.
/// </summary>
/// <remarks>
///  Kept here rather than in XAML so the same list drives both the picker and the round-trip through
///  the session file, and so a stored glyph that is no longer offered still renders.
/// </remarks>
public static class GroupIcons
{
    public static IReadOnlyList<(string Glyph, string Label)> All =>
    [
        ("\uE8B7", "Folder"),
        ("\uE821", "Project"),
        ("\uE7B8", "Box"),
        ("\uE774", "Web"),
        ("\uE8F1", "Library"),
        ("\uE716", "Team"),
        ("\uE734", "Starred"),
        ("\uE90F", "Tools"),
        ("\uE945", "Idea"),
        ("\uE7C1", "Flag")
    ];

    public static string Default => "\uE8B7";
}
