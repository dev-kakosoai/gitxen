using System.Collections.ObjectModel;
using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Diff;
using GitExtensions.WinUI.Services;
using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI.ViewModels;

/// <summary>
///  The Changes page: uncommitted work split into staged and unstaged, with the diff for whichever
///  file is selected and the commit message being composed.
/// </summary>
/// <remarks>
///  This replaces the modal commit dialog. Staging is an activity you move back and forth in while
///  reading diffs, which a dialog sized to fit a screenshot cannot support — so it gets a page, and
///  the message you are part-way through typing survives navigating away and back.
/// </remarks>
public sealed class WorkingDirectoryViewModel : ObservableObject
{
    private readonly RepositoryLoader _loader;

    /// <summary>Reloads the commit list after a commit, since history has a new tip.</summary>
    private readonly Func<Task> _onCommitted;

    private ChangedFileViewModel? _selectedUnstaged;
    private ChangedFileViewModel? _selectedStaged;
    private string _message = "";
    private bool _isAmend;
    private bool _isSignOff;
    private bool _isBusy;

    /// <summary>Guards the mutual clearing of the two list selections against re-entering itself.</summary>
    private bool _isSyncingSelection;

    internal WorkingDirectoryViewModel(RepositoryLoader loader, Func<Task> onCommitted)
    {
        _loader = loader;
        _onCommitted = onCommitted;
    }

    public ObservableCollection<ChangedFileViewModel> Unstaged { get; } = [];

    public ObservableCollection<ChangedFileViewModel> Staged { get; } = [];

    /// <summary>
    ///  The diff for the selected file, whichever list it came from. Hunk staging is enabled here and
    ///  nowhere else — this is the only diff that has an index to apply to.
    /// </summary>
    public DiffViewModel Diff { get; } = new() { SupportsHunkStaging = true };

    public ChangedFileViewModel? SelectedUnstaged
    {
        get => _selectedUnstaged;
        set
        {
            if (!SetProperty(ref _selectedUnstaged, value) || _isSyncingSelection)
            {
                return;
            }

            if (value is not null)
            {
                // One diff panel, so only one list can hold the selection at a time.
                _isSyncingSelection = true;
                SelectedStaged = null;
                _isSyncingSelection = false;
                _ = LoadDiffAsync(value);
            }
        }
    }

    public ChangedFileViewModel? SelectedStaged
    {
        get => _selectedStaged;
        set
        {
            if (!SetProperty(ref _selectedStaged, value) || _isSyncingSelection)
            {
                return;
            }

            if (value is not null)
            {
                _isSyncingSelection = true;
                SelectedUnstaged = null;
                _isSyncingSelection = false;
                _ = LoadDiffAsync(value);
            }
        }
    }

