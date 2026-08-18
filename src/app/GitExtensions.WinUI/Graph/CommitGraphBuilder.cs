using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Theming;
using GitUIPluginInterfaces;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace GitExtensions.WinUI.Graph;

/// <summary>
///  Builds the commit-graph lane drawing, one row at a time, in the order commits arrive from
///  <c>git log</c>.
/// </summary>
/// <remarks>
///  <para>
///   Standard lane assignment: a lane is a column waiting for a specific commit. When that commit
///   arrives it takes the lane, the lane starts waiting for the commit's first parent, and any
///   further parents (a merge) either join an existing lane or open a new one.
///  </para>
///  <para>
///   Deliberately independent of the WinForms app's graph engine, which lives in GitUI and would
///   drag the whole WinForms UI assembly in with it.
///  </para>
/// </remarks>
public sealed class CommitGraphBuilder
{
    public const double RowHeight = 26;

    private const double LaneWidth = 14;

    /// <summary>Beyond this the graph stops being readable and just eats horizontal space.</summary>
    private const int MaxLanes = 8;

    /// <summary>
    ///  Fixed rather than grown to fit: a width that changed as new lanes appeared would shift every
    ///  column to its right and leave already-rendered rows misaligned.
    /// </summary>
    public const double ColumnWidth = (MaxLanes * LaneWidth) + 6;

    /// <summary>
    ///  Lane colours come from the theme, which is also where repository group accents come from.
    /// </summary>
    /// <remarks>
    ///  These brush instances outlive a theme change -- only their colour is reassigned -- so a graph
    ///  that is already on screen recolours itself without being rebuilt.
    /// </remarks>
    private static IReadOnlyList<SolidColorBrush> LaneBrushes => ThemeBrushes.Lanes;

    /// <summary>Which commit each lane is currently waiting for; null means the lane is free.</summary>
    private readonly List<ObjectId?> _lanes = [];

    public IReadOnlyList<GraphSegment> AddCommit(GitRevision revision)
    {
        // Lines by colour, so a row emits one Path per colour rather than one per segment.
        Dictionary<int, PathFigureCollection> figuresByLane = [];

        int myLane = TakeLane(revision.ObjectId);

        // Everything still waiting at the top of this row arrives from above.
        for (int lane = 0; lane < _lanes.Count; lane++)
        {
            if (_lanes[lane] is null)
            {
                continue;
            }

            // The commit's own lane stops at the dot; the rest pass straight through.
            double endY = lane == myLane ? RowHeight / 2 : RowHeight;
            AddLine(figuresByLane, lane, X(lane), 0, X(lane), endY);
        }

        IReadOnlyList<ObjectId>? parents = revision.ParentIds;
        ObjectId? firstParent = parents is { Count: > 0 } ? parents[0] : null;

        // The commit's lane carries on downwards waiting for its first parent.
        _lanes[myLane] = firstParent;
        if (firstParent is not null)
        {
            AddLine(figuresByLane, myLane, X(myLane), RowHeight / 2, X(myLane), RowHeight);
        }

        // A merge sends an extra edge out to each additional parent.
        if (parents is not null)
        {
            for (int i = 1; i < parents.Count; i++)
            {
                int parentLane = TakeLane(parents[i]);
                AddLine(figuresByLane, parentLane, X(myLane), RowHeight / 2, X(parentLane), RowHeight);
            }
        }

        TrimTrailingFreeLanes();

        List<GraphSegment> segments = [];
        foreach ((int lane, PathFigureCollection figures) in figuresByLane)
        {
            segments.Add(new GraphSegment(new PathGeometry { Figures = figures }, BrushFor(lane), fill: null));
        }

        // The dot goes last so it paints over the lines meeting underneath it.
        segments.Add(new GraphSegment(
            new EllipseGeometry { Center = new Point(X(myLane), RowHeight / 2), RadiusX = 4, RadiusY = 4 },
            stroke: null,
            fill: BrushFor(myLane)));

        return segments;
    }

    /// <summary>
    ///  The lane already waiting for <paramref name="objectId"/>, or a newly opened one.
    /// </summary>
    private int TakeLane(ObjectId objectId)
    {
        int existing = _lanes.FindIndex(waiting => waiting is not null && waiting.Equals(objectId));
        if (existing >= 0)
        {
            return existing;
        }

        int free = _lanes.IndexOf(null);
        if (free >= 0)
        {
            _lanes[free] = objectId;
            return free;
        }

        _lanes.Add(objectId);
        return _lanes.Count - 1;
    }

    private void TrimTrailingFreeLanes()
    {
        while (_lanes.Count > 0 && _lanes[^1] is null)
        {
            _lanes.RemoveAt(_lanes.Count - 1);
        }
    }

    private static void AddLine(Dictionary<int, PathFigureCollection> figuresByLane, int lane, double x1, double y1, double x2, double y2)
    {
        if (!figuresByLane.TryGetValue(lane, out PathFigureCollection? figures))
        {
            figures = [];
            figuresByLane[lane] = figures;
        }

        PathFigure figure = new() { StartPoint = new Point(x1, y1) };
        figure.Segments.Add(new LineSegment { Point = new Point(x2, y2) });
        figures.Add(figure);
    }

    private static double X(int lane) => (Math.Min(lane, MaxLanes) * LaneWidth) + (LaneWidth / 2);

    private static Brush BrushFor(int lane) => LaneBrushes[lane % LaneBrushes.Count];
}
