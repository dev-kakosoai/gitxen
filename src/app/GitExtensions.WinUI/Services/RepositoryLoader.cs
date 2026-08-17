using System.Text;
using GitCommands;
using GitCommands.Git;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUIPluginInterfaces;

namespace GitExtensions.WinUI.Services;

/// <summary>
///  Wraps the reusable GitExtensions backend (<see cref="GitModule"/> + <see cref="RevisionReader"/>)
///  for a single repository, so the view models never touch it directly.
/// </summary>
/// <remarks>
///  <para>
///   One instance per open repository. <see cref="GitModule"/> isn't documented as thread-safe, so
///   callers must not run two of these operations against the same instance concurrently.
///  </para>
/// </remarks>
internal sealed class RepositoryLoader
{
    private readonly GitModule _module;

    public RepositoryLoader(IGitExecutorProvider executorProvider, string workingDir)
    {
        _module = new GitModule(executorProvider, workingDir);
    }

    /// <summary>
    ///  Streams the commit log to <paramref name="observer"/> in batches as <c>git log</c> produces
    ///  them. Blocks the calling thread for the duration — call it from a background thread.
    /// </summary>
    /// <param name="skip">How many of the most recent commits to skip, for paging.</param>
    public void StreamRevisions(IObserver<IReadOnlyList<GitRevision>> observer, int skip, CancellationToken cancellationToken)
    {
        // revisionFilter is spliced straight into the `git log` argument list, so --skip belongs here.
        string revisionFilter = skip > 0 ? $"HEAD --skip={skip}" : "HEAD";

        // allBodies: the Advanced-mode details pane shows the full commit message, not just the subject.
        new RevisionReader(_module, allBodies: true).GetLog(
            observer,
            revisionFilter,
            pathFilter: "",
            hasNotes: false,
            autostashLabel: "",
            cancellationToken: cancellationToken);
    }

    public string GetCurrentBranch() => _module.GetSelectedBranch();