    /// <summary>The commit message being composed. Kept across navigation on purpose.</summary>
    public string Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(CanCommit));
            }
        }
    }

    public bool IsAmend
    {
        get => _isAmend;
        set
        {
            if (SetProperty(ref _isAmend, value))
            {
                OnPropertyChanged(nameof(CanCommit));
                OnPropertyChanged(nameof(CommitButtonText));
            }
        }
    }

    public bool IsSignOff
    {
        get => _isSignOff;
        set => SetProperty(ref _isSignOff, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanCommit));
            }
        }
    }

    public int UnstagedCount => Unstaged.Count;

    public int StagedCount => Staged.Count;

    public bool HasChanges => Unstaged.Count > 0 || Staged.Count > 0;

    /// <summary>Shown in place of the two lists when there is nothing to commit.</summary>
    public Visibility CleanVisibility => HasChanges ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ChangesVisibility => HasChanges ? Visibility.Visible : Visibility.Collapsed;

    public string CommitButtonText => IsAmend ? "Amend commit" : "Commit";

    /// <summary>
    ///  Amending may commit an empty staging area — it can just reword HEAD — so the staged count
    ///  only gates a normal commit.
    /// </summary>
    public bool CanCommit =>
        !IsBusy && !string.IsNullOrWhiteSpace(Message) && (Staged.Count > 0 || IsAmend);

    /// <summary>Re-reads the working directory. Safe to call repeatedly; the page does so on every refresh.</summary>
    public async Task RefreshAsync()
    {
        string? previouslySelected = (SelectedUnstaged ?? SelectedStaged)?.Name;
        bool wasStaged = SelectedStaged is not null;

        IReadOnlyList<GitItemStatus> files = await Task.Run(_loader.GetWorkingDirectoryChanges);

        Unstaged.Clear();
        Staged.Clear();

        foreach (GitItemStatus file in files)
        {
            ChangedFileViewModel row = new(file, isWorkingDirectory: true);
            (row.IsStaged ? Staged : Unstaged).Add(row);
        }

        RaiseCountsChanged();
        RestoreSelection(previouslySelected, wasStaged);
    }

    public Task<GitOperationResult> StageAsync(ChangedFileViewModel file) =>
        RunAsync(() => _loader.StageFile(file.Name));

    public Task<GitOperationResult> UnstageAsync(ChangedFileViewModel file) =>
        RunAsync(() => _loader.UnstageFile(file.Name));

    public Task<GitOperationResult> StageAllAsync() =>
        RunAsync(() => _loader.StageAll());

    public Task<GitOperationResult> UnstageAllAsync() =>
        RunAsync(() => _loader.UnstageAll());

    /// <summary>Throws away a file's uncommitted changes. Callers confirm first — this is not undoable.</summary>
    public Task<GitOperationResult> DiscardAsync(ChangedFileViewModel file) =>
        RunAsync(() => _loader.DiscardFile(file.Name, file.IsNew));

    /// <summary>
    ///  Stages or unstages a single hunk, depending on which list the file is currently in.
    /// </summary>
    /// <remarks>
    ///  A hunk shown from the unstaged side is staged by applying its patch to the index; the same
    ///  hunk shown from the staged side is unstaged by applying it in reverse. One action, and which
    ///  direction it means follows from where you are looking at it.
    /// </remarks>
    public Task<GitOperationResult> ApplyHunkAsync(DiffHunk hunk)
    {
        ChangedFileViewModel? file = SelectedStaged ?? SelectedUnstaged;

        if (file is null)
        {
            return Task.FromResult(new GitOperationResult("Apply hunk", false, "No file is selected."));
        }

        bool reverse = file.IsStaged;
        string description = reverse ? "Unstage hunk" : "Stage hunk";

        return RunAsync(() => _loader.ApplyToIndex(hunk.Patch, reverse, description));
    }

    /// <summary>Pre-fills the message from HEAD so ticking Amend leaves rewording one edit away.</summary>
    public async Task PrepareAmendAsync()
    {
        if (Message.Length == 0)
        {
            Message = await Task.Run(_loader.GetLastCommitMessage);
        }
    }

    public async Task<GitOperationResult> CommitAsync()
    {
        IsBusy = true;

        try
        {
            GitOperationResult result = await Task.Run(() => _loader.Commit(Message, IsAmend, IsSignOff));

            if (result.Succeeded)
            {
                // A committed message must not linger in the box, or the next commit reuses it.
                Message = "";
                IsAmend = false;
                Diff.Clear();
                await _onCommitted();
            }

            await RefreshAsync();
            return result;
        }
        catch (Exception ex)
        {
            return new GitOperationResult("Commit", false, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<GitOperationResult> RunAsync(Func<GitOperationResult> operation)
    {
        IsBusy = true;

        try
        {
            GitOperationResult result = await Task.Run(operation);
            await RefreshAsync();
            return result;
        }
        catch (Exception ex)
        {
            return new GitOperationResult("Error", false, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task LoadDiffAsync(ChangedFileViewModel file)
    {
        // The action reads as the opposite depending on which side the file is on.
        Diff.HunkActionLabel = file.IsStaged ? "Unstage hunk" : "Stage hunk";

        return Diff.LoadAsync(
            file.Name,
            _ => Task.Run(() => _loader.GetWorkingDirectoryDiff(file.Name, file.OldName, file.IsStaged)));
    }

    /// <summary>
    ///  Puts the selection back on the same path after a refresh, following it across the staged
    ///  boundary — staging a file you were reading should not blank the diff panel.
    /// </summary>
    private void RestoreSelection(string? name, bool wasStaged)
    {
        if (name is null)
        {
            SelectedUnstaged = Unstaged.FirstOrDefault();
            SelectedStaged = SelectedUnstaged is null ? Staged.FirstOrDefault() : null;
            return;
        }

        ChangedFileViewModel? sameSide = (wasStaged ? Staged : Unstaged).FirstOrDefault(file => file.Name == name);
        ChangedFileViewModel? otherSide = (wasStaged ? Unstaged : Staged).FirstOrDefault(file => file.Name == name);
        ChangedFileViewModel? match = sameSide ?? otherSide;

        if (match is null)
        {
            // The file is fully committed or discarded; fall back to whatever is left.
            SelectedUnstaged = Unstaged.FirstOrDefault();
            SelectedStaged = SelectedUnstaged is null ? Staged.FirstOrDefault() : null;

            if (SelectedUnstaged is null && SelectedStaged is null)
            {
                Diff.Clear();
            }

            return;
        }

        if (Staged.Contains(match))
        {
            SelectedStaged = match;
        }
        else
        {
            SelectedUnstaged = match;
        }
    }

    private void RaiseCountsChanged()
    {
        OnPropertyChanged(nameof(UnstagedCount));
        OnPropertyChanged(nameof(StagedCount));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CleanVisibility));
        OnPropertyChanged(nameof(ChangesVisibility));
        OnPropertyChanged(nameof(CanCommit));
    }
}
