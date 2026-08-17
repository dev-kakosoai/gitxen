# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read this first: the repo has its own agent docs

This repo maintains a hierarchical, agent-optimised doc set (DAG) under `.github/copilot-docs/`,
plus reusable workflows under `.github/skills/`. Prefer these over re-deriving things from scratch:

- **Always read**: `.github/copilot-docs/L0-foundations/gitextensions-primer.md` (short: architecture
  anchor, glossary, ownership map).
- **Navigate from**: `.github/copilot-docs/docs-index.md` — has an ownership table and "reading chains"
  (ordered doc sequences) for common questions like "how does checkout work", "how is the commit graph
  rendered", "how does CI build/test a PR". Follow a reading chain before grepping the code cold.
- **Layers**: L1 `.github/copilot-docs/L1-conceptual/` (architecture, project map), L2
  `.github/copilot-docs/L2-core-platform/` (git execution, settings, plugins, translation, theming,
  build/test, CI), L3 `.github/copilot-docs/L3-flows/` (end-to-end flows: commit, checkout, clone,
  push/pull, rebase, diff).
- **Skills** (runbooks) under `.github/skills/`: `run-tests`, `add-translation-strings`,
  `add-winforms-dialog`, `code-search`, `deep-explanation`, `doc-management`, `pr-creation`.
- Full style/formatting/testing rules live in `.github/copilot-instructions.md` — read it; it is not
  duplicated below except for the parts that matter most often.

After reading docs, always verify against the actual source files they point to before relying on them.

## Repository setup

Uses git submodules under `externals/`. After cloning or creating a new worktree:

```
git submodule update --init --recursive
```

## Common commands

```
dotnet build                              # managed build (build GitExtensions.slnx)
dotnet build /v:q                         # quiet build; do this before running/interpreting tests
dotnet test                               # run the full test suite
dotnet test .\tests\app\UnitTests\<Project>\<Project>.csproj   # scope to one test project
dotnet test <project> --filter "FullyQualifiedName~<Type>"    # scope to one test class/type
dotnet build .\src\native\build.proj      # native build (C++ shell extension, needs VC++/ATL)
.\update-loc.cmd                          # regenerate English.xlf after UI string/control changes (requires a successful `dotnet build /v:q` first)
```

Requires .NET 10 SDK (pinned in `global.json`) and, for the native build, the VC++/ATL toolset. Windows-only.

**Always run the tests that cover a change locally before pushing** — do not rely on CI as the first
place a change is exercised.

## Architecture (big picture)

Layered, bottom to top:

```
GitExtensions.Extensibility  → interfaces + primitives (IGitCommand, IExecutable, ArgumentBuilder)
        ▲
GitCommands                  → git execution + models (GitModule, Executable, Commands, Settings)
        ▲
GitUI                        → WinForms UI (FormBrowse main window, dialogs, RevisionGrid)
        ▲
GitExtensions (exe)          → app entry point / bootstrap
```

Plus `src/plugins/*` (optional features via `GitUIPluginInterfaces`) and `src/native/GitExtensionsShellEx`
(Windows Explorer shell extension) / `GitExtSshAskPass`.

**Typical action flow:** a `Form` in `GitUI` calls into `GitUICommands` → `Commands` (in
`GitCommands.Git`) builds a structured `IGitCommand` → `GitModule`/`Executable` shells out to the real
`git` binary → output is parsed into models → the UI updates and `RepoChangedNotifier` fires.

Key vocabulary: `GitModule` (one repo, main entry point for git state/commands), `Executable`/
`IExecutable` (process-launch abstraction returning `ExecutionResult`), `Commands` (factory of
`IGitCommand` records declaring whether a command touches a remote / changes repo state),
`GitUICommands`/`IGitUICommands` (runs commands with UI feedback via process dialogs),
`ArgumentBuilder` (safe git argument construction), `FormBrowse` (main window), `RevisionGrid`
(commit graph control), Revision (a commit) vs ref (branch/tag/remote pointer), module vs submodule.

### Where things live

| Area | Path |
| --- | --- |
| Git execution engine, settings/config, remotes, submodules | `src/app/GitCommands/` |
| Versioned plugin interfaces & primitives | `src/app/GitExtensions.Extensibility/` |
| All WinForms UI: forms, `CommandsDialogs/`, `RevisionGrid`, theming, hotkeys, scripts engine | `src/app/GitUI/` |
| App entry point / bootstrap / DI wiring | `src/app/GitExtensions/` |
| Cross-cutting helpers | `src/app/GitExtUtils/` |
| Translation infrastructure | `src/app/ResourceManager/`; strings in `src/app/GitUI/Translation/English.xlf` |
| Roslyn analyzers enforcing repo conventions | `src/app/GitExtensions.Analyzers.CSharp/` |
| Crash/bug reporting UI | `src/app/BugReporter/` |
| Optional plugins (Bitbucket, GitHub3, GitFlow, BuildServerIntegration, …) | `src/plugins/`, contracts in `GitUIPluginInterfaces` |
| Experimental WinUI3 front-end (personal, in progress) | `src/app/GitExtensions.WinUI/` |
| Explorer shell extension / SSH askpass (C++) | `src/native/` |
| Installer (WiX) | `Setup/installer/` |
| Build/engineering scripts, rulesets, StyleCop config | `eng/` |
| Unit + integration tests, shared test helpers | `tests/` |

