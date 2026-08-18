using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.WinUI.Models;
using GitExtUtils;

namespace GitExtensions.WinUI.Services;

/// <summary>
///  Reading and resolving merge conflicts.
/// </summary>
internal sealed partial class RepositoryLoader
{
    /// <summary>
    ///  The conflicted files and what kind of conflict each one is.
    /// </summary>
    /// <remarks>
    ///  Read from <c>status --porcelain</c> rather than <c>diff --diff-filter=U</c>, which reports
    ///  the paths but not the shape: it cannot distinguish "both changed it" from "they deleted it",
    ///  and those need entirely different resolutions.
    /// </remarks>
    public IReadOnlyList<ConflictedFile> GetConflicts()
    {
        ExecutionResult result = _module.GitExecutable.Execute(
            new GitArgumentBuilder("status") { "--porcelain", "-z" },
            throwOnErrorExit: false);

        if (!result.ExitedSuccessfully)
        {
            return [];
        }

        List<ConflictedFile> conflicts = [];

        // -z separates records with NUL, so paths containing spaces or quotes need no unquoting.
        foreach (string record in result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (record.Length < 4)
            {
                continue;
            }

            string code = record[..2];
            string path = record[3..].Trim();

            ConflictKind? kind = code switch
            {
                "UU" => ConflictKind.BothModified,
                "AA" => ConflictKind.BothAdded,
                "DU" => ConflictKind.DeletedByUs,
                "UD" => ConflictKind.DeletedByThem,
                "AU" => ConflictKind.AddedByUs,
                "UA" => ConflictKind.AddedByThem,
                "DD" => ConflictKind.BothDeleted,
                _ => null
            };

            if (kind is ConflictKind resolved)
            {
                conflicts.Add(new ConflictedFile(path, resolved));
            }
        }

        return conflicts;
    }

    /// <summary>
    ///  Resolves a conflict by taking one side wholesale, then staging the result.
    /// </summary>
    /// <remarks>
    ///  Staging is what git means by resolved, so the two steps go together — leaving the file
    ///  checked out but unstaged would look resolved on disk while still blocking the operation.
    ///  A file that one side deleted has no version to check out, so it is removed instead.
    /// </remarks>
    public GitOperationResult TakeSide(ConflictedFile file, bool ours)
    {
        bool wantsDeletion =
            (ours && file.Kind is ConflictKind.DeletedByUs or ConflictKind.AddedByThem)
            || (!ours && file.Kind is ConflictKind.DeletedByThem or ConflictKind.AddedByUs);

        string description = $"{(ours ? "Keep ours" : "Keep theirs")}: {file.Path}";

        if (wantsDeletion)
        {
            return Run(description, new GitArgumentBuilder("rm")
            {
                "--force",
                "--",
                file.Path.Quote()
            });
        }

        ExecutionResult checkout = _module.GitExecutable.Execute(
            new GitArgumentBuilder("checkout")
            {
                ours ? "--ours" : "--theirs",
                "--",
                file.Path.Quote()
            },
            throwOnErrorExit: false);

        if (!checkout.ExitedSuccessfully)
        {
            return new GitOperationResult(description, false, checkout.AllOutput.Trim());
        }

        return Run(description, new GitArgumentBuilder("add") { "--", file.Path.Quote() });
    }

    /// <summary>Takes one side for every conflict at once, for a merge you already know the answer to.</summary>
    public GitOperationResult TakeSideForAll(bool ours)
    {
        IReadOnlyList<ConflictedFile> conflicts = GetConflicts();

        if (conflicts.Count == 0)
        {
            return new GitOperationResult("Resolve all", true, "Nothing is conflicted.");
        }

        foreach (ConflictedFile file in conflicts)
        {
            GitOperationResult result = TakeSide(file, ours);

            if (!result.Succeeded)
            {
                return result;
            }
        }

        return new GitOperationResult(
            ours ? "Keep ours" : "Keep theirs",
            true,
            $"Resolved {conflicts.Count} file(s).");
    }

    /// <summary>The name of the configured merge tool, empty when there is none.</summary>
    public string GetMergeToolName() => GetConfigValue(GitConfigScope.Local, "merge.tool") is { Length: > 0 } local
        ? local
        : GetConfigValue(GitConfigScope.Global, "merge.tool");

    /// <summary>
    ///  Runs the configured merge tool for one file.
    /// </summary>
    /// <remarks>
    ///  Blocks until the tool is closed, so callers must run it off the UI thread. <c>--no-prompt</c>
    ///  matters: without it git asks a question on stdin that nothing here can answer, and the process
    ///  would wait for an input that never comes.
    /// </remarks>
    public GitOperationResult LaunchMergeTool(string path)
    {
        string tool = GetMergeToolName();

        if (tool.Length == 0)
        {
            return new GitOperationResult(
                "Merge tool",
                false,
                "No merge tool is configured. Set merge.tool in Git config, for example to vscode or kdiff3.");
        }

        return Run($"Merge tool: {path}", new GitArgumentBuilder("mergetool")
        {
            "--no-prompt",
            $"--tool={tool}",
            "--",
            path.Quote()
        });
    }

    /// <summary>The working-tree content of a conflicted file, markers and all.</summary>
    public string ReadConflictedText(string path)
    {
        try
        {
            string full = Path.Combine(_module.WorkingDir, path);
            return File.Exists(full) ? File.ReadAllText(full) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
