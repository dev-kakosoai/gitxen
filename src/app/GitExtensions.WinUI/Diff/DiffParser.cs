using System.Globalization;
using System.Text.RegularExpressions;

namespace GitExtensions.WinUI.Diff;

/// <summary>One row of a side-by-side view; either side may be absent.</summary>
public sealed record SideBySideRow(DiffLineViewModel? Left, DiffLineViewModel? Right);

/// <summary>
///  Turns raw unified-diff text into view models, either as-is or paired up for side-by-side.
/// </summary>
public static partial class DiffParser
{
    /// <summary>Unified diff, with old/new line numbers tracked from the hunk headers.</summary>
    public static IReadOnlyList<DiffLineViewModel> ParseUnified(string diff, string fileName, int maxLines, out int omitted)
    {
        List<DiffLineViewModel> result = [];
        int oldNumber = 0;
        int newNumber = 0;
        omitted = 0;

        string[] lines = diff.Split('\n');

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');

            if (result.Count >= maxLines)
            {
                omitted = lines.Length - result.Count;
                break;
            }

            if (TryParseHunkHeader(line, out int oldStart, out int newStart))
            {
                oldNumber = oldStart;
                newNumber = newStart;
                result.Add(DiffLineViewModel.Create(line, fileName));
                continue;
            }

            // Numbering only advances on the side a line actually belongs to.
            (int old, int @new) = line.StartsWith('+') ? (0, newNumber++)
                : line.StartsWith('-') ? (oldNumber++, 0)
                : line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal)
                  || line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)
                    ? (0, 0)
                    : (oldNumber++, newNumber++);

            result.Add(DiffLineViewModel.Create(line, fileName, old, @new));
        }

        return result;
    }

    /// <summary>
    ///  Pairs each run of removals with the additions that follow it, so a modified line shows its
    ///  before and after on the same row.
    /// </summary>
    public static IReadOnlyList<SideBySideRow> ToSideBySide(IReadOnlyList<DiffLineViewModel> unified)
    {
        List<SideBySideRow> rows = [];
        List<DiffLineViewModel> removed = [];
        List<DiffLineViewModel> added = [];

        void Flush()
        {
            for (int i = 0; i < Math.Max(removed.Count, added.Count); i++)
            {
                rows.Add(new SideBySideRow(
                    i < removed.Count ? removed[i] : null,
                    i < added.Count ? added[i] : null));
            }

            removed.Clear();
            added.Clear();
        }

        foreach (DiffLineViewModel line in unified)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Removed:
                    removed.Add(line);
                    break;

                case DiffLineKind.Added:
                    added.Add(line);
                    break;

                default:
                    Flush();

                    // Headers and hunks span the full width; context appears on both sides.
                    rows.Add(line.Kind == DiffLineKind.Context
                        ? new SideBySideRow(line, line)
                        : new SideBySideRow(line, null));
                    break;
            }
        }

        Flush();
        return rows;
    }

    private static bool TryParseHunkHeader(string line, out int oldStart, out int newStart)
    {
        oldStart = 0;
        newStart = 0;

        Match match = HunkHeader().Match(line);
        if (!match.Success)
        {
            return false;
        }

        oldStart = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        newStart = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return true;
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}
