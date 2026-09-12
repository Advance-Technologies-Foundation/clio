---
description: Identity removal cannot ignore unresolvable foreign IIS paths as non-overlapping, even though CRM target selection catches normalization failures
applies-to:
  - clio/Common/IIS/IdentityServiceLifecycle.cs
  - clio/Common/IIS/DirectoryPathIdentity.cs
ticket: clio-1433
date: 2026-09-12
---

**What is true** — An unavailable or Win32-ambiguous foreign IIS path blocks identity folder
deletion. Restore access or correct the stale mapping before retrying.

**Why it is this way** — The alternative of catching normalization failures and returning
"does not overlap" was rejected. An unresolved alias can still address the deletion tree.
The CRM uninstaller's tolerant path-equality predicate selects a target; this predicate instead
must exclude every other consumer before deleting files, so the two fallbacks are not interchangeable.

**What breaks if you ignore it** — A failed physical-path lookup would silently authorize deletion
of files that a foreign IIS mapping still uses. Textual path inequality cannot establish safety.
