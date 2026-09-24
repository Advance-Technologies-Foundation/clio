---
description: pkg-to-file-system (and turn-fsm on) exports through a symlinked package directory with no reparse-point check, so a package linked from a repo via link-from-repository gets the FSM export written straight into the repo working tree
applies-to:
  - clio/Command/LoadPackagesToFileSystemCommand.cs
  - clio/Command/Link4RepoCommand.cs
  - clio/Command/TurnFsmCommand.cs
date: 2026-09-21
---

**What is true** — `LoadPackagesToFileSystemCommand.Load` calls
`IFileDesignModePackages.LoadPackagesToFileSystem()` against whatever the environment's
`Pkg/<package>` path currently resolves to, with no check for a reparse point. `link-from-repository`
replaces that same path with a symlink into a git repository (`Link4RepoCommand.cs` logs "Existing
directory ... will be replaced with symlink" when it does). Nothing in the export path detects that
the target is a symlink before writing into it. `turn-fsm on` calls this same export step after
writing the FSM configuration, so it inherits the trap.

**Why it is this way** — the export step predates `link-from-repository` symlinking package
directories, and the two commands don't inspect each other's effect on disk. Elsewhere in the repo,
write paths are guarded with an explicit reparse-point check (`OutputPathConfinement.cs`); the FSM
export path has no equivalent.

**What breaks if you ignore it** — running `pkg-to-file-system` (directly, or via `turn-fsm on`) while
an environment's `Pkg/<package>` folders are still symlinked from a previous `link-from-repository`
writes Creatio's generated FSM export straight into the developer's repository working tree, mixing
generated content into tracked source with no warning. Unlink (restore the real directory or remove
the symlink) before exporting, and re-link afterward.
