using System.Globalization;
using GitExtensions.WinUI.Graph;
using GitExtensions.WinUI.Models;
using GitUIPluginInterfaces;
using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI;

public sealed class CommitRowViewModel
{
    private CommitRowViewModel(string subject, int pendingCount)
    {
        Revision = null;
        IsWorkingDirectory = true;
        ShortHash = "";
        FullHash = "";
        Author = "";
        AuthorEmail = "";
        Subject = subject;
        Body = $"{pendingCount} uncommitted change(s) in the working directory.";
        Date = "";
        RelativeDate = "now";
        PendingCount = pendingCount;
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
        Date = revision.AuthorDate.ToString("f", CultureInfo.CurrentCulture);
        RelativeDate = FormatRelative(revision.AuthorDate);
        IsMerge = revision.ParentIds?.Count > 1;
    }

    /// <summary>The underlying revision; null for the synthetic working-directory row.</summary>
    public GitRevision? Revision { get; }

    /// <summary>Pre-built lane drawing for this row; empty for the working-directory row.</summary>
    public IReadOnlyList<GraphSegment> GraphSegments { get; init; } = [];

    /// <summary>
    ///  Branch/tag chips pointing at this commit. Assigned as rows stream in, from the single
    ///  ref lookup the tab does per reload.
    /// </summary>
    public IReadOnlyList<RefBadge> Refs { get; init; } = [];

    public Visibility RefsVisibility => Refs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Fixed so the lane geometry lines up from row to row.</summary>
    public double RowHeight => CommitGraphBuilder.RowHeight;

    public double GraphColumnWidth => CommitGraphBuilder.ColumnWidth;

    /// <summary>
    ///  True for the pseudo-row at the top of the list representing uncommitted changes.
    /// </summary>
    public bool IsWorkingDirectory { get; }

    /// <summary>Number of uncommitted changes; only meaningful on the working-directory row.</summary>
    public int PendingCount { get; }

    /// <summary>A merge commit, drawn with a distinguishing glyph.</summary>
    public bool IsMerge { get; }

    public string ShortHash { get; }

    public string FullHash { get; }

    public string Author { get; }

    public string AuthorEmail { get; }

    public string Subject { get; }

    public string Body { get; }

    /// <summary>Full, culture-formatted timestamp — used in tooltips and the details pane.</summary>
    public string Date { get; }

    /// <summary>Short human-scale age ("3 days ago"), which is what the list column shows.</summary>
    public string RelativeDate { get; }

    /// <summary>Up to two initials for the author avatar, so the row needs no network fetch.</summary>
    public string AuthorInitials => GetInitials(Author);

    /// <summary>The commit-list rows show either an avatar or the pending-changes glyph, never both.</summary>
    public Visibility AvatarVisibility => IsWorkingDirectory ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PendingGlyphVisibility => IsWorkingDirectory ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MergeGlyphVisibility => IsMerge ? Visibility.Visible : Visibility.Collapsed;

    public static CommitRowViewModel CreateWorkingDirectory(int pendingCount) =>
        new($"{pendingCount} uncommitted change{(pendingCount == 1 ? "" : "s")}", pendingCount);

    /// <summary>Case-insensitive match against the fields shown in the list.</summary>
    public bool Matches(string filter) =>
        Subject.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Author.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || ShortHash.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///  Coarse relative age. Deliberately not localised: the rest of this front-end is English-only,
    ///  and the precise timestamp is always one tooltip away.
    /// </summary>
    private static string FormatRelative(DateTime date)
    {
        TimeSpan age = DateTime.Now - date;

        if (age < TimeSpan.Zero)
        {
            // Commits dated in the future happen — a rebase across a clock change, or a bad committer date.
            return date.ToString("d", CultureInfo.CurrentCulture);
        }

        if (age.TotalMinutes < 1)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return Plural((int)age.TotalMinutes, "minute");
        }

        if (age.TotalDays < 1)
        {
            return Plural((int)age.TotalHours, "hour");
        }

        if (age.TotalDays < 31)
        {
            return Plural((int)age.TotalDays, "day");
        }

        if (age.TotalDays < 365)
        {
            return Plural((int)(age.TotalDays / 30), "month");
        }

        return Plural((int)(age.TotalDays / 365), "year");

        static string Plural(int count, string unit) => $"{count} {unit}{(count == 1 ? "" : "s")} ago";
    }

    private static string GetInitials(string author)
    {
        string[] parts = author.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "?";
        }

        return parts.Length == 1
            ? parts[0][..1].ToUpperInvariant()
            : $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }
}
