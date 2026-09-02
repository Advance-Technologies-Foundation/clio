---
description: KnowledgeManagedTreeDeleter clears read-only with manual TopDirectoryOnly recursion and UNLINKS directory reparse points rather than skipping them, because SearchOption.AllDirectories descends through links and because a recursive Directory.Delete cannot remove a tree containing a junction
applies-to:
  - clio/Command/McpServer/Knowledge/KnowledgeManagedTreeDeleter.cs
  - clio/Command/McpServer/Knowledge/KnowledgeSourceInstallationStore.cs
  - clio/Command/McpServer/Knowledge/KnowledgeSourceManagementService.cs
ticket: ENG-96211
date: 2026-08-30
---

**What is true** — deleting a knowledge tree has to clear the read-only attribute first (Git creates
`*.pack` / `*.idx` read-only and Windows refuses to delete a read-only file), and that walk must use manual
recursion with `SearchOption.TopDirectoryOnly`, never descending into a directory whose `ReparsePoint` bit is
set. `Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)` is the wrong tool for this and looks
right: it binds `EnumerationOptions.CompatibleRecursive` (`AttributesToSkip = 0`,
`IgnoreInaccessible = false`), so it **descends into directory symlinks and junctions** — verified on
Windows: a file behind a symlinked subdirectory is enumerated. Nothing behind a link is ever deleted, so
clearing read-only bits there mutates state outside the managed root.

A directory reparse point the walk meets is **unlinked**, not merely skipped — with a non-recursive delete,
which removes the link and leaves its target alone. This record used to say the opposite ("`Directory.Delete`
does the opposite and unlinks the reparse point, so the walk reaches strictly further than the delete"), and
that sentence was wrong in the one direction that costs money: a recursive delete meeting a **junction**
removes the link and then throws, leaving the tree undeletable. That false premise shipped and blocked a
release. See
[recursive-directory-delete-throws-on-a-junction-child.md](../platform/recursive-directory-delete-throws-on-a-junction-child.md)
for the measured matrix.

**Why it is this way** — the two consumers (`KnowledgeSourceInstallationStore`,
`KnowledgeSourceManagementService`) share one `IKnowledgeManagedTreeDeleter` rather than a copied private
method, because the duplicate is exactly how the wrong walk shipped in two classes at once.
`EnsureNoReparsePoint` does not cover this: it validates the ancestor chain *upward* from a path and never
looks at a descendant.

**What breaks if you ignore it** — two failures, both silent. On macOS/Linux git materialises symlinks by
default, so a Git knowledge source whose repository contains a directory symlink is checked out verbatim;
deleting it then walks *through* the link and clears the read-only bit on files outside the managed root
that are never deleted — the worst reachable variant being the rollback of a checkout clio has **just
rejected as untrusted**. Second, with `IgnoreInaccessible = false` an unreadable subdirectory throws from
`MoveNext`, i.e. from the enumerator and outside any per-entry `try`, before `Directory.Delete` is reached:
a delete that previously succeeded now fails, leaving the source unregistered with its cache intact — the
"not owned by Clio" dead end that no command can clear and that this whole area exists to remove.
