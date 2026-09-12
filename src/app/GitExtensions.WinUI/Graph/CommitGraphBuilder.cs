using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Theming;
using GitUIPluginInterfaces;
using Microsoft.UI.Xaml.Media;

namespace GitExtensions.WinUI.Graph;

/// <summary>
///  Builds the commit-graph lane layout, one row at a time, in the order commits arrive from
///  <c>git log</c>.
/// </summary>
/// <remarks>
///  <para>
///   Standard lane assignment: a lane is a column waiting for a specific commit. When that commit
///   arrives it takes the lane, the lane starts waiting for the commit's first parent, and any
///   further parents (a merge) either join an existing lane or open a new one.
///  </para>
///  <para>
///   What comes out is a <see cref="GraphRow"/> of plain coordinates, not XAML. The lane layout must
///   be computed for every commit that loads — a lane only means anything relative to the commits
///   already placed — but the drawing is built later, for the few rows the list actually realises.
///  </para>
///  <para>
///   Deliberately independent of the WinForms app's graph engine, which lives in GitUI and would
///   drag the whole WinForms UI assembly in with it.
///  </para>
/// </remarks>
public sealed class CommitGraphBuilder
{
    /// <summary>
    ///  Height of one commit row, and therefore of the lane geometry drawn behind it.
    /// </summary>
    /// <remarks>
    ///  Read as each row is laid out rather than as a constant, so a density change takes effect as
    ///  the list refreshes without touching rows that are already placed.
    /// </remarks>
    public static double RowHeight => Services.AppOptions.Density == Services.UiDensity.Compact ? 21 : 26;

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

    /// <summary>Reused between rows: the lines are copied out into each row's own array.</summary>
    private readonly List<GraphLine> _lines = [];

    public GraphRow AddCommit(GitRevision revision)
    {
        _lines.Clear();

        double rowHeight = RowHeight;
        int myLane = TakeLane(revision.ObjectId);

        // Everything still waiting at the top of this row arrives from above.
        for (int lane = 0; lane < _lanes.Count; lane++)
        {
            // Every lane past the overflow column is drawn at the same x as the overflow column
            // itself, so drawing them would stack identical lines in different colours.
            if (_lanes[lane] is null || lane > MaxLanes)
            {
                continue;
            }

            // The commit's own lane stops at the dot; the rest pass straight through.
            double endY = lane == myLane ? rowHeight / 2 : rowHeight;
            _lines.Add(new GraphLine(lane, X(lane), 0, X(lane), endY));
        }

        IReadOnlyList<ObjectId>? parents = revision.ParentIds;
        ObjectId? firstParent = parents is { Count: > 0 } ? parents[0] : null;

        // The commit's lane carries on downwards waiting for its first parent.
        _lanes[myLane] = firstParent;
        if (firstParent is not null)
        {
            _lines.Add(new GraphLine(myLane, X(myLane), rowHeight / 2, X(myLane), rowHeight));
        }

        // A merge sends an extra edge out to each additional parent.
        if (parents is not null)
        {
            for (int i = 1; i < parents.Count; i++)
            {
                int parentLane = TakeLane(parents[i]);
                _lines.Add(new GraphLine(parentLane, X(myLane), rowHeight / 2, X(parentLane), rowHeight));
            }
        }

        TrimTrailingFreeLanes();

        return new GraphRow([.. _lines], myLane, X(myLane), rowHeight);
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

    private static double X(int lane) => (Math.Min(lane, MaxLanes) * LaneWidth) + (LaneWidth / 2);

    internal static Brush BrushFor(int lane) => LaneBrushes[lane % LaneBrushes.Count];
}
