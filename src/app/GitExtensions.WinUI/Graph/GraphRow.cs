using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace GitExtensions.WinUI.Graph;

/// <summary>
///  One lane line of a commit-graph row, already positioned in the row's coordinate space.
/// </summary>
/// <remarks>
///  A plain struct rather than a <see cref="PathFigure"/>: the lane layout is computed for every
///  commit that loads, but only the handful of rows the list actually realises ever need drawing.
///  See <see cref="GraphRow"/>.
/// </remarks>
public readonly record struct GraphLine(int Lane, double X1, double Y1, double X2, double Y2);

/// <summary>
///  The lane layout of one commit-graph row, and the XAML drawing built from it on demand.
/// </summary>
/// <remarks>
///  <para>
///   Splitting the row in two is what keeps loading a page cheap. The layout — which lanes pass
///   through, where the dot sits — has to be computed as each commit arrives, because a lane is only
///   meaningful relative to the commits already placed. The drawing does not: the commit list is
///   virtualised, so of the 2,000 rows a page loads only about thirty are ever realised.
///  </para>
///  <para>
///   Building the drawing eagerly meant roughly 37,000 <see cref="PathFigure"/>,
///   <see cref="LineSegment"/> and <see cref="Geometry"/> objects per page on this repository — all
///   of them WinRT objects, constructed on the UI thread, and about 98% of them never shown.
///  </para>
/// </remarks>
public sealed class GraphRow
{
    /// <summary>A row with nothing to draw: the working-directory row, which is not a commit.</summary>
    public static GraphRow Empty { get; } = new([], dotLane: -1, dotX: 0, rowHeight: 0);

    private readonly GraphLine[] _lines;
    private readonly int _dotLane;
    private readonly double _dotX;
    private readonly double _rowHeight;

    /// <summary>Built on first request and kept, so scrolling back over a row does not rebuild it.</summary>
    private IReadOnlyList<GraphSegment>? _segments;

    internal GraphRow(GraphLine[] lines, int dotLane, double dotX, double rowHeight)
    {
        _lines = lines;
        _dotLane = dotLane;
        _dotX = dotX;
        _rowHeight = rowHeight;
    }

    /// <summary>
    ///  The drawable pieces of this row, built the first time the row is realised.
    /// </summary>
    public IReadOnlyList<GraphSegment> Segments => _segments ??= Build();

    private IReadOnlyList<GraphSegment> Build()
    {
        if (_dotLane < 0)
        {
            return [];
        }

        // Lines by colour, so a row emits one Path per colour rather than one per segment.
        Dictionary<int, PathFigureCollection> figuresByLane = [];

        foreach (GraphLine line in _lines)
        {
            if (!figuresByLane.TryGetValue(line.Lane, out PathFigureCollection? figures))
            {
                figures = [];
                figuresByLane[line.Lane] = figures;
            }

            PathFigure figure = new() { StartPoint = new Point(line.X1, line.Y1) };
            figure.Segments.Add(new LineSegment { Point = new Point(line.X2, line.Y2) });
            figures.Add(figure);
        }

        List<GraphSegment> segments = new(figuresByLane.Count + 1);

        foreach ((int lane, PathFigureCollection figures) in figuresByLane)
        {
            segments.Add(new GraphSegment(
                new PathGeometry { Figures = figures },
                CommitGraphBuilder.BrushFor(lane),
                fill: null));
        }

        // The dot goes last so it paints over the lines meeting underneath it.
        segments.Add(new GraphSegment(
            new EllipseGeometry { Center = new Point(_dotX, _rowHeight / 2), RadiusX = 4, RadiusY = 4 },
            stroke: null,
            fill: CommitGraphBuilder.BrushFor(_dotLane)));

        return segments;
    }
}
