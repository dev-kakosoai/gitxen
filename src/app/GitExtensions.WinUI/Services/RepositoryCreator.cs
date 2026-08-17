using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUIPluginInterfaces;

namespace GitExtensions.WinUI.Services;

/// <summary>
///  Creates repositories: cloning one from a URL, or initialising an empty one.
/// </summary>
/// <remarks>
///  Separate from <see cref="RepositoryLoader"/>, which is built around a repository that already
///  exists. These two commands run in the <em>parent</em> directory and produce the working directory
///  the rest of the app then opens, so there is nothing for a per-repository loader to attach to.
/// </remarks>
internal sealed class RepositoryCreator
{
    private readonly IGitExecutorProvider _executorProvider;

    public RepositoryCreator(IGitExecutorProvider executorProvider)
    {
        _executorProvider = executorProvider;
    }

    /// <summary>
    ///  Clones <paramref name="url"/> into a new folder under <paramref name="parentDirectory"/>.
    /// </summary>
    /// <param name="folderName">
    ///  The folder to create. Empty means let git derive it from the URL, matching what the command
    ///  line does.
    /// </param>
    /// <returns>The result, and the working directory to open when it succeeded.</returns>
    public (GitOperationResult Result, string WorkingDirectory) Clone(
        string url,
        string parentDirectory,
        string folderName,
        bool recurseSubmodules)
    {
        string target = string.IsNullOrWhiteSpace(folderName) ? DeriveFolderName(url) : folderName.Trim();

        if (string.IsNullOrEmpty(target))
        {
            return (new GitOperationResult("Clone", false, "Could not work out a folder name from that URL."), "");
        }

        string workingDirectory = Path.Combine(parentDirectory, target);

        if (Directory.Exists(workingDirectory) && Directory.EnumerateFileSystemEntries(workingDirectory).Any())
        {
            return (new GitOperationResult("Clone", false, $"{workingDirectory} already exists and is not empty."), "");
        }

        GitArgumentBuilder arguments = new("clone")
        {
            "--progress",
            { recurseSubmodules, "--recurse-submodules" },
            url.Quote(),
            target.Quote()
        };

        GitOperationResult result = Run(parentDirectory, "Clone", arguments);
        return (result, result.Succeeded ? workingDirectory : "");
    }

    /// <summary>Initialises an empty repository, creating the folder if it is not there yet.</summary>
    public (GitOperationResult Result, string WorkingDirectory) Init(string directory, bool bare)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (new GitOperationResult("Initialise", false, ex.Message), "");
        }

        GitOperationResult result = Run(directory, "Initialise", new GitArgumentBuilder("init")
        {
            { bare, "--bare" }
        });

        return (result, result.Succeeded && !bare ? directory : "");
    }

    /// <summary>
    ///  "https://host/owner/repo.git" and "git@host:owner/repo.git" both yield "repo" — the same name
    ///  <c>git clone</c> would pick.
    /// </summary>
    private static string DeriveFolderName(string url)
    {
        string trimmed = url.Trim().TrimEnd('/');

        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        int separator = trimmed.LastIndexOfAny(['/', ':', '\\']);
        return separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
    }

    /// <summary>
    ///  Runs git in <paramref name="workingDirectory"/>, which for these commands is a plain folder
    ///  rather than a repository.
    /// </summary>
    private GitOperationResult Run(string workingDirectory, string description, ArgumentString arguments)
    {
        GitModule module = new(_executorProvider, workingDirectory);

        // throwOnErrorExit: false — a bad URL or an unreachable host is information for the user.
        ExecutionResult result = module.GitExecutable.Execute(arguments, throwOnErrorExit: false);
        return new GitOperationResult(description, result.ExitedSuccessfully, result.AllOutput.Trim());
    }
}
