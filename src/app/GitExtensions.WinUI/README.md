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

Two solution filters at the repo root scope day-to-day work to this app's dependency closure,
so a bare `dotnet build` of all fifty solution projects stays a pre-push-only affair:

```
dotnet build Gitxen.slnf          # this app + its four backend projects (also openable in VS)
dotnet test  Gitxen.Tests.slnf    # the backend test projects that cover those dependencies
```

The output is `artifacts/Debug/bin/GitExtensions.WinUI/x64/net10.0-windows10.0.19041.0/GitExtensions.WinUI.exe`.

**Building is not proof it runs.** The csproj carries several workarounds for genuine .NET 10 SDK /
Windows App SDK version-resolution defects, each commented in place. Some of those failures only
appear at startup, as `FileNotFoundException` on `WinRT.Runtime` or `Microsoft.Windows.SDK.NET`.
Always launch the app after touching the csproj.

## The first run

`Views/SetupWizardView` is an overlay across the whole window, shown once when the session says setup
has not been through. Seven steps: welcome (which also reports whether git is installed at all),
identity, appearance, behaviour, a folder scan, projects, and a summary. Skipping counts as having
been through it — being asked again on every launch after saying no once is worse than never asking.

The steps are panels switched by visibility, not pages in a `Frame`, for the same reason the
repository sections are: going back a step must not discard what was typed into the step you are
returning to.

**Where each answer goes.** Identity (`user.name`, `user.email`, `init.defaultBranch`) is written to
git's global configuration through `Services/GlobalGitSettings`, which exists because
`RepositoryLoader` is built around a repository that already exists and the wizard runs before
anything has been opened. Theme, auto-fetch and the commit page size are written to `AppOptions`,
which is read on demand. Layout and UI mode are *not*: they come back on the outcome and the shell
sets them through `MainViewModel`, whose property setters are what raise the notifications the tab
strip and the repository column are bound to.

**The scan** is `Services/RepositoryScanner`: an iterative walk over an explicit stack, bounded by a
depth (4 by default), skipping dependency and build folders, and not descending into a repository
once it finds one — a repository's submodules are part of it, not separate things to import. The
branch shown against each hit is read straight out of `.git/HEAD` rather than by running git, because
a projects folder routinely holds dozens of repositories and a process per repository costs more than
the entire walk.

**Projects** are proposed from the folders the repositories were found in, which is already how people
think about them, and the proposed names are editable before anything is created. The alternative
offered is one project for everything, or none.

Imported repositories are added to Home and **not opened**: thirty tabs appearing at once would be a
worse first impression than the empty shell the wizard exists to fix. This is why `MaxRecent` in
`MainViewModel` is 60 rather than the 10 it was when that list was purely most-recently-used.

Home's **Find repositories** button reopens the wizard, which is how a second projects folder gets
imported later. Settings would be the conventional home for that, but Settings is a repository page
and is unreachable with nothing open.

A session written before the wizard existed has no `HasCompletedSetup` flag and would deserialise to
`false`, showing the first-run wizard to someone who has used the app for months. `SessionStore`
treats a session that already names repositories, groups or recent paths as one that is demonstrably
past its first run.

## Packaging

Everything that turns a build into something installable lives in
[`setup/gitxen/`](../../../setup/gitxen/README.md): the MSI, the portable archive, an MSIX, the
`irm | iex` script, and the winget / Chocolatey / Scoop manifests. One command builds the lot:

```
pwsh ./setup/gitxen/build/Build-Gitxen.ps1
```

The shipped payload is a **self-contained** publish (`-r win-x64 --self-contained
-p:WindowsAppSDKSelfContained=true`), so neither the .NET runtime nor the Windows App SDK has to be
installed on the target machine. Two properties in the csproj exist purely for that path, and both
were arrived at through a startup crash rather than a build error:

- **`RuntimeFrameworkVersion` is cleared.** `eng/RepoLayout.props` pins it to `10.0.0` for
  `Microsoft.NETCore.App`, and MSBuild applies a single value to *every* framework reference, so a
  RID-specific restore goes looking for `Microsoft.Windows.SDK.NET.Ref 10.0.0`, which has never
  existed.
- **`PublishXamlCompilerOutputs` carries the XAML compiler's output into the publish folder.** The
  `.xbf` files and the app's `.pri` are build outputs that the SDK's publish pipeline does not track,
  so `dotnet publish` drops them silently. Without the `.pri`, Microsoft.UI.Xaml cannot resolve the
  app's own XAML and the process dies before the window appears, with a stowed exception
  (`0xc000027b`) and no managed stack.

The same warning as above applies with more force here: publishing successfully is not proof the
published application starts. Run the executable out of `artifacts/Release/publish/Gitxen` after any
change to the csproj or to the packaging.

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
| `Terminal/` | The bottom terminal's engine: `ConPtySession` (a shell on a Windows pseudoconsole), `TerminalOutputBuffer` (VT stream back to plain lines), `TerminalSession` (one running shell), `TerminalShells` (which shells are installed). |
| `Assets/` | The Gitxen mark. `gitxen.svg` is the vector master; the PNGs beside it are rendered from the same geometry, and `gitxen.ico` bundles them for the shell. |

