---
description: compiling a package whose Pkg folder is symlinked into a git repo (via link-from-repository) regenerates that package's .csproj on every compile, so the churn lands inside tracked source unless suppressed
applies-to:
  - clio/Command/Link4RepoCommand.cs
  - clio/Command/TurnFsmCommand.cs
date: 2026-09-21
---

**What is true** — Creatio materializes a generated `<package>.csproj` (and other auxiliary files)
under the package's `Files` folder on every compile, independent of file design mode (see
[`ClioGate/non-fsm-compilation-materializes-package-project-files.md`](../ClioGate/non-fsm-compilation-materializes-package-project-files.md)).
When that package directory is a symlink into a git repository, the regenerated `.csproj` is written
straight into the repo's tracked working tree, not into a throwaway copy. A single compile of a
linked workspace has been observed to rewrite on the order of 2,500 lines of `.csproj` content.

**Why it is this way** — the compiler's project-file generation has no awareness that its target
directory is a symlink into a developer's git checkout; it treats it like any other `Files` folder.

**What breaks if you ignore it** — every compile of a repo-linked workspace shows as a large,
noisy diff in `git status`/`git diff` on the generated `.csproj`, and an `Assemblies/` output
folder appearing under the linked package looks like new tracked content. Mark the generated
`.csproj` with `git update-index --skip-worktree` so routine compiles stop showing as local changes,
and add `Assemblies/` to the repo's `.gitignore` (or an equivalent exclude) before compiling a linked
workspace.
