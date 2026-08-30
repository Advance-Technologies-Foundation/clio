---
description: the per-source mutation lock lives at sources/.locks/<key>.lock, a sibling of sources/<key>/ and never inside it - moving it into the source root would make KnowledgeManagedTreeDeleter's rename-before-delete fail on Windows every time, because clio itself holds the handle
applies-to:
  - clio/Command/McpServer/Knowledge/KnowledgeSourceInstallationStore.cs
  - clio/Command/McpServer/Knowledge/KnowledgeManagedTreeDeleter.cs
date: 2026-08-30
---

**What is true** — `ExecuteWithSourceMutationLock` opens `sources/.locks/<sourceKey>.lock`, a **sibling** of
`sources/<sourceKey>/`, never a file under it. That placement is load-bearing, not incidental:
`KnowledgeManagedTreeDeleter.Delete` runs inside that lock and begins by renaming the source root aside
(`Directory.Move`), and Windows refuses to rename a directory that contains an open handle. A lock file
inside the source root would therefore be held open by clio itself at the exact moment clio tries to move
the tree.

Two observable consequences confirm the layout rather than the intention, and both look like debris:
a `.lock` file outlives the source directory it guards (locks are never deleted — deleting a source removes
`sources/<key>/` and leaves `.locks/<key>.lock`), and a `.lock` can exist with **no** matching directory at
all, left by an operation that took the lock and failed before the source root was created. Neither is a
defect. Do not "tidy" them by relocating locks under the source they lock.

**Why it is this way** — the lock has to outlive and stand outside the thing it protects, because what it
protects is created, replaced and destroyed under it. A lock stored inside its own subject cannot guard the
subject's own removal.

**What breaks if you ignore it** — every knowledge-source delete fails on Windows, and it fails in the most
misleading way available: the rename throws before anything is destroyed, so the operator sees "the cache
could not be deleted, retry" and the retry fails identically, forever. Nothing points at the lock. On
Linux the same code would keep working, so the change would pass review and CI on a non-Windows runner and
break only for the developers who have the cache locked open. The rename exists precisely to keep a partial
delete from stripping the `.clio-knowledge-source` ownership marker (see
[the-read-only-walk-must-stop-where-directory-delete-stops.md](the-read-only-walk-must-stop-where-directory-delete-stops.md)),
so losing it costs the recovery this whole area was rebuilt to provide.
