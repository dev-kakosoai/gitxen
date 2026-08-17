using System.Collections.ObjectModel;
using System.Globalization;
using GitExtensions.WinUI.Diff;
using GitExtensions.WinUI.Services;
using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI.ViewModels;

/// <summary>
///  One diff panel: the parsed lines, the unified/side-by-side choice, and the loading of both.
/// </summary>
/// <remarks>
///  Extracted so the History page and the Changes page can each own a panel. They show different
///  diffs at the same time, which a single shared set of collections on the tab could not express.
///  The caller supplies the fetch as a delegate because where the text comes from differs — a commit
///  pair for history, the index or worktree for uncommitted changes.
/// </remarks>
public sealed class DiffViewModel : ObservableObject
{
    private static int MaxDiffLines => AppOptions.MaxDiffLines;

    private CancellationTokenSource? _cts;
    private bool _isSideBySide;
    private string _title = "";
    private bool _isLoading;

    /// <summary>
    ///  When true, each hunk header gets a stage/unstage affordance. Only the Changes page sets it —
    ///  a diff between two commits has nothing to stage.
    /// </summary>
    public bool SupportsHunkStaging { get; init; }

    /// <summary>
    ///  Labels the hunk action. Staging and unstaging are the same operation in opposite directions,
    ///  so the panel is told which one it is showing rather than working it out per row.
    /// </summary>
    public string HunkActionLabel { get; set; } = "Stage hunk";

    /// <summary>Raised when a hunk's action is invoked; the host page performs the git work.</summary>
    public event EventHandler<DiffHunk>? HunkActionRequested;

    public void RequestHunkAction(DiffHunk hunk) => HunkActionRequested?.Invoke(this, hunk);

    public ObservableCollection<DiffLineViewModel> Lines { get; } = [];

    public ObservableCollection<SideBySideRow> SideBySideLines { get; } = [];

    /// <summary>Unified or side-by-side; the panel shows one or the other.</summary>
    public bool IsSideBySide
    {
        get => _isSideBySide;
        set
        {
            if (SetProperty(ref _isSideBySide, value))
            {
                OnPropertyChanged(nameof(UnifiedVisibility));
                OnPropertyChanged(nameof(SideBySideVisibility));
                RebuildSideBySide();
            }
        }
    }

    public string Title
    {
        get => _title;
        private set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(HasContent));
                OnPropertyChanged(nameof(ContentVisibility));
                OnPropertyChanged(nameof(PlaceholderVisibility));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasContent => Title.Length > 0;

    public Visibility ContentVisibility => HasContent ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The "select a file" prompt, shown while nothing is loaded.</summary>
    public Visibility PlaceholderVisibility => HasContent ? Visibility.Collapsed : Visibility.Visible;

    public Visibility UnifiedVisibility =>
        HasContent && !IsSideBySide ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SideBySideVisibility =>
        HasContent && IsSideBySide ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    ///  Loads and parses a diff, superseding whatever was loading before.
    /// </summary>
    /// <param name="fileName">Used for the title and to pick the syntax highlighter.</param>
    /// <param name="fetch">Produces the raw unified diff text; runs off the UI thread.</param>
    public async Task LoadAsync(string fileName, Func<CancellationToken, Task<string>> fetch)
    {
        CancellationTokenSource cts = Supersede();

        Lines.Clear();
        SideBySideLines.Clear();
        Title = fileName;
        IsLoading = true;

        try
        {
            string diff = await fetch(cts.Token);

            if (cts.Token.IsCancellationRequested)
            {
                return;
            }

            if (string.IsNullOrEmpty(diff))
            {
                Lines.Add(DiffLineViewModel.CreatePlain("(no textual diff — binary file, or no changes)"));
                return;
            }

            IReadOnlyList<DiffLineViewModel> parsed = DiffParser.ParseUnified(diff, fileName, MaxDiffLines, out int omitted);

            // Hunks come from the raw text, not the parsed rows: the rows stop at the display cap and
            // a patch rebuilt from them would not apply cleanly. The header rows are then matched up
            // with them in order.
            IReadOnlyList<DiffHunk> hunks = SupportsHunkStaging ? HunkSplitter.Split(diff) : [];
            int hunkIndex = 0;

            foreach (DiffLineViewModel line in parsed)
            {
                if (line.Kind == DiffLineKind.Hunk && hunkIndex < hunks.Count)
                {
                    line.Hunk = hunks[hunkIndex++];
                }

                Lines.Add(line);
            }

            if (omitted > 0)
            {
                Lines.Add(DiffLineViewModel.CreatePlain(
                    $"… {omitted.ToString("N0", CultureInfo.InvariantCulture)} more lines not shown"));
            }

            RebuildSideBySide();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection.
        }
        catch (Exception ex)
        {
            Lines.Add(DiffLineViewModel.CreatePlain(ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                IsLoading = false;
                _cts = null;
            }
        }
    }

    /// <summary>Empties the panel back to its placeholder state.</summary>
    public void Clear()
    {
        Supersede();
        Lines.Clear();
        SideBySideLines.Clear();
        Title = "";
        IsLoading = false;
    }

    private CancellationTokenSource Supersede()
    {
        // Deliberately not disposed: background work may still observe the token after cancellation,
        // and disposing it out from under them throws. They hold no timer, so the GC can have them.
        CancellationTokenSource? previous = _cts;
        CancellationTokenSource cts = new();
        _cts = cts;
        previous?.Cancel();
        return cts;
    }

    private void RebuildSideBySide()
    {
        SideBySideLines.Clear();

        if (!IsSideBySide)
        {
            return;
        }

        foreach (SideBySideRow row in DiffParser.ToSideBySide([.. Lines]))
        {
            SideBySideLines.Add(row);
        }
    }
}
