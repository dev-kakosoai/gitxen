using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.WinUI.Services;

/// <summary>
///  Reads and writes <c>git config --global</c>, with no repository open.
/// </summary>
/// <remarks>
///  <see cref="RepositoryLoader"/> can already do this, but only for a repository that exists: it is
///  built around one working directory. The setup wizard runs before anything has been opened and
///  still needs to ask about the identity that every future commit will carry, so it gets a module
///  rooted at the user's profile — a folder that is always there and that git is happy to run in.
/// </remarks>
internal sealed class GlobalGitSettings
{
    private readonly GitModule _module;

    public GlobalGitSettings(IGitExecutorProvider executorProvider)
    {
        _module = new GitModule(
            executorProvider,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>The value at global scope, empty when it is not set there.</summary>
    public string Get(string key)
    {
        ExecutionResult result = _module.GitExecutable.Execute(
            new GitArgumentBuilder("config") { "--global", "--get", key.Quote() },
            throwOnErrorExit: false);

        // Exit code 1 simply means "not set", which is the normal answer on a fresh machine.
        return result.ExitedSuccessfully ? result.StandardOutput.Trim() : "";
    }

    /// <summary>
    ///  Sets a value, or removes the setting when <paramref name="value"/> is empty.
    /// </summary>
    /// <remarks>
    ///  Same reasoning as the per-repository writer: <c>user.name=</c> is a name that is genuinely
    ///  empty and makes git refuse to commit, whereas an absent key is what someone clearing a box
    ///  meant.
    /// </remarks>
    public GitOperationResult Set(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ExecutionResult cleared = _module.GitExecutable.Execute(
                new GitArgumentBuilder("config") { "--global", "--unset-all", key.Quote() },
                throwOnErrorExit: false);

            // Exit code 5 is "nothing to unset", which is success as far as the caller is concerned.
            return cleared.ExitedSuccessfully || cleared.ExitCode == 5
                ? new GitOperationResult($"Clear {key}", true, "")
                : new GitOperationResult($"Clear {key}", false, cleared.AllOutput.Trim());
        }

        ExecutionResult result = _module.GitExecutable.Execute(
            new GitArgumentBuilder("config") { "--global", key.Quote(), value.Trim().Quote() },
            throwOnErrorExit: false);

        return new GitOperationResult($"Set {key}", result.ExitedSuccessfully, result.AllOutput.Trim());
    }

    /// <summary>Whether git itself can be run at all, which is worth knowing before anything else.</summary>
    public bool IsGitAvailable()
    {
        try
        {
            ExecutionResult result = _module.GitExecutable.Execute(
                new GitArgumentBuilder("--version"),
                throwOnErrorExit: false);

            return result.ExitedSuccessfully;
        }
        catch (Exception)
        {
            // No git on PATH at all: the process could not even be started.
            return false;
        }
    }

    /// <summary>The version string, for the wizard to show when it reports git was found.</summary>
    public string GetVersion()
    {
        try
        {
            ExecutionResult result = _module.GitExecutable.Execute(
                new GitArgumentBuilder("--version"),
                throwOnErrorExit: false);

            return result.ExitedSuccessfully ? result.StandardOutput.Trim() : "";
        }
        catch (Exception)
        {
            return "";
        }
    }
}
