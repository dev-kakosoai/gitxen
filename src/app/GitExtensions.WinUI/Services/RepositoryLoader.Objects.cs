using System.Globalization;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.WinUI.Models;
using GitExtUtils;

namespace GitExtensions.WinUI.Services;

/// <summary>
///  The structured reads behind the Branches/Remotes/Tags/Stashes/Submodules/Worktrees pages.
/// </summary>
/// <remarks>
///  Split from the main <see cref="RepositoryLoader"/> file because it is all one concern — turning
///  git's machine-readable listings into records — and the operations file was already long.
///  Everything here uses a documented plumbing format (<c>for-each-ref</c>'s <c>--format</c>,
///  <c>worktree list --porcelain</c>) rather than the human-facing output, which git does not
///  guarantee between versions.
/// </remarks>
internal sealed partial class RepositoryLoader
{
    /// <summary>
    ///  Separates fields in the output of a <c>--format</c> command; chosen because git refs cannot
    ///  contain a tab.
    /// </summary>
    private const char FieldSeparator = '\t';

    /// <summary>
    ///  How the separator is written inside a <c>for-each-ref --format</c> argument.
    /// </summary>
    /// <remarks>
    ///  git's own escape, not a literal tab. <see cref="ArgumentBuilder"/> composes the whole argument
    ///  list into a single command line, and Windows splits a command line on tabs exactly as it does
    ///  on spaces — a literal tab here would break the format string into several arguments and the
    ///  command would fail. git expands the escape itself, well after any command-line parsing.
    /// </remarks>
    private const string RefSeparatorEscape = "%09";

    /// <summary>The same separator for commands that take a <c>git log</c> pretty format.</summary>
    private const string LogSeparatorEscape = "%x09";

    /// <summary>
    ///  Local branches, most recently committed to first, with upstream tracking state.
    /// </summary>
    public IReadOnlyList<BranchInfo> GetBranches()
    {
        string current = GetCurrentBranch();

        return ReadRecords(
            new GitArgumentBuilder("for-each-ref")
            {
                "--sort=-committerdate",
                $"--format=%(refname:short){RefSeparatorEscape}%(upstream:short){RefSeparatorEscape}"
                    + $"%(upstream:track){RefSeparatorEscape}%(objectname:short){RefSeparatorEscape}"
                    + $"%(committerdate:short){RefSeparatorEscape}%(contents:subject)",
                "refs/heads"
            },
            fieldCount: 6,
            fields =>
            {
                (int ahead, int behind) = ParseTracking(fields[2]);

                return new BranchInfo(
                    Name: fields[0],
                    Upstream: fields[1],
                    Ahead: ahead,
                    Behind: behind,
                    ShortHash: fields[3],
                    Date: fields[4],
                    Subject: fields[5],
                    IsCurrent: string.Equals(fields[0], current, StringComparison.Ordinal));
            });
    }

