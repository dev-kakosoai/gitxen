using System.Text;

namespace GitExtensions.WinUI.Diff;

/// <summary>
///  One hunk of a unified diff, together with everything needed to apply it on its own.
/// </summary>
/// <param name="Index">Position in the file's diff, used to match a hunk to the row that heads it.</param>
/// <param name="Header">The <c>@@ … @@</c> line, shown on the row.</param>
/// <param name="Patch">
///  A complete, self-contained patch: the file's header lines followed by this hunk alone. This is
///  what gets handed to <c>git apply</c>.
/// </param>
public sealed record DiffHunk(int Index, string Header, string Patch)
{
    /// <summary>The counts, e.g. "+4 −1", for the row that offers to stage it.</summary>
    public string Summary { get; init; } = "";
}

/// <summary>
///  Splits a file's unified diff into individually applicable patches.
/// </summary>
/// <remarks>
///  <para>
///   This is what makes partial staging possible: git has no "stage this hunk" command, so the way it
///   is done is to construct a patch containing only the hunk in question and feed that to
///   <c>git apply --cached</c>.
///  </para>
///  <para>
///   Deliberately works on the raw diff text rather than on the parsed view models. The parsed lines
///   are truncated at the display cap and carry no trailing-newline information; a patch rebuilt from
///   them would be subtly wrong, and a wrong patch corrupts the index rather than merely looking odd.
///  </para>
/// </remarks>
public static class HunkSplitter
{
    /// <summary>
    ///  Splits <paramref name="diff"/> into hunks. Returns an empty list when the text is not a
    ///  unified diff with a header — a binary file, or a message standing in for one.
    /// </summary>
    public static IReadOnlyList<DiffHunk> Split(string diff)
    {
        if (string.IsNullOrEmpty(diff))
        {
            return [];
        }

        string[] lines = diff.Split('\n');
        int firstHunk = Array.FindIndex(lines, IsHunkHeader);

        if (firstHunk <= 0)
        {
            // No hunks at all, or a diff that starts with one and so has no file header to reuse.
            return [];
        }

        // Everything before the first @@ identifies the file and applies to every hunk.
        string preamble = string.Join('\n', lines[..firstHunk]).TrimEnd('\n');

        List<DiffHunk> hunks = [];
        int start = firstHunk;

        for (int i = firstHunk + 1; i <= lines.Length; i++)
        {
            bool atEnd = i == lines.Length;

            if (!atEnd && !IsHunkHeader(lines[i]))
            {
                continue;
            }

            hunks.Add(Build(hunks.Count, preamble, lines[start..i]));
            start = i;
        }

        return hunks;
    }

    private static DiffHunk Build(int index, string preamble, string[] hunkLines)
    {
        StringBuilder patch = new();
        patch.Append(preamble).Append('\n');

        int added = 0;
        int removed = 0;

        foreach (string raw in hunkLines)
        {
            string line = raw.TrimEnd('\r');

            // A trailing empty element from the final newline is not part of the hunk.
            if (line.Length == 0 && ReferenceEquals(raw, hunkLines[^1]))
            {
                continue;
            }

            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                added++;
            }
            else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                removed++;
            }

            patch.Append(line).Append('\n');
        }

        return new DiffHunk(index, hunkLines[0].TrimEnd('\r'), patch.ToString())
        {
            Summary = $"+{added} −{removed}"
        };
    }

    private static bool IsHunkHeader(string line) => line.StartsWith("@@", StringComparison.Ordinal);
}
