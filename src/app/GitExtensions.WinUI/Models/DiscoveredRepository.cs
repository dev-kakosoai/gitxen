namespace GitExtensions.WinUI.Models;

/// <summary>
///  A repository the folder scan found, and what the setup wizard should do with it.
/// </summary>
/// <remarks>
///  Separate from <see cref="RecentRepository"/>: that one describes a repository the app already
///  knows about and is showing on Home, whereas this one is a candidate the user has not accepted
///  yet. Merging them would mean a scan writing into the Home list before anyone agreed to import
///  anything.
/// </remarks>
public sealed class DiscoveredRepository : ObservableObject
{
    private bool _isSelected = true;
    private string _projectName = "";

    public DiscoveredRepository(string path, string relativePath, string branch, string suggestedProject)
    {
        Path = path;
        RelativePath = relativePath;
        Branch = branch;
        SuggestedProject = suggestedProject;
        Name = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
    }

    public string Path { get; }

    public string Name { get; }

    /// <summary>
    ///  Where it sits below the folder that was scanned.
    /// </summary>
    /// <remarks>
    ///  Shown instead of the absolute path: after scanning one folder, the shared prefix is the same
    ///  on every row and only pushes the part that distinguishes them off the end of the line.
    /// </remarks>
    public string RelativePath { get; }

    /// <summary>The checked-out branch, read from .git/HEAD rather than by running git.</summary>
    public string Branch { get; }

    /// <summary>
    ///  The folder this repository sits in, which is what "group by folder" would name its project.
    ///  Empty when the repository is directly inside the scanned folder.
    /// </summary>
    public string SuggestedProject { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>The project to import into; empty means no project.</summary>
    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value ?? "");
    }
}

/// <summary>
///  A project the wizard is offering to create, before it becomes a <see cref="RepositoryGroup"/>.
/// </summary>
/// <remarks>
///  Proposed names are editable, so this cannot simply be the group itself: nothing should be
///  created until the wizard is finished, or backing out of it would leave projects behind.
/// </remarks>
public sealed class ProposedProject : ObservableObject
{
    private string _name;

    public ProposedProject(string name, string colorKey, int count)
    {
        _name = name;
        ColorKey = colorKey;
        Count = count;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string ColorKey { get; }

    public int Count { get; }

    public string CountText => Count == 1 ? "1 repository" : $"{Count} repositories";

    public Microsoft.UI.Xaml.Media.Brush Accent => GroupPalette.Accent(ColorKey);
}