The tab strip is a `TabView` with no content: the repository view is hosted in its own grid row
instead, because `TabView` arranges its content at the content's desired size rather than filling the
space, which left short pages floating in a fraction of the window.

Sections are switched by visibility rather than navigated to in a `Frame`. A `Frame` recreates its
page on every visit, which would discard the commit list's scroll position and any half-typed commit
message.

## The mark

An X drawn as two crossing strokes on a rounded gradient tile, with commit nodes at the four
ends and a punched-out node where they meet: an X for Gitxen, and two branches meeting at a
merge point.

The nodes are drawn only at 48px and above. Below that they close up against the strokes and the
X stops reading as an X, which is the one thing it has to survive at 16px in a taskbar.

| File | Used for |
| --- | --- |
| `gitxen.svg` | Vector master. Everything else is rendered to match it. |
| `gitxen.ico` | 16-256px. `ApplicationIcon`, so the shell shows it on the executable, and `AppWindow.SetIcon`, so the taskbar and Alt+Tab show it on the window. |
| `gitxen-32.png` | The title bar. |
| `favicon.ico`, `favicon-16.png`, `favicon-32.png`, `apple-touch-icon.png`, `gitxen-logo.png` | For a site or a readme; not used by the app. |

`ApplicationIcon` alone is not enough — it only embeds the icon in the executable. An unpackaged
WinUI window carries no icon of its own until it is given one, so `MainWindow` also calls
`AppWindow.SetIcon` at startup.

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

## The terminal

Ctrl+` (or the command-bar button) docks a terminal under whichever section is showing —
`Views/TerminalPane` hosted in `RepositoryView`. It offers whatever shells the machine actually has
(PowerShell 7, Windows PowerShell, cmd, Git Bash, WSL — probed by `Terminal/TerminalShells`), opens
sessions on the repository's working directory, and keeps several sessions at once. Like the panel in
an editor, the pane is shared across repository tabs; a session stays in the directory it was opened
on and says so in its title.

Each session runs its shell on a **ConPTY** (`Terminal/ConPtySession`), not on redirected stdio: a
shell behind plain pipes knows it is not interactive — PowerShell stops prompting, bash drops its
profile — while behind a pseudoconsole it behaves exactly as it does in Windows Terminal. The cost is
that ConPTY output is a VT stream, and `Terminal/TerminalOutputBuffer` turns that back into plain
lines. It is a **transcript, not a character-grid emulator**: carriage-return overwrites, backspace,
erase sequences and cursor-positioning-as-line-break are honoured, colours are dropped, and
full-screen programs (vim, htop) degrade to appended text — they belong in a real terminal via
"Open in". Commands are typed into a line editor under the transcript; Enter sends the line, Up/Down
recall history, Ctrl+C in the empty box interrupts.

Two things about ConPTY that are not obvious:

- **The shell exiting does not close the output pipe** — conhost holds the write end until the
  pseudoconsole is closed, so `ConPtySession` watches the process handle for exit rather than
  waiting for EOF.
- **Testing it needs a console-less parent.** Under a parent that already has a console (a test
  runner, `dotnet run`), the child attaches to that console instead of the pseudoconsole and the
  PTY sees nothing; the same code works correctly in this app because it is a GUI process. A
  windowless harness is the only honest way to exercise it outside the app.

The pane height persists like the other dragged panes (`AppOptions.TerminalPaneHeight`). Sessions are
ended explicitly when the window closes, so no shell outlives the app.

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

A first-run wizard that sets the git identity, the theme, the layout and the mode, then scans a
folder for repositories and imports the ones you pick, grouped into projects taken from the folders
they were found in. Reachable again from Home.

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
a paused merge, rebase, cherry-pick or revert. A dockable bottom terminal (Ctrl+`) with real shells
on ConPTY — PowerShell, cmd, Git Bash, WSL — described above.

## What is not

A character-grid terminal (the pane is a transcript; full-screen TUI programs degrade),
interactive rebase, real Blame and File-history views (both are raw text in a dialog), a file tree,
word-level diff highlighting, drag-and-drop on the commit graph, format-patch and apply-patch,
sparse checkout, submodule add/remove, GPG, LFS, the plugin host, localization (this front-end is
English-only and not wired to ResourceManager), the full settings tree, credential handling, and
crash reporting.

**There are no tests.** `HunkSplitter` is the most valuable thing to cover — it is pure
string-in/string-out and it builds patches that are applied to the index. The obstacle is that it
lives in an x64 Windows App SDK assembly, so testing it needs either an x64 test project or lifting
the pure parsers into a platform-neutral one.
