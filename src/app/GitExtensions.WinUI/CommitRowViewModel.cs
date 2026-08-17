using GitExtensions.WinUI.Graph;
using GitUIPluginInterfaces;

namespace GitExtensions.WinUI;

public sealed class CommitRowViewModel
{
    private CommitRowViewModel(string subject, int pendingCount)
    {
        Revision = null;
        IsWorkingDirectory = true;
        ShortHash = "•";
        FullHash = "";
        Author = "";
        AuthorEmail = "";
        Subject = subject;
        Body = $"{pendingCount} uncommitted change(s) in the working directory.";
        Date = "";
    }

    public CommitRowViewModel(GitRevision revision)
    {
        Revision = revision;
        ShortHash = revision.ObjectId.ToShortString();
        FullHash = revision.ObjectId.ToString();
        Author = revision.Author ?? "";
        AuthorEmail = revision.AuthorEmail ?? "";
        Subject = revision.Subject;
        Body = string.IsNullOrWhiteSpace(revision.Body) ? revision.Subject : revision.Body;
        Date = revision.AuthorDate.ToString("g");
    }

    /// <summary>The underlying revision; null for the synthetic working-directory row.</summary>
    public GitRevision? Revision { get; }

    /// <summary>Pre-built lane drawing for this row; empty for the working-directory row.</summary>
    public IReadOnlyList<GraphSegment> GraphSegments { get; init; } = [];

    /// <summary>Fixed so the lane geometry lines up from row to row.</summary>
    public double RowHeight => CommitGraphBuilder.RowHeight;

    public double GraphColumnWidth => CommitGraphBuilder.ColumnWidth;

    /// <summary>
    ///  True for the pseudo-row at the top of the list representing uncommitted changes.
    /// </summary>
    public bool IsWorkingDirectory { get; }

    public string ShortHash { get; }

    public string FullHash { get; }

    public string Author { get; }

    public string AuthorEmail { get; }

    public string Subject { get; }

    public string Body { get; }

    public string Date { get; }

    public static CommitRowViewModel CreateWorkingDirectory(int pendingCount) =>
        new($"Working directory — {pendingCount} uncommitted change(s)", pendingCount);

    /// <summary>Case-insensitive match against the fields shown in the list.</summary>
    public bool Matches(string filter) =>
        Subject.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Author.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || ShortHash.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
