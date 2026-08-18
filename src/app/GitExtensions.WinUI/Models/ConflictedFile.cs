using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI.Models;

/// <summary>
///  The shape of a conflict, which decides what resolving it can even mean.
/// </summary>
/// <remarks>
///  Not every conflict is two sets of edits to one file. When one side deleted a file and the other
///  changed it there is no content to merge — the choice is whether the file should exist at all —
///  and offering a merge tool for that case only wastes the user's time.
/// </remarks>
public enum ConflictKind
{
    /// <summary>Both sides changed the file. The usual case, and the only one a merge tool helps with.</summary>
    BothModified,

    /// <summary>Both sides added a file with the same name and different contents.</summary>
    BothAdded,

    /// <summary>We deleted it, they changed it.</summary>
    DeletedByUs,

    /// <summary>They deleted it, we changed it.</summary>
    DeletedByThem,

    /// <summary>We added it, they did not have it.</summary>
    AddedByUs,

    /// <summary>They added it, we did not have it.</summary>
    AddedByThem,

    /// <summary>Both sides deleted it, but something else differs.</summary>
    BothDeleted
}

/// <param name="Path">Repository-relative path.</param>
public sealed record ConflictedFile(string Path, ConflictKind Kind)
{
    /// <summary>Leaf name, which is what identifies the row.</summary>
    public string FileName
    {
        get
        {
            // git always reports repository-relative paths with forward slashes.
            int separator = Path.LastIndexOf('/');
            return separator >= 0 ? Path[(separator + 1)..] : Path;
        }
    }

    public string Directory
    {
        get
        {
            // git always reports repository-relative paths with forward slashes.
            int separator = Path.LastIndexOf('/');
            return separator >= 0 ? Path[..separator] : "";
        }
    }

    /// <summary>Plain English, because "UU" and "DU" mean nothing to most people.</summary>
    public string Description => Kind switch
    {
        ConflictKind.BothModified => "Both sides changed this file",
        ConflictKind.BothAdded => "Both sides added a file with this name",
        ConflictKind.DeletedByUs => "You deleted it; they changed it",
        ConflictKind.DeletedByThem => "They deleted it; you changed it",
        ConflictKind.AddedByUs => "You added it; they did not have it",
        ConflictKind.AddedByThem => "They added it; you did not have it",
        _ => "Both sides deleted it"
    };

    /// <summary>
    ///  True when the file has content from both sides to merge. A delete/modify conflict has no
    ///  common content, so the merge tool is hidden rather than offered and left to fail.
    /// </summary>
    public bool IsContentConflict => Kind is ConflictKind.BothModified or ConflictKind.BothAdded;

    public Visibility MergeToolVisibility =>
        IsContentConflict ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What "ours" and "theirs" mean for this conflict, spelled out on the buttons.</summary>
    public string OursLabel => Kind switch
    {
        ConflictKind.DeletedByUs => "Keep it deleted",
        ConflictKind.AddedByThem => "Do not add it",
        _ => "Keep ours"
    };

    public string TheirsLabel => Kind switch
    {
        ConflictKind.DeletedByThem => "Keep it deleted",
        ConflictKind.AddedByUs => "Do not add it",
        _ => "Keep theirs"
    };
}
