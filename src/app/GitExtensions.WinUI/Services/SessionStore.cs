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

    public WindowBounds? Window { get; set; }
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

    private static string SessionFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GitExtensions.WinUI",
        "session.json");

    public static SessionState Load()
    {
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

    public static void Save(SessionState state)
    {
        try
        {
            string path = SessionFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, _options));
        }
        catch (Exception)
        {
            // Losing the session is not worth surfacing an error over.
        }
    }
}
