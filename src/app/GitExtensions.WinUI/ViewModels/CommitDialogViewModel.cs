using System.Collections.ObjectModel;

namespace GitExtensions.WinUI.ViewModels;

/// <summary>
///  Backs the commit dialog: the staged/unstaged split, and whatever went wrong last time.
/// </summary>
public sealed class CommitDialogViewModel : ObservableObject
{
    private string _error = "";

    public ObservableCollection<ChangedFileViewModel> Staged { get; } = [];

    public ObservableCollection<ChangedFileViewModel> Unstaged { get; } = [];

    public string Summary => Staged.Count == 0
        ? "Nothing staged — stage files below, or nothing will be committed."
        : $"{Staged.Count} staged, {Unstaged.Count} not staged.";

    public string Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    public void Load(IReadOnlyList<ChangedFileViewModel> files)
    {
        Staged.Clear();
        Unstaged.Clear();

        foreach (ChangedFileViewModel file in files)
        {
            if (file.IsStaged)
            {
                Staged.Add(file);
            }
            else
            {
                Unstaged.Add(file);
            }
        }

        OnPropertyChanged(nameof(Summary));
    }
}