## GitExtensions.WinUI (experimental WinUI3 front-end)

`src/app/GitExtensions.WinUI/` is a personal, in-progress WinUI3 (Windows App SDK) rewrite of the UI,
staged alongside the WinForms app rather than replacing it. It reuses the existing backend as-is
(`ProjectReference` to `GitCommands`, `GitExtUtils`, `GitExtensions.Extensibility`) — no `GitUI`
dependency — and drives it through `GitModule` + `RevisionReader`. It is deliberately excluded from
the installer and publish pipeline (no `BuildDependency` in `GitExtensions.slnx`, not referenced by
`GitExtensions.csproj`).

- Build/run: `dotnet build src/app/GitExtensions.WinUI/GitExtensions.WinUI.csproj -c Debug -p:Platform=x64`
  (x64-only; the rest of the solution is AnyCPU). Requires a real Windows SDK install — the native
  WinUI toolchain (`cswinrt.exe`, PRI/MSIX tasks) needs the `KitsRoot10` registry value present.
- Bootstrap mirrors the minimum viable part of `src/app/GitExtensions/Program.cs`: capture a
  `JoinableTaskContext` for `ThreadHelper`, register services (`Bootstrap/ServiceContainerRegistry.cs`
  replicates the app-level + `GitCommands` registrations, skipping `GitUI`'s), then `AppSettings.LoadSettings()`.
- The csproj carries several non-obvious workarounds for genuine .NET 10 SDK / WindowsAppSDK
  version-resolution defects — each is commented in place. Do not "clean them up" without
  re-verifying the app still *launches*, not just builds: several of these failures only surface at
  runtime as `FileNotFoundException` on `WinRT.Runtime` / `Microsoft.Windows.SDK.NET`.
- UI modes (`UiMode.cs`): Simple (default), Advanced, and a hidden Zen mode reachable only via
  Ctrl+Shift+Z (Escape exits). One tab per repository, each with its own `RepositoryTabViewModel`.
- Commits stream in as `git log` produces them, a page (`MaxCommits`, 2,000) at a time, with
  "Load more commits" paging via `--skip` spliced into `RevisionReader`'s `revisionFilter`. A
  selected file's diff is capped at 5,000 lines (`MaxDiffLines`). The caps exist because this repo
  alone has ~17k commits, which is more than an unvirtualized bound collection should hold.
- Git operations (fetch/pull/push/checkout/commit) go through `RepositoryLoader`, which builds
  arguments with the same `Commands`/`GitModule` factories the WinForms app uses and runs them via
  `GitModule.GitExecutable.Execute(..., throwOnErrorExit: false)` — a non-zero exit is information
  for the user, not an exception. Every operation returns a `GitOperationResult` and triggers a
  reload. Push is confirmed first (it is the only one that changes something off this machine), and
  commit is deliberately all-or-nothing: there is no partial staging UI yet, so it stages everything
  and says so before committing.
- Session (open tabs, mode, window bounds) persists to
  `%LOCALAPPDATA%\GitExtensions.WinUI\session.json`; restore failures are recorded next to it in
  `restore-error.log` rather than silently leaving the shell looking like a first run.

## Hard rules that apply almost everywhere

- Run git operations through `IGitModule`; build arguments via `Commands` in `GitCommands.Git` (never
  raw strings) — see `.github/copilot-instructions.md` for the full `GitUICommands`/`StartCommandLineProcessDialog` pattern.
- Files must use CRLF line endings and satisfy StyleCop + `.editorconfig`.
- C# 14 features; nullable reference types enabled — trust the type system, avoid `!` suppressions
  (use `Validates.NotNull` instead when a runtime check is genuinely needed).
- Never use `var` for primitive types; prefer `Type t = new();` over `var t = new Type();`.
- Any UI string/control/menu-item change requires updating `src/app/GitUI/Translation/English.xlf` via
  `update-loc.cmd` — CI fails if it's stale. Never hand-edit `.xlf` files.
- Changes under `src/app/GitExtensions.Extensibility/` bump the **plugin interface version** — call
  this out explicitly in the commit message.
- Tests: NUnit + `NSubstitute` (mocking) + `AwesomeAssertions` (never `ClassicAssert`, never Moq). Test
  names keep the method name and add a snake_case suffix (`MyMethod_should_return_expected`); no
  `Arrange`/`Act`/`Assert` comments. Substitute `IExecutable`/`IProcess` so tests don't spawn real `git`.
  Fix flaky tests at the root cause rather than dismissing or retrying them.
- Commit messages follow Conventional Commits.
