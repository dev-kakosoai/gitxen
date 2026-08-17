using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GitExtensions.WinUI.Models;

/// <summary>
///  Structured views of the repository objects the navigation pages list.
/// </summary>
/// <remarks>
///  These exist because the pages need to bind to fields — a branch's upstream, a worktree's
///  branch — rather than render the raw text <c>git branch -v</c> and friends emit. Each is parsed
///  from a machine-readable git format (<c>for-each-ref</c>, <c>--porcelain</c>) rather than from
///  git's human-facing output, which is explicitly not a stable interface.
/// </remarks>
public sealed record BranchInfo(
    string Name,
    string Upstream,
    int Ahead,
    int Behind,
    string ShortHash,
    string Date,
    string Subject,
    bool IsCurrent)
{
    /// <summary>Empty when there is no upstream, so the column can collapse rather than show "none".</summary>
    public bool HasUpstream => !string.IsNullOrEmpty(Upstream);

    public bool IsAhead => Ahead > 0;

    public bool IsBehind => Behind > 0;
}

public sealed record RemoteInfo(string Name, string FetchUrl, string PushUrl)
{
    /// <summary>Most remotes fetch and push to the same place; only say so twice when they differ.</summary>
    public bool HasSeparatePushUrl =>
        !string.IsNullOrEmpty(PushUrl) && !string.Equals(PushUrl, FetchUrl, StringComparison.Ordinal);
}

public sealed record TagInfo(string Name, string ShortHash, string Date, string Subject, bool IsAnnotated)
{
    /// <summary>Annotated tags carry their own message and author; lightweight ones are just a pointer.</summary>
    public string Kind => IsAnnotated ? "annotated" : "lightweight";
}

public sealed record StashInfo(string Reference, string Subject, string ShortHash);

public sealed record SubmoduleInfo(string Path, string ShortHash, string Described, SubmoduleState State)
{
    /// <summary>Plain-English rendering of the marker, since '+' and '-' mean nothing on their own.</summary>
    public string StateText => State switch
    {
        SubmoduleState.Modified => "different commit checked out",
        SubmoduleState.NotInitialized => "not initialised",
        SubmoduleState.Conflicted => "merge conflicts",
        _ => "up to date"
    };

    public bool NeedsAttention => State != SubmoduleState.UpToDate;
}

/// <summary>
///  The leading marker <c>git submodule status</c> puts in front of each entry.
/// </summary>
public enum SubmoduleState
{
    /// <summary>Checked out at the recorded commit.</summary>
    UpToDate,

    /// <summary>Checked out at a different commit than the superproject records ('+').</summary>
    Modified,

    /// <summary>Not initialised ('-').</summary>
    NotInitialized,

    /// <summary>Merge conflicts ('U').</summary>
    Conflicted
}

public sealed record WorktreeInfo(string Path, string ShortHash, string Branch, bool IsBare, bool IsLocked, bool IsMain)
{
    /// <summary>A detached worktree has no branch, so the row says so rather than showing a blank.</summary>
    public string BranchOrDetached => string.IsNullOrEmpty(Branch) ? "(detached)" : Branch;

    /// <summary>The main worktree cannot be removed, so its row offers no remove action.</summary>
    public bool CanRemove => !IsMain;
}

/// <summary>Which kind of ref a history-row badge represents; drives its colour.</summary>
public enum RefBadgeKind
{
    LocalBranch,
    Remote,
    Tag
}

/// <summary>
///  A branch or tag chip drawn on the history row for the commit it points at.
/// </summary>
/// <remarks>
///  Carries its own brushes rather than leaving the template to switch on <see cref="Kind"/>: x:Bind
///  cannot select a resource by enum value without a converter, and the colours are fixed semantics
///  that read correctly against both the light and dark surfaces. The same approach the diff and
///  changed-file view models already take.
/// </remarks>
/// <param name="IsHead">The currently checked-out branch, which is drawn emphasised.</param>
public sealed record RefBadge(string Name, RefBadgeKind Kind, bool IsHead)
{
    private static readonly SolidColorBrush _localBrush = new(Color.FromArgb(56, 88, 166, 255));
    private static readonly SolidColorBrush _localText = new(Color.FromArgb(255, 88, 166, 255));
    private static readonly SolidColorBrush _remoteBrush = new(Color.FromArgb(56, 188, 140, 255));
    private static readonly SolidColorBrush _remoteText = new(Color.FromArgb(255, 188, 140, 255));
    private static readonly SolidColorBrush _tagBrush = new(Color.FromArgb(56, 210, 168, 65));
    private static readonly SolidColorBrush _tagText = new(Color.FromArgb(255, 210, 168, 65));
    private static readonly SolidColorBrush _headBrush = new(Color.FromArgb(72, 63, 185, 80));
    private static readonly SolidColorBrush _headText = new(Color.FromArgb(255, 63, 185, 80));

    /// <summary>HEAD first, then local branches, then remotes, then tags.</summary>
    public int SortKey => IsHead ? 0 : Kind switch
    {
        RefBadgeKind.LocalBranch => 1,
        RefBadgeKind.Remote => 2,
        _ => 3
    };

    public Brush Background => IsHead ? _headBrush : Kind switch
    {
        RefBadgeKind.LocalBranch => _localBrush,
        RefBadgeKind.Remote => _remoteBrush,
        _ => _tagBrush
    };

    public Brush Foreground => IsHead ? _headText : Kind switch
    {
        RefBadgeKind.LocalBranch => _localText,
        RefBadgeKind.Remote => _remoteText,
        _ => _tagText
    };

    /// <summary>
    ///  Tags are prefixed rather than relying on colour alone, since a tag and a branch can share a
    ///  name and the two hues would otherwise be the only thing telling them apart.
    /// </summary>
    public string Label => Kind == RefBadgeKind.Tag ? $"tag: {Name}" : Name;
}
