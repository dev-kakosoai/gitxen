using System.Globalization;
using System.Text;
using GitCommands;
using GitCommands.Git;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtensions.WinUI.Models;
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
internal sealed partial class RepositoryLoader
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
    public void StreamRevisions(IObserver<IReadOnlyList<GitRevision>> observer, int skip, int maxCount, RevisionQuery query, CancellationToken cancellationToken)
    {
        // revisionFilter is spliced straight into the `git log` argument list, so the scope, the
        // search terms and --skip all belong here rather than being applied afterwards.
        StringBuilder revisionFilter = new(query.Scope == RevisionScope.AllBranches ? "--all" : "HEAD");

        if (skip > 0)
        {
            revisionFilter.Append(CultureInfo.InvariantCulture, $" --skip={skip}");
        }

        // Without this git walks the whole history and the page is thrown away at the far end: the
        // caller can only stop the stream once a batch has already arrived past its cap, so on this
        // repository a page cost the full 17k-commit walk (477ms) rather than 135ms, and parsed
        // fifteen thousand revisions nobody asked for.
        if (maxCount > 0)
        {
            revisionFilter.Append(CultureInfo.InvariantCulture, $" --max-count={maxCount}");
        }

        // -S is a content search: it matches commits that changed the number of occurrences of the
        // text, which is what "find where this was introduced" actually means. --author and the path
        // filter are ordinary log filters.
        if (!string.IsNullOrWhiteSpace(query.ContainingText))
        {
            revisionFilter.Append(CultureInfo.InvariantCulture, $" -S{query.ContainingText.Quote()} --pickaxe-regex");
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            revisionFilter.Append(CultureInfo.InvariantCulture, $" --author={query.Author.Quote()}");
        }

        if (!string.IsNullOrWhiteSpace(query.MessageContains))
        {
            revisionFilter.Append(CultureInfo.InvariantCulture, $" --grep={query.MessageContains.Quote()} --regexp-ignore-case");
        }

        // allBodies: the Advanced-mode details pane shows the full commit message, not just the subject.
        new RevisionReader(_module, allBodies: true).GetLog(
            observer,
            revisionFilter.ToString(),
            pathFilter: string.IsNullOrWhiteSpace(query.Path) ? "" : query.Path.ToPosixPath().Quote(),
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

    /// <summary>Stages everything, including untracked files, which is what the Changes page offers.</summary>
    public GitOperationResult StageAll()
    {
        IReadOnlyList<GitItemStatus> changes = _module.GetAllChangedFiles();
        IReadOnlyList<GitItemStatus> unstaged = [.. changes.Where(file => file.Staged != StagedStatus.Index)];

        if (unstaged.Count == 0)
        {
            return new GitOperationResult("Stage all", true, "Nothing to stage.");
        }

        return _module.StageFiles(unstaged, out string output)
            ? new GitOperationResult("Stage all", true, $"Staged {unstaged.Count} file(s).")
            : new GitOperationResult("Stage all", false, output);
    }

    public GitOperationResult UnstageAll() =>
        Run("Unstage all", Commands.Reset(ResetMode.ResetIndex, "HEAD"));

    /// <summary>
    ///  Applies a patch to the index, forwards to stage it or reversed to unstage it.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   This is how partial staging works: git has no command that stages part of a file, so a patch
    ///   containing just the wanted hunk is applied to the index with <c>--cached</c>. The working
    ///   tree is untouched either way — only what is staged changes.
    ///  </para>
    ///  <para>
    ///   The patch goes through a temp file rather than stdin so that its exact bytes, including the
    ///   trailing newline git insists on, reach the command unmodified.
    ///  </para>
    /// </remarks>
    public GitOperationResult ApplyToIndex(string patch, bool reverse, string description)
    {
        string patchFile = Path.Combine(Path.GetTempPath(), $"gitextensions-winui-hunk-{Guid.NewGuid():N}.patch");

        try
        {
            // Written as UTF-8 without a BOM: git treats a BOM as file content and the patch stops
            // matching.
            File.WriteAllText(patchFile, patch, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            return Run(description, new GitArgumentBuilder("apply")
            {
                "--cached",
                { reverse, "--reverse" },

                // Hunks taken out of a larger diff carry the surrounding context but not always the
                // whitespace git would prefer; without this a stage can fail on an unrelated line.
                "--whitespace=nowarn",
                "--",
                patchFile.Quote()
            });
        }
        catch (IOException ex)
        {
            return new GitOperationResult(description, false, ex.Message);
        }
        finally
        {
            try
            {
                File.Delete(patchFile);
            }
            catch (IOException)
            {
                // A leftover temp patch is not worth failing the operation over.
            }
        }
    }

    /// <summary>
    ///  Throws away a file's uncommitted changes. An untracked file has nothing to restore it from, so
    ///  discarding it means deleting it — a different command, and the caller must have confirmed.
    /// </summary>
    public GitOperationResult DiscardFile(string fileName, bool isUntracked) =>
        isUntracked
            ? Run($"Delete {fileName}", new GitArgumentBuilder("clean") { "-f", "--", fileName.Quote() })
            : Run($"Discard {fileName}", new GitArgumentBuilder("checkout") { "--", fileName.Quote() });

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

    public GitOperationResult StashPop() =>
        Run("Stash pop", new GitArgumentBuilder("stash") { "pop" });

    /// <summary>
    ///  Stash entries are addressed by reflog selector (<c>stash@{1}</c>), which is why the pages pass
    ///  a reference through rather than an index — the selector is what git accepts.
    /// </summary>
    public GitOperationResult StashApply(string reference) =>
        Run($"Apply {reference}", new GitArgumentBuilder("stash") { "apply", reference.Quote() });

    public GitOperationResult StashPop(string reference) =>
        Run($"Pop {reference}", new GitArgumentBuilder("stash") { "pop", reference.Quote() });

    public GitOperationResult StashDrop(string reference) =>
        Run($"Drop {reference}", new GitArgumentBuilder("stash") { "drop", reference.Quote() });

    public GitOperationResult RenameBranch(string oldName, string newName) =>
        Run($"Rename {oldName}", new GitArgumentBuilder("branch") { "-m", oldName.Quote(), newName.Quote() });

    /// <summary>Points a local branch at an upstream, for a branch created before its remote existed.</summary>
    public GitOperationResult SetUpstream(string branch, string upstream) =>
        Run($"Track {upstream}", new GitArgumentBuilder("branch")
        {
            $"--set-upstream-to={upstream}",
            branch.Quote()
        });

    /// <summary>Fetches one named remote, rather than whichever one the current branch tracks.</summary>
    public GitOperationResult FetchRemote(string remote, bool prune) =>
        Run($"Fetch {remote}", new GitArgumentBuilder("fetch")
        {
            { prune, "--prune" },
            "--tags",
            remote.Quote()
        });

    /// <summary>Updates one submodule, for when re-initialising every one of them is overkill.</summary>
    public GitOperationResult UpdateSubmodule(string path) =>
        Run($"Update {path}", new GitArgumentBuilder("submodule")
        {
            "update",
            "--init",
            "--recursive",
            "--",
            path.Quote()
        });

    /// <summary>Drops worktree administrative entries whose directories are gone.</summary>
    public GitOperationResult PruneWorktrees() =>
        Run("Prune worktrees", new GitArgumentBuilder("worktree") { "prune" });

    /// <summary>Checks out a tag or commit, which necessarily leaves HEAD detached.</summary>
    public GitOperationResult CheckoutDetached(string reference) =>
        Run($"Checkout {reference}", new GitArgumentBuilder("checkout") { "--detach", reference.Quote() });

    // ---- History operations -------------------------------------------------------------------
    // These are all built with GitArgumentBuilder rather than the Commands factories: the factories
    // take option structs and enums this front-end doesn't model, and the raw arguments are plain
    // enough to be obviously correct.

    public GitOperationResult CherryPick(ObjectId commit) =>
        Run($"Cherry-pick {commit.ToShortString()}", new GitArgumentBuilder("cherry-pick") { commit.ToString() });

    public GitOperationResult Revert(ObjectId commit) =>
        Run($"Revert {commit.ToShortString()}", new GitArgumentBuilder("revert") { "--no-edit", commit.ToString() });

    public GitOperationResult Rebase(string onto) =>
        Run($"Rebase onto {onto}", new GitArgumentBuilder("rebase") { onto.Quote() });

    public GitOperationResult ResetTo(ObjectId commit, ResetMode mode) =>
        Run($"Reset ({mode}) to {commit.ToShortString()}", Commands.Reset(mode, commit.ToString()));

    /// <summary>
    ///  Reset to anything git can resolve, including a reflog selector such as <c>HEAD@{2}</c>, which
    ///  is not an <see cref="ObjectId"/> and so cannot go through the overload above.
    /// </summary>
    public GitOperationResult ResetTo(string reference, ResetMode mode) =>
        Run($"Reset ({mode}) to {reference}", Commands.Reset(mode, reference));

    /// <summary>Creates a branch at any resolvable reference, again for reflog selectors.</summary>
    public GitOperationResult CreateBranchAt(string branchName, string reference, bool checkout) =>
        Run($"Create branch {branchName}", new GitArgumentBuilder(checkout ? "checkout" : "branch")
        {
            { checkout, "-b" },
            branchName.Quote(),
            reference.Quote()
        });

    /// <summary>
    ///  Continue/abort/skip for whichever operation is in progress. The command differs per
    ///  operation, so the caller says which one it means.
    /// </summary>
    public GitOperationResult ContinueOperation(string operation, string action) =>
        Run($"{operation} --{action}", new GitArgumentBuilder(operation) { $"--{action}" });

    /// <summary>Files with unresolved merge conflicts.</summary>
    public IReadOnlyList<string> GetConflictedFiles()
    {
        ExecutionResult result = _module.GitExecutable.Execute(
            new GitArgumentBuilder("diff") { "--name-only", "--diff-filter=U" },
            throwOnErrorExit: false);

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    /// <summary>Marks a conflict resolved by staging the file, which is what git means by resolved.</summary>
    public GitOperationResult MarkResolved(string fileName)
    {
        _module.StageFile(fileName);
        return new GitOperationResult("Resolve", true, $"Marked {fileName} resolved.");
    }

    public GitOperationResult Bisect(string action, ObjectId? commit = null) =>
        Run($"Bisect {action}", new GitArgumentBuilder("bisect")
        {
            action,
            { commit is not null, commit?.ToString() ?? "" }
        });

    // ---- Tags, remotes, submodules, worktrees --------------------------------------------------

    public GitOperationResult ListTags() =>
        Run("Tags", new GitArgumentBuilder("tag") { "--list", "--sort=-creatordate" });

    public GitOperationResult CreateTag(string name, ObjectId? commit, string message) =>
        Run($"Create tag {name}", new GitArgumentBuilder("tag")
        {
            { !string.IsNullOrWhiteSpace(message), $"-a -m {message.Quote()}" },
            name.Quote(),
            { commit is not null, commit?.ToString() ?? "" }
        });

    public GitOperationResult DeleteTag(string name) =>
        Run($"Delete tag {name}", new GitArgumentBuilder("tag") { "-d", name.Quote() });

    public GitOperationResult PushTag(string name) =>
        Run($"Push tag {name}", new GitArgumentBuilder("push") { GetRemote(), $"refs/tags/{name}".Quote() });

    public GitOperationResult ListRemotes() =>
        Run("Remotes", new GitArgumentBuilder("remote") { "-v" });

    public GitOperationResult AddRemote(string name, string url) =>
        Run($"Add remote {name}", new GitArgumentBuilder("remote") { "add", name.Quote(), url.Quote() });

    public GitOperationResult RemoveRemote(string name) =>
        Run($"Remove remote {name}", new GitArgumentBuilder("remote") { "remove", name.Quote() });

    public GitOperationResult ListSubmodules() =>
        Run("Submodules", new GitArgumentBuilder("submodule") { "status", "--recursive" });

    public GitOperationResult UpdateSubmodules() =>
        Run("Update submodules", new GitArgumentBuilder("submodule") { "update", "--init", "--recursive" });

    public GitOperationResult SyncSubmodules() =>
        Run("Sync submodules", new GitArgumentBuilder("submodule") { "sync", "--recursive" });

    public GitOperationResult ListWorktrees() =>
        Run("Worktrees", new GitArgumentBuilder("worktree") { "list" });

    public GitOperationResult AddWorktree(string path, string branch) =>
        Run($"Add worktree {path}", new GitArgumentBuilder("worktree") { "add", path.Quote(), branch.Quote() });

    public GitOperationResult RemoveWorktree(string path) =>
        Run($"Remove worktree {path}", new GitArgumentBuilder("worktree") { "remove", path.Quote() });

    public GitOperationResult Fetch() =>
        Run("Fetch", _module.FetchCmd(GetRemote(), remoteBranch: null, localBranch: null));

    public GitOperationResult Pull() =>
        Run("Pull", _module.PullCmd(GetRemote(), remoteBranch: null, rebase: false));

    /// <summary>
    ///  Pull with the choices the plain one does not offer: rebase instead of merge, and pruning
    ///  remote-tracking branches that no longer exist.
    /// </summary>
    public GitOperationResult Pull(PullOptions options)
    {
        string remote = string.IsNullOrWhiteSpace(options.Remote) ? GetRemote() : options.Remote;

        return Run($"Pull {remote}", new GitArgumentBuilder("pull")
        {
            { options.Rebase, "--rebase" },
            { options.Prune, "--prune" },
            { options.FastForwardOnly && !options.Rebase, "--ff-only" },
            remote.Quote(),
            { !string.IsNullOrWhiteSpace(options.RemoteBranch), options.RemoteBranch.Quote() }
        });
    }

    public GitOperationResult Push(string branch) =>
        Run("Push", Commands.Push(GetRemote(), branch, toBranch: branch, ForcePushOptions.DoNotForce, track: false, recursiveSubmodules: 0));

    /// <summary>
    ///  Push with the options that make it usable on a real branch: force-with-lease, tags, and
    ///  setting the upstream for a branch that has never been pushed.
    /// </summary>
    /// <remarks>
    ///  Force is always <c>--force-with-lease</c>, never a bare <c>--force</c>: the lease refuses the
    ///  push if the remote moved since it was last fetched, which is the difference between rewriting
    ///  your own history and discarding someone else's.
    /// </remarks>
    public GitOperationResult Push(PushOptions options)
    {
        string remote = string.IsNullOrWhiteSpace(options.Remote) ? GetRemote() : options.Remote;
        string target = string.IsNullOrWhiteSpace(options.RemoteBranch)
            ? options.Branch
            : $"{options.Branch}:{options.RemoteBranch}";

        return Run($"Push to {remote}", new GitArgumentBuilder("push")
        {
            { options.ForceWithLease, "--force-with-lease" },
            { options.SetUpstream, "--set-upstream" },
            { options.PushAllTags, "--tags" },
            remote.Quote(),
            { !options.PushAllTags || !string.IsNullOrWhiteSpace(options.Branch), target.Quote() }
        });
    }

    /// <summary>Merge with the strategy choices: keep the merge commit, or fold the work in flat.</summary>
    public GitOperationResult MergeBranch(string branchName, MergeOptions options) =>
        Run($"Merge {branchName}", new GitArgumentBuilder("merge")
        {
            { options.NoFastForward && !options.Squash, "--no-ff" },
            { options.Squash, "--squash" },
            { options.NoCommit && !options.Squash, "--no-commit" },
            { !string.IsNullOrWhiteSpace(options.Message), $"-m {options.Message.Quote()}" },
            branchName.Quote()
        });

    /// <summary>
    ///  Deletes a branch on the remote by pushing an empty ref to it. Nothing local changes.
    /// </summary>
    public GitOperationResult DeleteRemoteBranch(string remote, string branch) =>
        Run($"Delete {remote}/{branch}", new GitArgumentBuilder("push")
        {
            remote.Quote(),
            "--delete",
            branch.Quote()
        });

    /// <summary>
    ///  Checks a branch out, deciding what to do with uncommitted changes rather than always refusing.
    /// </summary>
    public GitOperationResult Checkout(string branch, LocalChangesAction localChanges) =>
        Run($"Checkout {branch}", Commands.Checkout(branch, localChanges));

    /// <summary>Stash with the options that decide what actually gets set aside.</summary>
    public GitOperationResult StashSave(string message, bool includeUntracked, bool keepIndex) =>
        Run("Stash", new GitArgumentBuilder("stash")
        {
            "push",
            { includeUntracked, "--include-untracked" },
            { keepIndex, "--keep-index" },
            { !string.IsNullOrWhiteSpace(message), $"-m {message.Quote()}" }
        });

    /// <summary>The diff a stash entry would apply, for reading before deciding to apply it.</summary>
    public string GetStashDiff(string reference)
    {
        ExecutionResult result = _module.GitExecutable.Execute(
            new GitArgumentBuilder("stash") { "show", "--patch", reference.Quote() },
            throwOnErrorExit: false);

        return result.ExitedSuccessfully ? result.StandardOutput : result.AllOutput;
    }

    /// <summary>
    ///  Removes untracked files. Directories and ignored files are opt-in because <c>git clean</c>
    ///  deletes without a reflog to recover from.
    /// </summary>
    public GitOperationResult Clean(bool includeDirectories, bool includeIgnored, bool dryRun) =>
        Run(dryRun ? "Clean (preview)" : "Clean", new GitArgumentBuilder("clean")
        {
            dryRun ? "--dry-run" : "--force",
            { includeDirectories, "-d" },
            { includeIgnored, "-x" }
        });

    /// <summary>Repacks and prunes; the housekeeping <c>FormCleanupRepository</c> offers.</summary>
    public GitOperationResult CollectGarbage() =>
        Run("Garbage collect", new GitArgumentBuilder("gc") { "--auto" });

    /// <summary>Writes the working tree of a revision to an archive.</summary>
    public GitOperationResult Archive(string reference, string outputPath) =>
        Run($"Archive {reference}", new GitArgumentBuilder("archive")
        {
            $"--output={outputPath.Quote()}",
            reference.Quote()
        });

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
    ///  Undoes the last commit, keeping its changes staged.
    /// </summary>
    /// <remarks>
    ///  A soft reset, so nothing is lost: the files stay exactly as they were and the commit's own
    ///  message can be recovered from the reflog. This is the "I committed too early" button, not a
    ///  way to discard work.
    /// </remarks>
    public GitOperationResult UndoLastCommit() =>
        Run("Undo last commit", Commands.Reset(ResetMode.Soft, "HEAD~1"));

    /// <summary>The files that differ between two commits, for comparing them directly.</summary>
    public IReadOnlyList<GitItemStatus> GetChangedFilesBetween(ObjectId first, ObjectId second, CancellationToken cancellationToken) =>
        _module.GetDiffFilesWithUntracked(first.ToString(), second.ToString(), StagedStatus.None, cancellationToken: cancellationToken);

    /// <summary>One file's diff between two arbitrary commits.</summary>
    public async Task<string> GetDiffTextBetweenAsync(ObjectId first, ObjectId second, string fileName, string? oldFileName, CancellationToken cancellationToken)
    {
        (Patch? patch, string? errorMessage) = await _module.GetSingleDiffAsync(
            first,
            second,
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
