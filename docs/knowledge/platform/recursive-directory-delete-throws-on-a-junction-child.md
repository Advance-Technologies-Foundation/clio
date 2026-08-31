---
description: Directory.Delete(path, recursive true) on .NET 10 Windows throws when the tree contains a JUNCTION child - it unlinks the junction and then fails anyway, leaving the tree; a directory symlink is handled natively, the failure is not elevation-dependent, and the exception type varies by host
applies-to:
  - clio/Command/McpServer/Knowledge/KnowledgeManagedTreeDeleter.cs
  - clio.tests/Command/McpServer/KnowledgeManagedTreeDeleterFileSystemTests.cs
ticket: ENG-96211
date: 2026-08-31
---

**What is true** — measured on .NET 10 / Windows, three runs of each shape:

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

**Why it is this way** — a junction carries `IO_REPARSE_TAG_MOUNT_POINT`, the same tag as a volume mount
point, while a symlink carries `IO_REPARSE_TAG_SYMLINK`. The framework's recursive delete distinguishes the
two and refuses the mount-point tag rather than risk deleting through a mounted volume. Clio cannot tell it
that a knowledge checkout's junction is not a mounted volume, so clio removes the link before asking.

**What breaks if you ignore it** — a user whose knowledge checkout contains a junction cannot delete the
cache at all, and the error names only `'linked'`. Because the source is unregistered while its cache
survives, the next `add-knowledge-source` for that alias is refused with "not owned by Clio" — the dead end
the whole deletion path was rebuilt to remove. It reached a release: the test that covers this branch chose
a symlink whenever the privilege was available, so every developer machine with elevation or Developer Mode
ran the easy shape and the release runner was the first host to execute the junction path. The fixture now
always creates a junction on Windows for exactly that reason — if you make that helper "prefer" anything,
you restore the lottery.
