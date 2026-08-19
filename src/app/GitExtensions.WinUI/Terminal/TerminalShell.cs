namespace GitExtensions.WinUI.Terminal;

/// <summary>
///  A shell the terminal pane can host.
/// </summary>
/// <param name="Id">Stable identifier, in case a default shell is ever persisted.</param>
/// <param name="Name">What the menu says.</param>
/// <param name="ExecutablePath">Full path to the shell executable.</param>
/// <param name="Arguments">Arguments the shell needs to come up interactive.</param>
public sealed record TerminalShell(
    string Id,
    string Name,
    string ExecutablePath,
    string Arguments = "");

/// <summary>
///  The shells installed on this machine, probed once per process.
/// </summary>
/// <remarks>
///  Probed by looking for the files rather than by launching anything: a menu that lists every shell
///  and fails on click is worse than one that lists what is actually there. The order is the default
///  order — the first entry is what a bare "open terminal" starts.
/// </remarks>
public static class TerminalShells
{
    /// <summary>The shells that are actually installed, in preference order.</summary>
    public static IReadOnlyList<TerminalShell> Available { get; } = Detect();

    /// <summary>The shell a new terminal opens with, or null when the machine has none at all.</summary>
    public static TerminalShell? Default => Available.Count > 0 ? Available[0] : null;

    private static List<TerminalShell> Detect()
    {
        List<TerminalShell> shells = [];

        // PowerShell 7 ahead of Windows PowerShell: where both exist, the one someone installed by
        // hand is the one they mean.
        if (FindOnPath("pwsh") is string pwsh)
        {
            shells.Add(new TerminalShell("pwsh", "PowerShell 7", pwsh, "-NoLogo"));
        }

        string windowsPowerShell = Path.Combine(
            Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(windowsPowerShell))
        {
            shells.Add(new TerminalShell("powershell", "Windows PowerShell", windowsPowerShell, "-NoLogo"));
        }

        string cmd = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (File.Exists(cmd))
        {
            shells.Add(new TerminalShell("cmd", "Command Prompt", cmd));
        }

        if (FindGitBash() is string bash)
        {
            // --login -i so .bashrc/.bash_profile run and the prompt comes up, as in Git Bash proper.
            shells.Add(new TerminalShell("gitbash", "Git Bash", bash, "--login -i"));
        }

        string wsl = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        if (File.Exists(wsl))
        {
            shells.Add(new TerminalShell("wsl", "WSL", wsl));
        }

        return shells;
    }

    /// <summary>
    ///  Finds Git Bash by walking up from git.exe rather than guessing install directories first:
    ///  the git actually on PATH is the install the user works with.
    /// </summary>
    private static string? FindGitBash()
    {
        List<string> candidates = [];

        if (FindOnPath("git") is string git)
        {
            // <root>\cmd\git.exe or <root>\bin\git.exe — bash lives in <root>\bin.
            string? root = Path.GetDirectoryName(Path.GetDirectoryName(git));
            if (root is not null)
            {
                candidates.Add(Path.Combine(root, "bin", "bash.exe"));
            }
        }

        foreach (string? programs in new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetEnvironmentVariable("LocalAppData") is string local
                ? Path.Combine(local, "Programs")
                : null
        })
        {
            if (programs is not null)
            {
                candidates.Add(Path.Combine(programs, "Git", "bin", "bash.exe"));
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Resolves a command on PATH to its full path, or null when it is not installed.</summary>
    private static string? FindOnPath(string command)
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim(), command + ".exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is not worth failing the whole probe over.
            }
        }

        return null;
    }
}
