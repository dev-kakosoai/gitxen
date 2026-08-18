namespace GitExtensions.WinUI.Models;

/// <summary>
///  One row in the go-to palette: a repository object, section or file the user can jump to.
/// </summary>
/// <remarks>
///  <para>
///   Split from <see cref="PaletteCommand"/> rather than grown out of it: the command palette answers
///   "do something", this answers "take me to something", and the two rank differently — a command
///   matches on what it does, an object on what it is called. Sharing a class would mean sharing a
///   matcher that suits neither.
///  </para>
///  <para>
///   A class with settable properties rather than a record, for the same XAML type-info generator
///   reason as <see cref="PaletteCommand"/>.
///  </para>
/// </remarks>
public sealed class GoToItem
{
    public GoToItem(string glyph, string title, string category, string detail, Func<Task> invoke)
    {
        Glyph = glyph;
        Title = title;
        Category = category;
        Detail = detail;
        Invoke = invoke;
    }

    /// <summary>Segoe Fluent glyph identifying the kind of object, the way ReSharper draws type icons.</summary>
    public string Glyph { get; set; }

    /// <summary>The object's name; what the query is matched against.</summary>
    public string Title { get; set; }

    /// <summary>The kind, shown dimmed: "Branch", "Tag", "Commit", "File", "Section".</summary>
    public string Category { get; set; }

    /// <summary>Disambiguation on the right: a hash, an author, a folder.</summary>
    public string Detail { get; set; }

    /// <summary>Performs the jump.</summary>
    public Func<Task> Invoke { get; set; }

    /// <summary>Kinds rank against each other when scores tie: a branch beats a file of the same name.</summary>
    public int CategoryRank { get; set; }

    /// <summary>
    ///  Scores <paramref name="query"/> against the title. Lower is better; null is no match.
    /// </summary>
    /// <remarks>
    ///  The ladder is ReSharper's: exact prefix, then initials ("camel humps" — for git names the
    ///  humps sit after separators rather than at capitals, so "fw" finds "feat/winui3-frontend"),
    ///  then substring, then subsequence as the everything-else net. The substring tier also looks at
    ///  the detail, so a short hash pasted into the box finds its commit.
    /// </remarks>
    public int? ScoreFor(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 3;
        }

        query = query.Trim();

        if (Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (MatchesInitials(query, Title))
        {
            return 1;
        }

        if (Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Detail.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return IsSubsequence(query, Title) ? 3 : null;
    }

    /// <summary>
    ///  Whether the query matches the leading characters of the name's segments, in order.
    /// </summary>
    /// <remarks>
    ///  Segments begin at the start, after a separator, and at a capital following a lowercase, so
    ///  both "feature/add-login" and "AddLoginCommand" expose the same initials.
    /// </remarks>
    private static bool MatchesInitials(string query, string candidate)
    {
        int q = 0;

        for (int i = 0; i < candidate.Length && q < query.Length; i++)
        {
            char c = candidate[i];

            bool startsSegment = i == 0
                || IsSeparator(candidate[i - 1])
                || (char.IsUpper(c) && char.IsLower(candidate[i - 1]));

            if (startsSegment && !IsSeparator(c)
                && char.ToLowerInvariant(c) == char.ToLowerInvariant(query[q]))
            {
                q++;
            }
        }

        return q == query.Length;

        static bool IsSeparator(char c) => c is '/' or '-' or '_' or '.' or ' ' or '\\';
    }

    private static bool IsSubsequence(string query, string candidate)
    {
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
