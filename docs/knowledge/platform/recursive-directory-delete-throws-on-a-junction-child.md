---
description: Directory.Delete(path, recursive true) on .NET 10 Windows throws when the tree contains a JUNCTION child - it unlinks the junction and then fails anyway, leaving the tree; a directory symlink is handled natively, the failure is not elevation-dependent, and the exception type varies by host
applies-to:
  - clio/Command/McpServer/Knowledge/KnowledgeManagedTreeDeleter.cs
  - clio.tests/Command/McpServer/KnowledgeManagedTreeDeleterFileSystemTests.cs
ticket: ENG-96211
date: 2026-08-31
---

**What is true** — measured on .NET 10 / Windows, three runs of each shape. Note the scope: `clio.tests`
targets net10.0 only, while `clio` ships net8.0 **and** net10.0, so a .NET 10 servicing fix does not release
clio from the workaround — the lowest shipped target framework has to cope first.

| tree | `Directory.Delete(root, recursive: true)` |
|---|---|
| contains a **junction** child, any depth | **throws**; the junction is unlinked, the root survives |
| the path **is** a junction | succeeds; unlinked, target untouched |
| contains a directory **symlink** child | succeeds |
| contains a file **symlink** child | succeeds |

Three details decide how you may react to it, and each one contradicts the obvious guess:

- **It is not an elevation problem.** It fails identically in an elevated process. Do not write "when
  non-elevated" — that tells the next reader an elevated machine is safe.
- **The exception type is not stable.** A self-hosted release runner produced
  `UnauthorizedAccessException: Access to the path 'linked' is denied`; a developer workstation produced
  `IOException: The parameter is incorrect. : 'linked'`. Never key a catch or an assertion on the type.
- **The junction is already gone when it throws.** So a bare "retry once" appears to work. Do not do that: a
  retry after a partial delete is precisely the non-atomic behaviour `DeleteRecoverably` renames the tree to
  avoid.

The supported reaction is to unlink directory reparse points yourself, up front, with a **non-recursive**
delete — which removes the link and leaves the target and its attributes untouched (verified) — so the
recursive delete never meets one. `KnowledgeManagedTreeDeleter` does this during the read-only walk it
already performs, which costs nothing extra.

**Why it is this way — and it is a bug, not a safety refusal.** A junction carries
`IO_REPARSE_TAG_MOUNT_POINT`, the same tag as a volume mount point; a symlink carries
`IO_REPARSE_TAG_SYMLINK`. For the mount-point tag the recursive delete first attempts an unmount via
`DeleteVolumeMountPoint`. On an ordinary junction — which is not a mounted folder — that call fails and does
nothing; the error is latched, `RemoveDirectory` still removes the link, and the latched error is thrown at
the end. That is exactly the signature above: junction gone, tree left, message differing by host. It is
[dotnet/runtime#86249](https://github.com/dotnet/runtime/issues/86249), open with an active fix, alongside
the older [#23646](https://github.com/dotnet/runtime/issues/23646) on the missing name-surrogate check. Do
not describe it as the framework protecting mounted volumes — it isn't, and believing that stops you
checking what clio's replacement does.

**What the replacement does NOT inherit.** `Directory.Delete(link, recursive: false)` is a bare
`RemoveDirectory`: it never calls `DeleteVolumeMountPoint`. So for a *genuine* mounted folder inside the
managed tree, clio strips the mount-point path without dismounting, leaving the volume mounted with no path.
**Accepted**, because a mounted folder cannot arrive from remote checkout content — it needs local mount
privilege — but accepted deliberately rather than unnoticed.

**Three tag classes, not two.** The framework keys on the name-surrogate bit: it unlinks a symlink natively,
mishandles a mount point as above, and **descends into** a non-name-surrogate tag — a OneDrive
Files-On-Demand placeholder, a ProjFS/Scalar root, WCI, DFS. Clio separates them with
`ResolveLinkTarget(returnFinalTarget: false) is not null`: the first two are unlinked, and the third is
walked exactly as the framework walks it, so a read-only `*.pack` behind a placeholder folder still has its
attribute cleared instead of becoming an undeletable cache for a different reason.

**`ResolveLinkTarget` returns a target for a JUNCTION, not only for a symbolic link** — measured, both
`LinkTarget` and `ResolveLinkTarget`. That is what makes the split above usable: gating on it does not
accidentally exclude the mount-point tag, which is the only one that actually breaks the delete. It also
answers a question about a different guard: `OutputPathConfinement.IsReparsePoint` is implemented over
`LinkTarget`, so `mklink /J` does **not** slip past the write-confinement check.

**A substituted `IDirectoryInfo` does not return null here.** `ResolveLinkTarget` returns an interface, and
NSubstitute auto-substitutes interface return types — so a mock link looks like a link without being
configured, and a test for the non-link class must set `ResolveLinkTarget` to null explicitly. Assuming the
default is null is how that branch ends up unpinned.

**What breaks if you ignore it** — a user whose knowledge checkout contains a junction cannot delete the
cache at all, and the error names only `'linked'`. Because the source is unregistered while its cache
survives, the next `add-knowledge-source` for that alias is refused with "not owned by Clio" — the dead end
the whole deletion path was rebuilt to remove. It reached a release: the test that covers this branch chose
a symlink whenever the privilege was available, so every developer machine with elevation or Developer Mode
ran the easy shape and the release runner was the first host to execute the junction path. The fixture now
always creates a junction on Windows for exactly that reason — if you make that helper "prefer" anything,
you restore the lottery.
