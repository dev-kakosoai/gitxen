namespace GitExtensions.WinUI.Models;

/// <summary>
///  One entry in the command palette.
/// </summary>
/// <remarks>
///  The palette is built fresh each time it opens, from whatever the repository currently offers —
///  the sections, the operations, and one entry per branch. That is why the action is a delegate
///  rather than an identifier: an entry closes over the branch or page it refers to, so there is no
///  command registry to keep in step with the UI.
/// </remarks>
/// <remarks>
///  A class with settable properties rather than a positional record: the XAML type-info generator
///  emits property setters for every type used as an <c>x:DataType</c>, and cannot assign a record's
///  init-only properties.
/// </remarks>
public sealed class PaletteCommand
{
    public PaletteCommand(string title, string category, string detail, Func<Task> invoke)
    {
        Title = title;
        Category = category;
        Detail = detail;
        Invoke = invoke;
    }

    /// <summary>What the user reads and types against.</summary>
    public string Title { get; set; }

    /// <summary>Grouping shown beside the title: "Go to", "Branch", "Repository".</summary>
    public string Category { get; set; }

    /// <summary>Optional extra context, such as what a command will keep or discard.</summary>
    public string Detail { get; set; }

    /// <summary>Runs the command.</summary>
    public Func<Task> Invoke { get; set; }

    /// <summary>
    ///  Subsequence match, the behaviour every command palette has: "chb" finds "Checkout branch".
    /// </summary>
    /// <remarks>
    ///  Matching the category as well as the title means typing "branch" surfaces every branch entry,
    ///  which is the common case for a palette in a git client.
    /// </remarks>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return IsSubsequence(query, Title) || IsSubsequence(query, $"{Category} {Title}");
    }

    /// <summary>Exact-prefix matches sort first, then title matches, then everything else.</summary>
    public int RankFor(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 1;
        }

        if (Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

    private static bool IsSubsequence(string query, string candidate)
    {
        // Lowered once rather than comparing case-insensitively per character: there is no
        // string.IndexOf(char, int, StringComparison) overload, and this keeps the loop simple.
        string haystack = candidate.ToLowerInvariant();
        int position = 0;

        foreach (char character in query.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            int found = haystack.IndexOf(character, position);

            if (found < 0)
            {
                return false;
            }

            position = found + 1;
        }

        return true;
    }
}
