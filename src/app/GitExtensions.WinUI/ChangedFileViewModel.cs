using GitExtensions.Extensibility.Git;
using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI;

public sealed class ChangedFileViewModel
{
    public ChangedFileViewModel(GitItemStatus status, bool isWorkingDirectory = false)
    {
        Name = status.Name;
        OldName = status.OldName;
        Change = GetChangeGlyph(status);
        IsStaged = status.Staged == StagedStatus.Index;
        IsWorkingDirectory = isWorkingDirectory;

        Detail = status.IsRenamed && !string.IsNullOrEmpty(status.OldName)
            ? $"renamed from {status.OldName}"
            : isWorkingDirectory ? (IsStaged ? "staged" : "not staged") : "";
    }

    public string Name { get; }

    /// <summary>Previous path for a rename; needed so git can diff the right pair.</summary>
    public string? OldName { get; }

    /// <summary>Single-character status marker, matching git's own porcelain vocabulary.</summary>
    public string Change { get; }

    public string Detail { get; }

    public bool IsStaged { get; }

    public bool IsWorkingDirectory { get; }

    /// <summary>Stage/unstage buttons only make sense for uncommitted changes.</summary>
    public Visibility StagingVisibility => IsWorkingDirectory ? Visibility.Visible : Visibility.Collapsed;

    private static string GetChangeGlyph(GitItemStatus status)
    {
        if (status.IsNew)
        {
            return "A";
        }

        if (status.IsDeleted)
        {
            return "D";
        }

        return status.IsRenamed ? "R" : "M";
    }
}
