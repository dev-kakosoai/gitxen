using System.Collections.ObjectModel;
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

    public Brush Accent => GroupPalette.Accent(ColorKey);

    /// <summary>The header fill — the same hue, faint enough to sit behind text.</summary>
    public Brush Tint => GroupPalette.Tint(ColorKey);

    public void RaiseCountChanged() => OnPropertyChanged(nameof(Count));
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
    private static readonly Dictionary<string, Color> _colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blue"] = Color.FromArgb(255, 88, 166, 255),
        ["Green"] = Color.FromArgb(255, 63, 185, 80),
        ["Purple"] = Color.FromArgb(255, 188, 140, 255),
        ["Amber"] = Color.FromArgb(255, 210, 168, 65),
        ["Rose"] = Color.FromArgb(255, 233, 105, 134),
        ["Teal"] = Color.FromArgb(255, 86, 211, 200),
        ["Slate"] = Color.FromArgb(255, 145, 152, 161)
    };

    private static readonly Dictionary<string, SolidColorBrush> _accents = _colors
        .ToDictionary(pair => pair.Key, pair => new SolidColorBrush(pair.Value), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, SolidColorBrush> _tints = _colors
        .ToDictionary(
            pair => pair.Key,
            pair => new SolidColorBrush(Color.FromArgb(38, pair.Value.R, pair.Value.G, pair.Value.B)),
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Keys => [.. _colors.Keys];

    public static string Default => "Blue";

    public static Brush Accent(string key) =>
        _accents.TryGetValue(key, out SolidColorBrush? brush) ? brush : _accents[Default];

    public static Brush Tint(string key) =>
        _tints.TryGetValue(key, out SolidColorBrush? brush) ? brush : _tints[Default];
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
