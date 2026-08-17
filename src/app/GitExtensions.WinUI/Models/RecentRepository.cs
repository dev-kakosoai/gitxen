using Microsoft.UI.Xaml;

namespace GitExtensions.WinUI.Models;

/// <summary>
///  A repository on the Home page's recent list, with enough of its current state to be worth
///  looking at before opening it.
/// </summary>
/// <remarks>
///  The branch and last commit are read in the background after the list appears, rather than being
///  part of building it: each one costs two git calls against a repository that may be on a slow or
///  disconnected drive, and the list is useful without them.
/// </remarks>
public sealed class RecentRepository : ObservableObject
{
    private string _branch = "";
    private string _lastCommit = "";
    private bool _isMissing;

    public RecentRepository(string path)
    {
        Path = path;
        Name = GetName(path);
    }

    public string Path { get; }

    public string Name { get; }

    /// <summary>The checked-out branch, once it has been read.</summary>
    public string Branch
    {
        get => _branch;
        set
        {
            if (SetProperty(ref _branch, value))
            {
                OnPropertyChanged(nameof(DetailsVisibility));
            }
        }
    }

    public string LastCommit
    {
        get => _lastCommit;
        set => SetProperty(ref _lastCommit, value);
    }

    /// <summary>
    ///  The folder is gone or is no longer a repository. Kept in the list rather than dropped
    ///  silently: a moved repository is worth telling someone about, and removing the entry would
    ///  make it look like they had never opened it.
    /// </summary>
    public bool IsMissing
    {
        get => _isMissing;
        set
        {
            if (SetProperty(ref _isMissing, value))
            {
                OnPropertyChanged(nameof(MissingVisibility));
                OnPropertyChanged(nameof(DetailsVisibility));
            }
        }
    }

    public Visibility MissingVisibility => IsMissing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DetailsVisibility =>
        !IsMissing && Branch.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    private static string GetName(string path)
    {
        string trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        string name = System.IO.Path.GetFileName(trimmed);
        return name.Length == 0 ? trimmed : name;
    }
}
