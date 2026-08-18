using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace GitExtensions.WinUI;

/// <summary>How a file changed, which drives its marker letter and colour.</summary>
public enum FileChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed
}

public sealed class ChangedFileViewModel
{
    public ChangedFileViewModel(GitItemStatus status, bool isWorkingDirectory = false)
    {
        Name = status.Name;
        OldName = status.OldName;
        Kind = GetKind(status);
        IsNew = status.IsNew;
        IsStaged = status.Staged == StagedStatus.Index;
        IsWorkingDirectory = isWorkingDirectory;

        // Split for the two-line row: the leaf is what identifies the file, the folder is context.
        int separator = Name.LastIndexOfAny(['/', '\\']);
        FileName = separator >= 0 ? Name[(separator + 1)..] : Name;
        Directory = separator >= 0 ? Name[..separator] : "";

        Detail = status.IsRenamed && !string.IsNullOrEmpty(status.OldName)
            ? $"renamed from {status.OldName}"
            : Directory;
    }

    public string Name { get; }

    /// <summary>Leaf file name, shown as the row's primary text.</summary>
    public string FileName { get; }

    /// <summary>Containing folder, shown beneath the file name.</summary>
    public string Directory { get; }

    /// <summary>Previous path for a rename; needed so git can diff the right pair.</summary>
    public string? OldName { get; }

    public FileChangeKind Kind { get; }

    /// <summary>Untracked, which changes how the file is discarded — removed rather than reverted.</summary>
    public bool IsNew { get; }

    /// <summary>Single-character status marker, matching git's own porcelain vocabulary.</summary>
    public string Change => Kind switch
    {
        FileChangeKind.Added => "A",
        FileChangeKind.Deleted => "D",
        FileChangeKind.Renamed => "R",
        _ => "M"
    };

    public Brush ChangeBrush => Kind switch
    {
        FileChangeKind.Added => ThemeBrushes.FileAdded,
        FileChangeKind.Deleted => ThemeBrushes.FileDeleted,
        FileChangeKind.Renamed => ThemeBrushes.FileRenamed,
        _ => ThemeBrushes.FileModified
    };

    public string Detail { get; }

    /// <summary>Collapses the second row line when there is nothing to say in it.</summary>
    public Visibility DetailVisibility => Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsStaged { get; }

    public bool IsWorkingDirectory { get; }

    /// <summary>Stage/unstage buttons only make sense for uncommitted changes.</summary>
    public Visibility StagingVisibility => IsWorkingDirectory ? Visibility.Visible : Visibility.Collapsed;

    private static FileChangeKind GetKind(GitItemStatus status)
    {
        if (status.IsNew)
        {
            return FileChangeKind.Added;
        }

        if (status.IsDeleted)
        {
            return FileChangeKind.Deleted;
        }

        return status.IsRenamed ? FileChangeKind.Renamed : FileChangeKind.Modified;
    }
}
