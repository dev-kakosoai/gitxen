using Microsoft.UI.Xaml.Media;

namespace GitExtensions.WinUI.Graph;

/// <summary>
///  One drawable piece of a commit-graph row: either the lane lines of a single colour, or the
///  commit dot. Pre-built so the row template is a plain binding with no drawing code behind it.
/// </summary>
public sealed class GraphSegment
{
    public GraphSegment(Geometry geometry, Brush? stroke, Brush? fill)
    {
        Geometry = geometry;
        Stroke = stroke;
        Fill = fill;
    }

    public Geometry Geometry { get; }

    /// <summary>Set for lane lines, null for the dot.</summary>
    public Brush? Stroke { get; }

    /// <summary>Set for the dot, null for lane lines.</summary>
    public Brush? Fill { get; }
}
