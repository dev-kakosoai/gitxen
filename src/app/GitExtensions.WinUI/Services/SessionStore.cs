using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitExtensions.WinUI.Services;

/// <summary>
///  What the shell restores on the next launch.
/// </summary>
public sealed class SessionState
{
    public UiMode Mode { get; set; } = UiMode.Simple;

    /// <summary>Working directories of the tabs that were open, in tab order.</summary>
    public List<string> Repositories { get; set; } = [];

    public int SelectedIndex { get; set; }

    /// <summary>Most-recently-opened repositories, newest first.</summary>
    public List<string> Recent { get; set; } = [];

    public int MaxCommits { get; set; } = 2000;

    public int MaxDiffLines { get; set; } = 5000;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public RepositoryLayout Layout { get; set; } = RepositoryLayout.Tabs;

    public int AutoFetchMinutes { get; set; } = 10;

    /// <summary>Repository groups, in display order.</summary>
    public List<SavedGroup> Groups { get; set; } = [];

    /// <summary>Per-repository state, so each one reopens the way it was left.</summary>
    public List<SavedRepositoryState> RepositoryStates { get; set; } = [];

    /// <summary>Width of the repository column, in effective pixels.</summary>
    public double SidebarWidth { get; set; } = 280;

    public WindowBounds? Window { get; set; }
}

/// <summary>
///  A repository group as it is stored.
/// </summary>
/// <remarks>
///  Repositories are recorded as paths rather than as objects: the group is a statement about which
///  working directories belong together, and it should survive one of them being closed, reopened or
///  temporarily unavailable.
/// </remarks>
public sealed class SavedGroup
{
    public string Name { get; set; } = "";

    public string Glyph { get; set; } = "";

    public string ColorKey { get; set; } = "";

    public bool IsExpanded { get; set; } = true;

    public List<string> Repositories { get; set; } = [];
}

/// <summary>
///  How one repository was left: which section, what was being typed, how the diff was being read.
/// </summary>
/// <remarks>
///  Keyed by working directory rather than by tab position, so state follows the repository across
///  being closed and reopened, and across tabs being reordered by their project.
/// </remarks>
public sealed class SavedRepositoryState
{
    public string Path { get; set; } = "";

    /// <summary>Navigation tag, e.g. "history"; empty falls back to the default section.</summary>
    public string Section { get; set; } = "";

    /// <summary>
    ///  An uncommitted message. Losing a half-written commit message to a restart is the single most
    ///  annoying thing a git client can do, so it is kept with the rest of the state.
    /// </summary>
    public string CommitDraft { get; set; } = "";

    public bool DiffSideBySide { get; set; }

    /// <summary>Whether History was showing every branch or only the current one.</summary>
    public bool AllBranches { get; set; }
}

public sealed class WindowBounds
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

/// <summary>
///  Persists <see cref="SessionState"/> to the user's local app data.
/// </summary>
/// <remarks>
///  Deliberately a private file rather than GitExtensions' own <c>AppSettings</c> store: this is an
///  experimental front-end and shouldn't write into the settings the real app reads.
/// </remarks>
internal static class SessionStore
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    ///  Where the session lives: under the current Windows user's local app data.
    /// </summary>
    /// <remarks>
    ///  Per user rather than per machine, so two accounts keep entirely separate state and nothing
    ///  needs a login. Local rather than roaming: it records window bounds and machine-specific paths,
    ///  which have no business following the user to another machine.
    /// </remarks>
    private static string SessionDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Gitxen");

    /// <summary>The folder used before the product was named, kept only so state can be carried over.</summary>
    private static string LegacyDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GitExtensions.WinUI");

    private static string SessionFilePath => Path.Combine(SessionDirectory, "session.json");

    /// <summary>
    ///  Carries a session written under the old folder name across to the new one.
    /// </summary>
    /// <remarks>
    ///  Copied rather than moved, and only when the new location has nothing in it: renaming the
    ///  product should not be able to lose someone's groups and layout, and leaving the old file in
    ///  place means a build from before the rename still finds its own state.
    /// </remarks>
    private static void MigrateLegacySession()
    {
        try
        {
            string current = SessionFilePath;
            string legacy = Path.Combine(LegacyDirectory, "session.json");

            if (File.Exists(current) || !File.Exists(legacy))
            {
                return;
            }

            Directory.CreateDirectory(SessionDirectory);
            File.Copy(legacy, current);
        }
        catch (Exception)
        {
            // Starting fresh is a far better outcome than refusing to start.
        }
    }

    public static SessionState Load()
    {
        MigrateLegacySession();

        try
        {
            string path = SessionFilePath;
            if (!File.Exists(path))
            {
                return new SessionState();
            }

            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), _options) ?? new SessionState();
        }
        catch (Exception ex)
        {
            // A corrupt or unreadable session should never stop the app from starting, but it
            // shouldn't look like a first run with no explanation either.
            LogRestoreFailure(ex);
            return new SessionState();
        }
    }

    /// <summary>
    ///  Records why a session failed to restore, next to the session file itself.
    /// </summary>
    public static void LogRestoreFailure(Exception exception)
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(SessionFilePath)!, "restore-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, exception.ToString());
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    ///  Writes the session, replacing the previous file only once the new one is complete.
    /// </summary>
    /// <remarks>
    ///  Written to a temporary file and then moved into place. A crash or a power cut part-way through
    ///  a direct write leaves a truncated file, which fails to parse on the next launch and silently
    ///  looks like a first run — everything the user had arranged, gone. The move is atomic, so the
    ///  worst case becomes losing the most recent save rather than all of them.
    /// </remarks>
    public static void Save(SessionState state)
    {
        try
        {
            string path = SessionFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, _options));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception)
        {
            // Losing one save is not worth surfacing an error over; the next one will succeed.
        }
    }
}
