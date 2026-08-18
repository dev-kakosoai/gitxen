using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI.Models;

/// <summary>A multi-step git operation that has stopped part-way and is waiting on you.</summary>
public enum RepositoryOperationKind
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert,
    Bisect
}

/// <summary>
///  The paused operation a repository is currently in, if any.
/// </summary>
/// <remarks>
///  Detected from the marker files git leaves in the git directory, which is the same thing git's own
///  prompt scripts read. Surfacing this matters because the state is otherwise invisible: a rebase
///  that stopped on a conflict looks, from the history and the file list alone, much like an ordinary
///  dirty working directory.
/// </remarks>
/// <param name="Step">Current step of a rebase, 0 when not applicable.</param>
/// <param name="TotalSteps">Total steps of a rebase, 0 when not applicable.</param>
/// <param name="ConflictCount">Files with unresolved conflicts.</param>
public sealed record RepositoryOperation(
    RepositoryOperationKind Kind,
    int Step,
    int TotalSteps,
    int ConflictCount)
{
    public static RepositoryOperation None { get; } = new(RepositoryOperationKind.None, 0, 0, 0);

    public bool IsInProgress => Kind != RepositoryOperationKind.None;

    public Visibility BannerVisibility => IsInProgress ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Skip only means anything during a rebase; the others have no notion of it.</summary>
    public Visibility SkipVisibility =>
        Kind == RepositoryOperationKind.Rebase ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Bisect is driven by marking commits good or bad, not by continue/abort.</summary>
    public Visibility ContinueVisibility =>
        IsInProgress && Kind != RepositoryOperationKind.Bisect ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The git subcommand these actions belong to, e.g. "cherry-pick".</summary>
    public string CommandName => Kind switch
    {
        RepositoryOperationKind.Merge => "merge",
        RepositoryOperationKind.Rebase => "rebase",
        RepositoryOperationKind.CherryPick => "cherry-pick",
        RepositoryOperationKind.Revert => "revert",
        RepositoryOperationKind.Bisect => "bisect",
        _ => ""
    };

    public string Title => Kind switch
    {
        RepositoryOperationKind.Merge => "Merge in progress",
        RepositoryOperationKind.Rebase => TotalSteps > 0
            ? $"Rebase in progress — step {Step} of {TotalSteps}"
            : "Rebase in progress",
        RepositoryOperationKind.CherryPick => "Cherry-pick in progress",
        RepositoryOperationKind.Revert => "Revert in progress",
        RepositoryOperationKind.Bisect => "Bisect in progress",
        _ => ""
    };

    /// <summary>
    ///  Says what to do next. Conflicts are named explicitly because resolving them is the only way
    ///  forward, and "continue" fails until they are staged.
    /// </summary>
    public string Message => Kind switch
    {
        RepositoryOperationKind.Bisect =>
            "Mark the checked-out commit good or bad from the History page, or reset to finish.",
        _ => ConflictCount > 0
            ? $"{ConflictCount} file(s) still have conflicts. Resolve them and stage each one, then continue."
            : "Nothing is conflicted. Continue to carry on, or abort to return to where you started."
    };
}