    /// <summary>
    ///  Remotes with their fetch and push URLs. <c>git remote -v</c> emits one line per direction, so
    ///  the two lines for a remote are folded into a single record.
    /// </summary>
    public IReadOnlyList<RemoteInfo> GetRemotes()
    {
        Dictionary<string, (string Fetch, string Push)> urls = [];
        List<string> order = [];

        foreach (string line in ReadLines(new GitArgumentBuilder("remote") { "-v" }))
        {
            // "origin\thttps://example/repo.git (fetch)"
            string[] parts = line.Split([FieldSeparator, ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            string name = parts[0];
            string url = parts[1];
            bool isPush = parts[2].Contains("push", StringComparison.OrdinalIgnoreCase);

            if (!urls.TryGetValue(name, out (string Fetch, string Push) existing))
            {
                order.Add(name);
                existing = ("", "");
            }

            urls[name] = isPush ? (existing.Fetch, url) : (url, existing.Push);
        }

        return order.Select(name => new RemoteInfo(name, urls[name].Fetch, urls[name].Push)).ToList();
    }

    /// <summary>
    ///  Remote-tracking branches, grouped under the remote they came from.
    /// </summary>
    /// <remarks>
    ///  <c>refs/remotes/&lt;remote&gt;/HEAD</c> is skipped: it is a symbolic pointer at the remote's
    ///  default branch, not a branch of its own, and listing it duplicates whichever branch it names.
    /// </remarks>
    public IReadOnlyList<RemoteBranchInfo> GetRemoteBranches()
    {
        List<RemoteBranchInfo> branches = ReadRecords(
            new GitArgumentBuilder("for-each-ref")
            {
                "--sort=-committerdate",
                $"--format=%(refname:short){RefSeparatorEscape}%(objectname:short){RefSeparatorEscape}"
                    + $"%(committerdate:short){RefSeparatorEscape}%(contents:subject){RefSeparatorEscape}%(symref)",
                "refs/remotes"
            },
            fieldCount: 5,
            fields =>
            {
                // "origin/feature/x" splits into the remote and everything after it.
                string full = fields[0];
                int slash = full.IndexOf('/');
                string remote = slash > 0 ? full[..slash] : "";
                string shortName = slash > 0 ? full[(slash + 1)..] : full;

                return new
                {
                    Info = new RemoteBranchInfo(full, remote, shortName, fields[1], fields[2], fields[3]),
                    IsSymbolic = !string.IsNullOrEmpty(fields[4])
                };
            })
            .Where(entry => !entry.IsSymbolic && entry.Info.ShortName != "HEAD")
            .Select(entry => entry.Info)
            .ToList();

        return branches;
    }

    /// <summary>
    ///  The reflog for HEAD, newest first — every position HEAD has held, including ones no branch
    ///  points at any more.
    /// </summary>
    public IReadOnlyList<ReflogEntry> GetReflog(int maxCount) =>
        ReadRecords(
            new GitArgumentBuilder("reflog")
            {
                $"--max-count={maxCount}",
                $"--format=%gd{LogSeparatorEscape}%h{LogSeparatorEscape}%gs{LogSeparatorEscape}%s{LogSeparatorEscape}%ar"
            },
            fieldCount: 5,
            fields => new ReflogEntry(
                Selector: fields[0],
                ShortHash: fields[1],
                Action: SplitAction(fields[2]),
                Subject: fields[3],
                Date: fields[4]));

    /// <summary>
    ///  Takes the operation out of a reflog message. <c>%gs</c> is the whole message, e.g.
    ///  "commit: fix the thing"; the leading verb is what identifies what moved the ref.
    /// </summary>
    private static string SplitAction(string reflogMessage)
    {
        int colon = reflogMessage.IndexOf(':');
        return colon > 0 ? reflogMessage[..colon].Trim() : reflogMessage.Trim();
    }

    /// <summary>Tags, newest first. Lightweight and annotated tags are distinguished by object type.</summary>
    public IReadOnlyList<TagInfo> GetTags() =>
        ReadRecords(
            new GitArgumentBuilder("for-each-ref")
            {
                "--sort=-creatordate",
                $"--format=%(refname:short){RefSeparatorEscape}%(objectname:short){RefSeparatorEscape}"
                    + $"%(creatordate:short){RefSeparatorEscape}%(contents:subject){RefSeparatorEscape}%(objecttype)",
                "refs/tags"
            },
            fieldCount: 5,
            fields => new TagInfo(
                Name: fields[0],
                ShortHash: fields[1],
                Date: fields[2],
                Subject: fields[3],
                IsAnnotated: string.Equals(fields[4], "tag", StringComparison.Ordinal)));

    /// <summary>
    ///  Every path git tracks, relative to the repository root.
    /// </summary>
    /// <remarks>
    ///  Read live for the go-to palette rather than kept anywhere: ls-files reads the index without
    ///  touching the worktree, so even this repository's ~4,000 paths come back in tens of
    ///  milliseconds, and a listing that is never stale beats an index that must be invalidated.
    /// </remarks>
    public IReadOnlyList<string> GetTrackedFiles() =>
        ReadLines(new GitArgumentBuilder("ls-files"));

    /// <summary>The stash stack, most recent first.</summary>
    public IReadOnlyList<StashInfo> GetStashes() =>
        ReadRecords(
            new GitArgumentBuilder("stash")
            {
                "list",
                $"--format=%gd{LogSeparatorEscape}%gs{LogSeparatorEscape}%h"
            },
            fieldCount: 3,
            fields => new StashInfo(Reference: fields[0], Subject: fields[1], ShortHash: fields[2]));

    /// <summary>
    ///  Submodules with their checkout state. The leading marker is the whole point of
    ///  <c>submodule status</c>, so it is parsed rather than trimmed away.
    /// </summary>
    public IReadOnlyList<SubmoduleInfo> GetSubmodules()
    {
        List<SubmoduleInfo> submodules = [];

        foreach (string line in ReadLines(new GitArgumentBuilder("submodule") { "status", "--recursive" }, trimEntries: false))
        {
            if (line.Length < 2)
            {
                continue;
            }

            SubmoduleState state = line[0] switch
            {
                '+' => SubmoduleState.Modified,
                '-' => SubmoduleState.NotInitialized,
                'U' => SubmoduleState.Conflicted,
                _ => SubmoduleState.UpToDate
            };

            // "<marker><sha> <path> (<describe>)" — the marker occupies the first column even when blank.
            string[] parts = line[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            string described = parts.Length > 2 ? string.Join(' ', parts[2..]).Trim('(', ')') : "";

            submodules.Add(new SubmoduleInfo(
                Path: parts[1],
                ShortHash: Shorten(parts[0]),
                Described: described,
                State: state));
        }

        return submodules;
    }

    /// <summary>
    ///  Worktrees, main one first. <c>--porcelain</c> emits a blank-line-separated block per worktree
    ///  rather than one line each, so this accumulates a block at a time.
    /// </summary>
    public IReadOnlyList<WorktreeInfo> GetWorktrees()
    {
        List<WorktreeInfo> worktrees = [];
        string path = "";
        string head = "";
        string branch = "";
        bool bare = false;
        bool locked = false;

        void Flush()
        {
            if (path.Length == 0)
            {
                return;
            }

            // The first block git emits is always the main worktree.
            worktrees.Add(new WorktreeInfo(path, Shorten(head), branch, bare, locked, IsMain: worktrees.Count == 0));
            path = head = branch = "";
            bare = locked = false;
        }

        foreach (string line in ReadLines(new GitArgumentBuilder("worktree") { "list", "--porcelain" }, keepBlankLines: true))
        {
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (TryReadPrefixed(line, "worktree ", out string value))
            {
                path = value;
            }
            else if (TryReadPrefixed(line, "HEAD ", out value))
            {
                head = value;
            }
            else if (TryReadPrefixed(line, "branch ", out value))
            {
                // Reported as a full ref (refs/heads/main); the short name is what belongs on screen.
                branch = value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value;
            }
            else if (line.StartsWith("bare", StringComparison.Ordinal))
            {
                bare = true;
            }
            else if (line.StartsWith("locked", StringComparison.Ordinal))
            {
                locked = true;
            }
        }

        // The final block is not followed by a blank line.
        Flush();
        return worktrees;
    }

    /// <summary>
    ///  Every ref in the repository grouped by the commit it points at, so the history rows can show
    ///  branch and tag badges. Read in one <c>git</c> call rather than per row.
    /// </summary>
    public IReadOnlyDictionary<string, List<RefBadge>> GetRefsByCommit()
    {
        Dictionary<string, List<RefBadge>> byCommit = [];

        IEnumerable<string[]> records = ReadRecords(
            new GitArgumentBuilder("for-each-ref")
            {
                $"--format=%(objectname){RefSeparatorEscape}%(refname){RefSeparatorEscape}%(HEAD)",
                "refs/heads",
                "refs/remotes",
                "refs/tags"
            },
            fieldCount: 3,
            fields => fields);

        foreach (string[] fields in records)
        {
            string refName = fields[1];
            bool isHead = string.Equals(fields[2], "*", StringComparison.Ordinal);

            RefBadgeKind kind =
                refName.StartsWith("refs/tags/", StringComparison.Ordinal) ? RefBadgeKind.Tag
                : refName.StartsWith("refs/remotes/", StringComparison.Ordinal) ? RefBadgeKind.Remote
                : RefBadgeKind.LocalBranch;

            if (!byCommit.TryGetValue(fields[0], out List<RefBadge>? badges))
            {
                badges = [];
                byCommit[fields[0]] = badges;
            }

            badges.Add(new RefBadge(ShortenRefName(refName), kind, isHead));
        }

        // HEAD first, then local branches, then remotes, then tags — the order GitExtensions uses.
        foreach (List<RefBadge> badges in byCommit.Values)
        {
            badges.Sort(static (left, right) => left.SortKey.CompareTo(right.SortKey));
        }

        return byCommit;
    }

    /// <summary>Ahead/behind counts parsed out of <c>%(upstream:track)</c>, e.g. "[ahead 2, behind 1]".</summary>
    private static (int Ahead, int Behind) ParseTracking(string track)
    {
        if (string.IsNullOrEmpty(track))
        {
            return (0, 0);
        }

        return (ReadCount("ahead "), ReadCount("behind "));

        int ReadCount(string keyword)
        {
            int start = track.IndexOf(keyword, StringComparison.Ordinal);
            if (start < 0)
            {
                return 0;
            }

            start += keyword.Length;
            int end = start;
            while (end < track.Length && char.IsAsciiDigit(track[end]))
            {
                end++;
            }

            return int.TryParse(track[start..end], NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : 0;
        }
    }

    private static string ShortenRefName(string refName)
    {
        foreach (string prefix in (string[])["refs/heads/", "refs/remotes/", "refs/tags/"])
        {
            if (refName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return refName[prefix.Length..];
            }
        }

        return refName;
    }

    private static string Shorten(string hash) => hash.Length > 8 ? hash[..8] : hash;

    private static bool TryReadPrefixed(string line, string prefix, out string value)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = line[prefix.Length..].Trim();
            return true;
        }

        value = "";
        return false;
    }

    /// <summary>
    ///  Runs a tab-separated <c>--format</c> command and projects each line.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   Short lines are padded to <paramref name="fieldCount"/> rather than skipped. Trailing fields
    ///   are routinely empty — a branch with no upstream, a ref that is not symbolic, a commit with no
    ///   subject — and the trailing separators that would mark them are removed before this sees the
    ///   line, so requiring a full set of fields silently drops exactly those rows.
    ///  </para>
    ///  <para>
    ///   A completely empty line yields nothing, which is the one case that really is malformed.
    ///  </para>
    /// </remarks>
    private List<T> ReadRecords<T>(ArgumentString arguments, int fieldCount, Func<string[], T> project)
    {
        List<T> records = [];

        foreach (string line in ReadLines(arguments))
        {
            // The last field can itself contain the separator (a commit subject may), so limit the split.
            string[] fields = line.Split(FieldSeparator, fieldCount);

            if (fields.Length == 0 || fields[0].Length == 0)
            {
                continue;
            }

            if (fields.Length < fieldCount)
            {
                string[] padded = new string[fieldCount];
                fields.CopyTo(padded, 0);

                for (int i = fields.Length; i < fieldCount; i++)
                {
                    padded[i] = "";
                }

                fields = padded;
            }

            records.Add(project(fields));
        }

        return records;
    }

    /// <summary>
    ///  Runs a read-only git command and returns its standard output as lines. Failure yields no lines
    ///  rather than throwing — an empty page is the right answer for a repository with no remotes.
    /// </summary>
    private List<string> ReadLines(ArgumentString arguments, bool trimEntries = true, bool keepBlankLines = false)
    {
        ExecutionResult result = _module.GitExecutable.Execute(arguments, throwOnErrorExit: false);

        if (!result.ExitedSuccessfully)
        {
            return [];
        }

        StringSplitOptions options = keepBlankLines ? StringSplitOptions.None : StringSplitOptions.RemoveEmptyEntries;
        if (trimEntries)
        {
            options |= StringSplitOptions.TrimEntries;
        }

        return [.. result.StandardOutput.Split('\n', options)];
    }
}
