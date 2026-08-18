namespace GitExtensions.WinUI.Models;

/// <summary>
///  The choices a push offers beyond "send the current branch somewhere".
/// </summary>
/// <param name="Branch">The local branch being pushed.</param>
/// <param name="Remote">Empty means the remote the branch tracks, or the first one configured.</param>
/// <param name="RemoteBranch">Empty means the same name as <paramref name="Branch"/>.</param>
/// <param name="ForceWithLease">
///  Overwrite the remote branch, but only if it has not moved since it was last fetched.
/// </param>
/// <param name="SetUpstream">Record the pushed branch as this branch's upstream.</param>
/// <param name="PushAllTags">Also push every tag.</param>
public sealed record PushOptions(
    string Branch,
    string Remote = "",
    string RemoteBranch = "",
    bool ForceWithLease = false,
    bool SetUpstream = false,
    bool PushAllTags = false);

/// <param name="Rebase">Replay local commits on top of the fetched ones instead of merging.</param>
/// <param name="Prune">Drop remote-tracking branches that no longer exist on the remote.</param>
/// <param name="FastForwardOnly">Refuse the pull rather than create a merge commit.</param>
public sealed record PullOptions(
    string Remote = "",
    string RemoteBranch = "",
    bool Rebase = false,
    bool Prune = false,
    bool FastForwardOnly = false);

/// <param name="NoFastForward">Always record a merge commit, even when a fast-forward is possible.</param>
/// <param name="Squash">Apply the changes as one uncommitted change set instead of merging.</param>
/// <param name="NoCommit">Merge but leave the result staged for review.</param>
public sealed record MergeOptions(
    bool NoFastForward = false,
    bool Squash = false,
    bool NoCommit = false,
    string Message = "");

/// <summary>
///  One entry of <c>git reflog</c> — where a ref used to point, and what moved it.
/// </summary>
/// <remarks>
///  The reflog is the recovery path after a reset or a rebase goes wrong: commits that no branch
///  points at any more are still listed here until they are garbage collected.
/// </remarks>
/// <param name="Selector">The reflog selector, e.g. <c>HEAD@{2}</c>, which git accepts as a revision.</param>
/// <param name="Action">What moved the ref — "commit", "rebase (finish)", "reset", and so on.</param>
public sealed record ReflogEntry(string Selector, string ShortHash, string Action, string Subject, string Date);

/// <summary>A remote-tracking branch, listed separately from the local ones.</summary>
/// <param name="Remote">The remote it belongs to, e.g. "origin".</param>
/// <param name="ShortName">The branch name without the remote prefix.</param>
public sealed record RemoteBranchInfo(string FullName, string Remote, string ShortName, string ShortHash, string Date, string Subject)
{
    /// <summary>A local branch created from this one would normally take the short name.</summary>
    public string SuggestedLocalName => ShortName;
}