    /// <summary>
    ///  The files a commit changed, relative to its first parent. Blocks — call from a background thread.
    /// </summary>
    public IReadOnlyList<GitItemStatus> GetChangedFiles(GitRevision revision, CancellationToken cancellationToken)
    {
        if (!revision.HasParent)
        {
            // A root commit has nothing to diff against; listing its whole tree isn't what the pane is for.
            return [];
        }

        return _module.GetDiffFilesWithUntracked(
            revision.FirstParentId.ToString(),
            revision.ObjectId.ToString(),
            StagedStatus.None,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///  The unified diff for one file in a commit, against its first parent.
    /// </summary>
    public async Task<string> GetDiffTextAsync(GitRevision revision, string fileName, string? oldFileName, CancellationToken cancellationToken)
    {
        if (!revision.HasParent)
        {
            return "";
        }

        // useGitColoring is off: we colour the diff ourselves, and git's ANSI escape codes would
        // otherwise just have to be stripped back out again.
        (Patch? patch, string? errorMessage) = await _module.GetSingleDiffAsync(
            revision.FirstParentId,
            revision.ObjectId,
            fileName,
            oldFileName,
            extraDiffArguments: "",
            encoding: Encoding.UTF8,
            cacheResult: true,
            isTracked: true,
            useGitColoring: false,
            commandConfiguration: GitCommandConfiguration.Default,
            cancellationToken: cancellationToken);

        return patch?.Text ?? errorMessage ?? "";
    }

    /// <summary>
    ///  <c>git blame</c> for a file, as of a revision. Returned as raw text: git's default output is
    ///  already aligned and readable, and parsing the porcelain format would buy nothing here.
    /// </summary>
    public string GetBlame(string fileName, ObjectId? revision)
    {
        GitArgumentBuilder arguments = new("blame")
        {
            "--date=short",
            { revision is not null, revision?.ToString() ?? "" },
            "--",
            fileName.Quote()
        };

        ExecutionResult result = _module.GitExecutable.Execute(arguments, throwOnErrorExit: false);
        return result.ExitedSuccessfully ? result.StandardOutput : result.AllOutput;
    }

    /// <summary>The commits that touched one file, newest first.</summary>
    public IReadOnlyList<GitRevision> GetFileHistory(string fileName, int maxCount, CancellationToken cancellationToken)
    {
        FileHistoryObserver observer = new(maxCount);

        // pathFilter is what RevisionReader already uses for per-file history, so this reuses the
        // same streaming path as the main log.
        new RevisionReader(_module).GetLog(
            observer,
            revisionFilter: "HEAD",
            pathFilter: fileName.ToPosixPath().Quote(),
            hasNotes: false,
            autostashLabel: "",
            cancellationToken: cancellationToken);

        return observer.Revisions;
    }

    public IReadOnlyList<string> GetLocalBranches() =>
        _module.GetRefs(RefsFilter.Heads).Select(reference => reference.LocalName).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Uncommitted changes in the working directory.</summary>
    public IReadOnlyList<GitItemStatus> GetWorkingDirectoryChanges() => _module.GetAllChangedFiles();

    /// <summary>
    ///  The diff for an uncommitted file. Unlike a commit diff there is no revision pair — it is the
    ///  index or worktree against HEAD — so this runs `git diff` directly and returns its raw output.
    /// </summary>
    public string GetWorkingDirectoryDiff(string fileName, string? oldFileName, bool staged)
    {
        ExecutionResult result = _module.GitExecutable.Execute(
            Commands.GetCurrentChanges(fileName, oldFileName, staged, extraDiffArguments: "", noLocks: false),
            throwOnErrorExit: false);

        return result.StandardOutput;
    }

    public GitOperationResult StageFile(string fileName)
    {
        _module.StageFile(fileName);
        return new GitOperationResult("Stage", true, $"Staged {fileName}.");
    }

    public GitOperationResult UnstageFile(string fileName) =>
        Run($"Unstage {fileName}", Commands.Reset(ResetMode.ResetIndex, "HEAD", fileName));

    public GitOperationResult CreateBranch(string branchName, ObjectId objectId, bool checkout) =>
        Run($"Create branch {branchName}", Commands.Branch(branchName, objectId, checkout));

    /// <summary>Creates a branch at HEAD, for when no specific commit is selected.</summary>
    public GitOperationResult CreateBranchFromHead(string branchName, bool checkout) =>
        Run($"Create branch {branchName}", new GitArgumentBuilder(checkout ? "checkout" : "branch")
        {
            { checkout, "-b" },
            branchName.Quote()
        });

    // The remaining operations are built with GitArgumentBuilder directly rather than the Commands
    // factories, whose overloads want ref objects/other state this front-end doesn't track.
    public GitOperationResult DeleteBranch(string branchName, bool force) =>
        Run($"Delete branch {branchName}", new GitArgumentBuilder("branch")
        {
            force ? "-D" : "-d",
            branchName.Quote()
        });

    public GitOperationResult MergeBranch(string branchName) =>
        Run($"Merge {branchName}", new GitArgumentBuilder("merge")
        {
            branchName.Quote()
        });

    public GitOperationResult StashSave(string message) =>
        Run("Stash", new GitArgumentBuilder("stash")
        {
            "push",
            { !string.IsNullOrWhiteSpace(message), $"-m {message.Quote()}" }
        });

    public GitOperationResult StashList() =>
        Run("Stash list", new GitArgumentBuilder("stash") { "list" });

    public GitOperationResult Fetch() =>
        Run("Fetch", _module.FetchCmd(GetRemote(), remoteBranch: null, localBranch: null));

    public GitOperationResult Pull() =>
        Run("Pull", _module.PullCmd(GetRemote(), remoteBranch: null, rebase: false));

    public GitOperationResult Push(string branch) =>
        Run("Push", Commands.Push(GetRemote(), branch, toBranch: branch, ForcePushOptions.DoNotForce, track: false, recursiveSubmodules: 0));

    /// <summary>
    ///  Stages every change in the working directory and commits it with <paramref name="message"/>.
    ///  Deliberately all-or-nothing: partial staging needs a real staging UI, which this doesn't have
    ///  yet, and silently committing a subset would be worse than not offering it.
    /// </summary>
    public GitOperationResult CommitAll(string message)
    {
        IReadOnlyList<GitItemStatus> changes = _module.GetAllChangedFiles();
        if (changes.Count == 0)
        {
            return new GitOperationResult("Commit", false, "Nothing to commit — the working directory is clean.");
        }

        if (!_module.StageFiles(changes, out string stageOutput))
        {
            return new GitOperationResult("Stage", false, stageOutput);
        }

        return Commit(message, amend: false, signOff: false);
    }

    /// <summary>The message of HEAD, for pre-filling an amend.</summary>
    public string GetLastCommitMessage()
    {
        ExecutionResult result = _module.GitExecutable.Execute(
            new GitArgumentBuilder("log") { "-1", "--pretty=%B" },
            throwOnErrorExit: false);

        return result.ExitedSuccessfully ? result.StandardOutput.TrimEnd() : "";
    }

    /// <summary>
    ///  Commits whatever is currently staged. Staging is the caller's business, which is what makes
    ///  partial commits possible.
    /// </summary>
    public GitOperationResult Commit(string message, bool amend, bool signOff)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return new GitOperationResult("Commit", false, "A commit message is required.");
        }

        // git reads the message from a file so multi-line messages and quoting are git's problem,
        // not ours.
        string messageFile = Path.Combine(Path.GetTempPath(), $"gitextensions-winui-commit-{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllText(messageFile, message, Encoding.UTF8);

            return Run("Commit", Commands.Commit(
                amend: amend,
                signOff: signOff,
                author: "",
                useExplicitCommitMessage: true,
                commitMessageFile: messageFile,
                getPathForGitExecution: _module.GetPathForGitExecution));
        }
        finally
        {
            try
            {
                File.Delete(messageFile);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing the commit over.
            }
        }
    }

