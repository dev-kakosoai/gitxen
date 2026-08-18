using System.Diagnostics;

namespace GitExtensions.WinUI.Services;

/// <summary>
///  An application that can be opened on a repository's working directory.
/// </summary>
/// <param name="Id">Stable identifier, used by the command palette.</param>
/// <param name="Name">What the menu says.</param>
/// <param name="Glyph">Segoe Fluent glyph for the menu item.</param>
/// <param name="Executable">The command to look for on PATH, without an extension.</param>
/// <param name="TerminalCommand">
///  For a command-line tool, the command to run inside a terminal. Empty for applications that take
///  the folder as an argument directly.
/// </param>
public sealed record ExternalTool(
    string Id,
    string Name,
    string Glyph,
    string Executable,
    string TerminalCommand = "");

/// <summary>
///  Opens a repository in another application — an editor, a terminal, or a coding agent.
/// </summary>
/// <remarks>
///  <para>
///   Availability is decided by probing PATH rather than by trying to launch and seeing what happens:
///   a menu that lists everything and fails on click is worse than one that lists what is installed.
///   The probe is a directory scan of the PATH entries, so it costs no processes.
///  </para>
///  <para>
///   Command-line tools are launched through Windows Terminal where it exists, because they need a
///   console to run in and Terminal is the one that can be told which directory to start in. Without
///   it they fall back to the classic console host.
///  </para>
/// </remarks>
public static class ExternalTools
{
    /// <summary>Extensions a command can have on Windows, in the order the shell would resolve them.</summary>
    private static readonly string[] _executableExtensions = [".exe", ".cmd", ".bat", ".com"];

    private static readonly ExternalTool[] _all =
    [
        new("vscode", "Visual Studio Code", "", "code"),
        new("terminal", "Windows Terminal", "", "wt"),
        new("claude", "Claude Code", "", "claude", TerminalCommand: "claude"),
        new("codex", "Codex", "", "codex", TerminalCommand: "codex"),
        new("explorer", "File Explorer", "", "explorer")
    ];

    /// <summary>The tools that are actually installed, in menu order.</summary>
    public static IReadOnlyList<ExternalTool> Available =>
        [.. _all.Where(tool => Resolve(tool.Executable) is not null)];

    /// <summary>
    ///  Opens <paramref name="workingDirectory"/> in <paramref name="tool"/>.
    /// </summary>
    /// <returns>An error message, or empty when the application was started.</returns>
    public static string Launch(ExternalTool tool, string workingDirectory)
    {
        if (Resolve(tool.Executable) is not string executable)
        {
            return $"{tool.Name} was not found on your PATH.";
        }

        try
        {
            ProcessStartInfo start = tool.TerminalCommand.Length == 0
                ? DirectStart(executable, workingDirectory)
                : TerminalStart(tool.TerminalCommand, workingDirectory);

            // UseShellExecute so .cmd shims — which is how most of these install — resolve properly.
            start.UseShellExecute = true;
            start.WorkingDirectory = workingDirectory;

            Process.Start(start);
            return "";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    private static ProcessStartInfo DirectStart(string executable, string workingDirectory) =>
        new(executable, [workingDirectory]);

    /// <summary>
    ///  Runs a console tool in a terminal opened at the repository.
    /// </summary>
    /// <remarks>
    ///  Windows Terminal is preferred because <c>-d</c> sets the starting directory; the fallback runs
    ///  through cmd with <c>/k</c> so the window stays open if the tool exits immediately, which is
    ///  otherwise indistinguishable from nothing having happened.
    /// </remarks>
    private static ProcessStartInfo TerminalStart(string command, string workingDirectory) =>
        Resolve("wt") is not null
            ? new ProcessStartInfo("wt", ["-d", workingDirectory, command])
            : new ProcessStartInfo("cmd", ["/k", command]);

    /// <summary>
    ///  Finds a command on PATH, returning the name to launch or null when it is not installed.
    /// </summary>
    private static string? Resolve(string command)
    {
        // Shipped with Windows; probing PATH for it is pointless and occasionally wrong.
        if (command == "explorer")
        {
            return command;
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string extension in _executableExtensions)
            {
                try
                {
                    if (File.Exists(Path.Combine(directory.Trim(), command + extension)))
                    {
                        return command;
                    }
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is not worth failing the whole probe over.
                }
            }
        }

        return null;
    }
}
