# Gitxen

Gitxen is a free, open-source Windows git client. It is a **derivative work of
[Git Extensions](https://github.com/gitextensions/gitextensions)** and is distributed under the same
licence, the **GNU General Public License version 3**. The full licence text is in
[LICENSE.md](LICENSE.md).

## Upstream

| | |
| --- | --- |
| Upstream project | Git Extensions |
| Upstream source | https://github.com/gitextensions/gitextensions |
| Upstream licence | GNU General Public License v3.0 |

Copyright in the original work remains with the Git Extensions authors and contributors. Nothing here
supersedes their copyright; this notice records the derivation, as GPL-3.0 requires.

## What Gitxen changes

Gitxen adds a WinUI 3 front-end under `src/app/GitExtensions.WinUI/`, built on the existing Git
Extensions engine (`GitCommands`, `GitExtUtils`, `GitExtensions.Extensibility`) without modifying it.
The original WinForms application is left in place and continues to build and run unchanged.

Modifications, as required by GPL-3.0 section 5(a):

- Added `src/app/GitExtensions.WinUI/` — a new WinUI 3 user interface, with its own shell,
  repository pages, commit graph, diff viewer, hunk staging and conflict resolution.
- No changes to the behaviour of the original WinForms application, its plugins, or the git engine
  they share.

Each change is recorded individually in the git history of this repository, which is the authoritative
record of what was modified and when.

## What this means for you

Gitxen is free software. You may use, study, share and modify it. If you distribute Gitxen, or any
work derived from it, you must do so under GPL-3.0 and make the corresponding source available — the
same terms under which Git Extensions was given to us.

Git Extensions is a trademark of its own project. Gitxen is named differently precisely so that it is
not mistaken for, and does not trade on, the upstream project's identity. Gitxen is not endorsed by or
affiliated with the Git Extensions project.
