# Gitxen

Gitxen is a free, open-source git client for Windows, built with WinUI 3.

It shows staged and unstaged changes with hunk-level staging, a commit graph with ref badges and
live search, branches, remotes, tags, stashes, submodules, worktrees, reflog recovery and conflict
resolution — with several repositories open at once as tabs.

Gitxen is a **derivative work of [Git Extensions](https://github.com/gitextensions/gitextensions)**
and is distributed under the same licence, **GPL-3.0** — see [Licence and credits](#licence-and-credits)
below.

## Status

Gitxen is in early development (current version: 0.1.0). The WinUI 3 front-end lives in
[src/app/GitExtensions.WinUI/](src/app/GitExtensions.WinUI/) and is built on the unmodified
Git Extensions engine; the original WinForms application also still builds and runs from this
repository, unchanged.

## Install

Requires Windows 10 (1809) or later, x64. Packages are self-contained — no separate .NET or
Windows App SDK install is needed.

* **Installer (MSI)** or **portable zip**: download from the
  [releases page](https://github.com/dev-kakosoai/gitxen/releases).
* **PowerShell one-liner** (per-user, no admin, verifies checksums):

  ```powershell
  irm https://raw.githubusercontent.com/dev-kakosoai/gitxen/master/setup/gitxen/install.ps1 | iex
  ```

* **Package managers**, as the channels come online: winget (`Kakoso.Gitxen`),
  Chocolatey (`gitxen`), Scoop (`gitxen`).

Every channel puts `gitxen` on `PATH` and adds a Start menu entry. Packaging details live in
[setup/gitxen/](setup/gitxen/README.md).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in
`global.json`), a Windows SDK install, and Visual Studio 2026 for the full solution. After cloning:

```
git submodule update --init --recursive
dotnet build Gitxen.slnf
dotnet build src/app/GitExtensions.WinUI/GitExtensions.WinUI.csproj -c Debug -p:Platform=x64
```

`Gitxen.slnf` scopes the build to the app and its backend dependencies; a bare `dotnet build` builds
the entire solution, including the WinForms application. See the
[Gitxen README](src/app/GitExtensions.WinUI/README.md) for the project layout and conventions.

## Licence and credits

Gitxen is licensed under the **GNU General Public License v3.0** — see [LICENSE.md](LICENSE.md).

It is a derivative work of **[Git Extensions](https://github.com/gitextensions/gitextensions)**.
Copyright in the original work remains with the Git Extensions authors and contributors;
[NOTICE.md](NOTICE.md) records the derivation and what was changed, as GPL-3.0 requires. Gitxen is
named differently precisely so that it is not mistaken for the upstream project, and is **not
endorsed by or affiliated with** the Git Extensions project.

With thanks to:

* The **Git Extensions team and contributors**, whose engine and years of work this project is built
  on. If you find Gitxen useful, consider [supporting Git Extensions on Open
  Collective](https://opencollective.com/gitextensions).
* [Yusuke Kamiyamane](http://p.yusukekamiyamane.com/) for icons used in the codebase
  ([CC BY 3.0](http://creativecommons.org/licenses/by/3.0/)).

## Conduct

Project maintainers pledge to foster an open and welcoming environment, and ask contributors to do
the same. For more information see the [code of conduct](CODE_OF_CONDUCT.md).

## Links

* Source code: [github.com/dev-kakosoai/gitxen](https://github.com/dev-kakosoai/gitxen)
* Issue tracker: [github.com/dev-kakosoai/gitxen/issues](https://github.com/dev-kakosoai/gitxen/issues)
* Upstream project: [gitextensions.github.io](https://gitextensions.github.io/) ·
  [online manual](https://git-extensions-documentation.readthedocs.org/)
