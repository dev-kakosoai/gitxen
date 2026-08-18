# Gitxen

Gitxen is a free, open-source git client for Windows, built with WinUI 3.

It is a **derivative work of [Git Extensions](https://github.com/gitextensions/gitextensions)** and is
distributed under the same licence, **GPL-3.0** — see [LICENSE.md](../../../LICENSE.md) and
[NOTICE.md](../../../NOTICE.md). Copyright in the original work remains with the Git Extensions
authors; Gitxen is not endorsed by or affiliated with that project.

It reuses the Git Extensions engine as-is (`GitCommands`, `GitExtUtils`,
`GitExtensions.Extensibility` — deliberately no `GitUI` dependency) and is built alongside the
original WinForms application rather than replacing it. It is excluded from the installer and the
publish pipeline.

The project directory, assembly and namespaces are still named `GitExtensions.WinUI`. Renaming those
is a mechanical but wide-reaching change, kept separate from the product rename so that the two do not
land in one commit.

## Building and running

```
dotnet build src/app/GitExtensions.WinUI/GitExtensions.WinUI.csproj -c Debug -p:Platform=x64
```

x64 only; the rest of the solution is AnyCPU. Needs a real Windows SDK install, because the native
WinUI toolchain (`cswinrt.exe`, PRI tasks) reads the `KitsRoot10` registry value.

The output is `artifacts/Debug/bin/GitExtensions.WinUI/x64/net10.0-windows10.0.19041.0/GitExtensions.WinUI.exe`.

**Building is not proof it runs.** The csproj carries several workarounds for genuine .NET 10 SDK /
Windows App SDK version-resolution defects, each commented in place. Some of those failures only
appear at startup, as `FileNotFoundException` on `WinRT.Runtime` or `Microsoft.Windows.SDK.NET`.
Always launch the app after touching the csproj.

## Shape of the code

```
MainWindow            shell: custom title bar, repo tab strip, empty state
└── RepositoryView    one repository: nav pane, command bar, InfoBar for results
    └── Views/*View   one page per section, all deriving from RepositoryPage
        └── DiffPanel shared diff reader (unified / side-by-side)
```

| Layer | What lives there |
| --- | --- |
| `Views/` | One control per navigation section. `RepositoryPage` is the base: it carries the `Tab` dependency property and the dialog helpers. |
| `ViewModels/` | `MainViewModel` (open tabs), `RepositoryTabViewModel` (one repository), `WorkingDirectoryViewModel` (staging), `DiffViewModel` (one diff panel). |
| `Services/` | `RepositoryLoader` — every git command for an existing repository, split across two files (operations, and `.Objects.cs` for the structured listings). `RepositoryCreator` — clone and init, which run in a parent directory. |
| `Models/` | Records the pages bind to: `BranchInfo`, `RemoteInfo`, `TagInfo`, `StashInfo`, `SubmoduleInfo`, `WorktreeInfo`, `ReflogEntry`, plus `RevisionQuery` and the operation option records. |
| `Diff/` | `DiffParser` (raw diff to rows), `HunkSplitter` (raw diff to applicable patches), `SyntaxHighlighter`. |

The tab strip is a `TabView` with no content: the repository view is hosted in its own grid row
instead, because `TabView` arranges its content at the content's desired size rather than filling the
space, which left short pages floating in a fraction of the window.

Sections are switched by visibility rather than navigated to in a `Frame`. A `Frame` recreates its
page on every visit, which would discard the commit list's scroll position and any half-typed commit
message.

## Conventions worth knowing

**Every git command goes through `RepositoryLoader`,** built with `GitArgumentBuilder` or the
`Commands` factories, and run with `throwOnErrorExit: false` — a non-zero exit is information for the
user (nothing to fetch, a rejected push, a dirty worktree), not an exceptional condition. Operations
return a `GitOperationResult` and are surfaced in the repository's `InfoBar` via `Tab.Report`, not in
a modal dialog.

**Never put a literal tab in a `--format` argument.** `ArgumentBuilder` composes the whole argument
list into a single command line, and Windows splits a command line on tabs exactly as it does on
spaces, so a literal tab fragments the format string and the command fails. Use git's own escapes:
`%09` for `for-each-ref`, `%x09` for anything taking a `git log` pretty format.

**`ReadRecords` pads short lines rather than skipping them.** Trailing fields are routinely empty — a
branch with no upstream, a ref that is not symbolic — and the separators that would mark them are
trimmed off before parsing. Requiring a full field count silently drops exactly those rows.

**Reads are capped.** 2,000 commits per page (`AppOptions.MaxCommits`) and 5,000 diff lines
(`MaxDiffLines`). This repository alone has ~17k commits, more than an unvirtualised bound collection
should hold. Paging uses `--skip`, spliced into `RevisionReader`'s `revisionFilter`.

**Session state** — open tabs, UI mode, window bounds, settings — persists to
`%LOCALAPPDATA%\Gitxen\session.json`. Restore failures are written to `restore-error.log`
beside it rather than leaving the shell silently looking like a first run. A session written by a build from before the rename is carried over from the old folder on first launch.

## A layout constraint you will hit

**In this app, a `Grid` column placed after a star-sized column is arranged off the right of the
screen.** The star column takes an unbounded width, so anything after it lands outside the window.

Confirmed with layout instrumentation: the element reports `Visible`, with a real size and a position
inside the window, and still does not paint. The origin was not found. Attempts that did *not* fix it:
setting `HorizontalContentAlignment` on the `UserControl`s, stretching the `TabViewItem` content,
pinning the root to the client size, and pinning the `NavigationView` content width.

Work within it:

- Toolbars are single left-aligned runs. Nothing is pinned to the right of a full-width bar.
- List row templates use fixed column widths.
- Where a star column is genuinely wanted, it goes **last** — as in the commit rows and the diff rows.

Anything under an element with an explicit width is unaffected, which is why the 400px staging pane on
the Changes page lays out normally.

## What is implemented

Open / clone / init, multiple repositories as tabs or a left-hand column, and a Home tab that is
always first: recent repositories with their current branch and last commit, organised into projects
you can create, colour, give an icon, collapse, and drag repositories in and out of. Group membership
is stored as paths, so a group survives its repositories being closed or temporarily unavailable.

Changes: staged and unstaged lists, stage/unstage per file and for all, **hunk-level staging**,
discard, commit, amend, sign-off. History: graph lanes, ref badges, current-branch or all-branches
scope, client-side filter plus a real `git log` search (message, author, content via `-S`, path),
commit details, per-commit context menu (cherry-pick, revert, rebase, reset, bisect, branch, tag,
checkout, copy). Reflog with recovery actions. Conflict resolution: a side per file, a merge tool, or resolve by hand
and mark it done. Paused merges, rebases and cherry-picks are detected and shown as a banner with
their own continue/skip/abort. Branches (local and remote), Remotes, Tags, Stashes,
Submodules, Worktrees, Maintenance, Settings. Fetch / pull / push with options, and continue/abort for
a paused merge, rebase, cherry-pick or revert.

## What is not

Interactive rebase, real Blame and File-history views (both are raw text in a dialog), a file tree,
word-level diff highlighting, drag-and-drop on the commit graph, format-patch and apply-patch,
sparse checkout, submodule add/remove, GPG, LFS, the plugin host, localization (this front-end is
English-only and not wired to ResourceManager), the full settings tree, credential handling, and
crash reporting.

**There are no tests.** `HunkSplitter` is the most valuable thing to cover — it is pure
string-in/string-out and it builds patches that are applied to the index. The obstacle is that it
lives in an x64 Windows App SDK assembly, so testing it needs either an x64 test project or lifting
the pure parsers into a platform-neutral one.
