namespace GitExtensions.WinUI.Models;

/// <summary>How much of the history to read.</summary>
public enum RevisionScope
{
    /// <summary>Only what is reachable from HEAD — the current branch and its ancestry.</summary>
    CurrentBranch,

    /// <summary>Every ref, so other branches and remote-tracking branches appear in the graph.</summary>
    AllBranches
}

/// <summary>
///  What to ask <c>git log</c> for.
/// </summary>
/// <remarks>
///  <para>
///   These are filters git applies while walking history, not a filter over rows already on screen.
///   That distinction matters: the front-end only holds a page of commits at a time, so filtering
///   client-side can only ever search the page you happen to have loaded.
///  </para>
///  <para>
///   A record so a query can be compared for equality — the tab reloads only when the query actually
///   changes, rather than on every keystroke that leaves it the same.
///  </para>
/// </remarks>
/// <param name="Scope">Current branch or every ref.</param>
/// <param name="MessageContains">Matched against commit messages (<c>--grep</c>, case-insensitive).</param>
/// <param name="Author">Matched against the author (<c>--author</c>).</param>
/// <param name="ContainingText">Content search: commits that changed occurrences of this text (<c>-S</c>).</param>
/// <param name="Path">Restricts history to one file or directory.</param>
public sealed record RevisionQuery(
    RevisionScope Scope = RevisionScope.CurrentBranch,
    string MessageContains = "",
    string Author = "",
    string ContainingText = "",
    string Path = "")
{
    /// <summary>Nothing narrowed — the plain current-branch log.</summary>
    public static RevisionQuery Default { get; } = new();

    /// <summary>True when git is doing the filtering, which is worth saying on screen.</summary>
    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(MessageContains)
        || !string.IsNullOrWhiteSpace(Author)
        || !string.IsNullOrWhiteSpace(ContainingText)
        || !string.IsNullOrWhiteSpace(Path);
}