    public GitOperationResult Checkout(string branch) =>
        Run($"Checkout {branch}", Commands.Checkout(branch, LocalChangesAction.DontChange));

    /// <summary>
    ///  The remote to fetch/pull/push against — the one configured for the current branch where there
    ///  is one, otherwise the repository's first remote.
    /// </summary>
    private string GetRemote()
    {
        string current = _module.GetCurrentRemote();
        if (!string.IsNullOrEmpty(current))
        {
            return current;
        }

        return _module.GetRemoteNames().FirstOrDefault() ?? "origin";
    }

    private GitOperationResult Run(string description, ArgumentString arguments)
    {
        // throwOnErrorExit: false — a non-zero exit here is information for the user (nothing to
        // fetch, rejected push, dirty worktree), not an exceptional condition.
        ExecutionResult result = _module.GitExecutable.Execute(arguments, throwOnErrorExit: false);
        return new GitOperationResult(description, result.ExitedSuccessfully, result.AllOutput.Trim());
    }

    /// <summary>Collects revisions up to a cap; file history is a dialog, not an endless list.</summary>
    private sealed class FileHistoryObserver : IObserver<IReadOnlyList<GitRevision>>
    {
        private readonly int _maxCount;

        public FileHistoryObserver(int maxCount)
        {
            _maxCount = maxCount;
        }

        public List<GitRevision> Revisions { get; } = [];

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(IReadOnlyList<GitRevision> value)
        {
            foreach (GitRevision revision in value)
            {
                if (Revisions.Count >= _maxCount)
                {
                    return;
                }

                Revisions.Add(revision);
            }
        }
    }
}

/// <param name="Description">The operation, for display.</param>
/// <param name="Succeeded">Whether git exited zero.</param>
/// <param name="Output">Combined stdout/stderr — git reports progress on stderr even when it succeeds.</param>
public sealed record GitOperationResult(string Description, bool Succeeded, string Output);
