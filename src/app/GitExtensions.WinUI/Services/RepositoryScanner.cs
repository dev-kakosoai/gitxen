using GitCommands;
using GitExtensions.WinUI.Models;

namespace GitExtensions.WinUI.Services;

/// <summary>How far along a scan is, for the progress line in the wizard.</summary>
/// <param name="FoldersVisited">Directories looked at so far.</param>
/// <param name="Found">Repositories found so far.</param>
/// <param name="CurrentFolder">The directory being looked at, for something to show.</param>
public readonly record struct ScanProgress(int FoldersVisited, int Found, string CurrentFolder);

/// <summary>
///  Finds git repositories under a folder.
/// </summary>
/// <remarks>
///  <para>
///   Iterative rather than recursive, over an explicit stack: a scan is pointed at whatever folder
///   the user picks, which may be a drive root with a pathological depth, and recursion there
///   overflows the stack rather than reporting anything.
///  </para>
///  <para>
///   The walk does not descend into a repository once it finds one. A repository's own submodules
///   and nested worktrees are part of it, not separate things to import, and including them turns
///   one meaningful result into a list nobody wants to read.
///  </para>
/// </remarks>
internal static class RepositoryScanner
{
    /// <summary>
    ///  Directories never worth walking into.
    /// </summary>
    /// <remarks>
    ///  Dependency and build folders hold tens of thousands of files and no repositories, and they
    ///  are what makes the difference between a scan taking two seconds and taking two minutes.
    ///  <c>node_modules</c> is the extreme case, but a vendored one can genuinely contain a
    ///  <c>.git</c>, which is another reason not to look inside.
    /// </remarks>
    private static readonly HashSet<string> _skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", "vendor", "target", "dist", "build",
        ".vs", ".vscode", ".idea", ".gradle", ".nuget", ".cargo", ".venv", "venv", "env",
        "__pycache__", "AppData", "$Recycle.Bin", "System Volume Information", "Windows"
    };

    /// <summary>How deep below the scanned folder to look, when the caller has no opinion.</summary>
    public const int DefaultMaxDepth = 4;

    /// <summary>
    ///  Walks <paramref name="root"/> and returns every repository found, ordered by path.
    /// </summary>
    /// <param name="maxDepth">
    ///  Levels below <paramref name="root"/> to descend. Repositories are usually one or two folders
    ///  down from where people keep them, and an unbounded walk of a large drive takes long enough
    ///  that it reads as a hang.
    /// </param>
    public static Task<IReadOnlyList<DiscoveredRepository>> ScanAsync(
        string root,
        int maxDepth,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
        => Task.Run(() => Scan(root, maxDepth, progress, cancellationToken), cancellationToken);

    private static IReadOnlyList<DiscoveredRepository> Scan(
        string root,
        int maxDepth,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<DiscoveredRepository> found = [];

        if (!Directory.Exists(root))
        {
            return found;
        }

        Stack<(string Directory, int Depth)> pending = new();
        pending.Push((root, 0));

        int visited = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (string directory, int depth) = pending.Pop();
            visited++;

            // Reporting every directory would post thousands of messages to the UI thread for a
            // label nobody can read that fast; every repository found is worth showing immediately.
            if (visited % 25 == 0)
            {
                progress?.Report(new ScanProgress(visited, found.Count, directory));
            }

            if (IsRepository(directory))
            {
                found.Add(Describe(root, directory));
                progress?.Report(new ScanProgress(visited, found.Count, directory));

                // Do not descend: see the class remarks.
                continue;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (string child in EnumerateSubdirectories(directory))
            {
                string name = Path.GetFileName(child);

                if (_skipped.Contains(name))
                {
                    continue;
                }

                // Hidden and system folders are the user's profile plumbing far more often than they
                // are somewhere a repository was deliberately put.
                try
                {
                    FileAttributes attributes = File.GetAttributes(child);

                    if (attributes.HasFlag(FileAttributes.System)
                        || (attributes.HasFlag(FileAttributes.Hidden) && name.StartsWith('.')))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                pending.Push((child, depth + 1));
            }
        }

        progress?.Report(new ScanProgress(visited, found.Count, ""));

        return found.OrderBy(repository => repository.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    ///  Lists subdirectories, treating an unreadable folder as empty.
    /// </summary>
    /// <remarks>
    ///  A scan crosses junctions, other users' profiles and folders the account simply cannot open.
    ///  One <see cref="UnauthorizedAccessException"/> must not end a scan that has already found
    ///  everything else.
    /// </remarks>
    private static IEnumerable<string> EnumerateSubdirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    ///  Whether the folder is a working directory.
    /// </summary>
    /// <remarks>
    ///  The cheap existence check first, because it is false for almost every folder a scan visits,
    ///  and only then the backend's own definition, so the wizard agrees with what the rest of the
    ///  app will accept when it tries to open the result.
    /// </remarks>
    private static bool IsRepository(string directory)
    {
        string dotGit = Path.Combine(directory, ".git");

        if (!Directory.Exists(dotGit) && !File.Exists(dotGit))
        {
            return false;
        }

        return GitModule.IsValidGitWorkingDir(directory);
    }

    private static DiscoveredRepository Describe(string root, string directory)
    {
        string relative = Path.GetRelativePath(root, directory);

        // The folder the repository sits in, relative to the scan: "ClientA" for
        // D:\Projects\ClientA\api when D:\Projects was scanned, and empty when it is at the top.
        string? parent = Path.GetDirectoryName(relative);
        string suggested = string.IsNullOrEmpty(parent) || parent == "."
            ? ""
            : parent.Split(Path.DirectorySeparatorChar)[0];

        return new DiscoveredRepository(directory, relative, ReadBranch(directory), suggested);
    }

    /// <summary>
    ///  Reads the checked-out branch out of <c>.git/HEAD</c>.
    /// </summary>
    /// <remarks>
    ///  Read from the file rather than by running <c>git branch</c>: a scan of a projects folder
    ///  routinely finds dozens of repositories, and spawning a git process for each one costs more
    ///  than the whole walk did. HEAD is a single line and its format is stable.
    /// </remarks>
    private static string ReadBranch(string directory)
    {
        try
        {
            string head = Path.Combine(directory, ".git", "HEAD");

            if (!File.Exists(head))
            {
                // A worktree or submodule, where .git is a file pointing elsewhere. Not worth
                // following for a label on a list.
                return "";
            }

            string content = File.ReadAllText(head).Trim();

            const string prefix = "ref: refs/heads/";
            return content.StartsWith(prefix, StringComparison.Ordinal)
                ? content[prefix.Length..]
                : $"detached at {content[..Math.Min(7, content.Length)]}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
