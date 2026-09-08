---
description: ResolveForRead / ResolveCanonicalOutput return the symlink-resolved path; Resolve still returns the lexical one, and the OData file contract must use the canonical entry points
applies-to:
  - clio/Command/OutputPathConfinement.cs
  - clio/Command/McpServer/Tools/ODataFileContract.cs
ticket: "1221"
date: 2026-09-01
---

**What is true** — `OutputPathConfinement` has two kinds of entry point. `Resolve` returns the LEXICAL
absolute path (the long-standing behaviour every schema-writing tool depends on), while
`ResolveForRead` and `ResolveCanonicalOutput` return the CANONICAL, symlink-resolved path. The OData
file contract uses the canonical pair, opens the file ONCE, reads its length and its bytes from that
same handle, and calls `RevalidateResolved` while the handle is still open.

**Why it is this way** — confinement was decided on the symlink-resolved path but the LEXICAL path was
handed back, and the caller then performed two further opens (a metadata probe, then the read). An
intermediate component swapped between those steps redirected the size check and the read at different
files, one of them outside the allowed roots. Only the OData entry points were switched to the
canonical form: changing `Resolve` would move the returned path for every unrelated caller and for
their tests.

**What breaks if you ignore it** — reading through `Resolve` (or reopening a path between validation
and use) restores the disclosed read/write escape: the checks pass on one file and the I/O lands on
another. Note a related pre-existing quirk this does NOT fix: a symlink with an ABSOLUTE target is
taken verbatim, so on macOS a link pointing at `/var/...` is compared against a temp root resolved to
`/private/var/...` and an in-bounds path is rejected. A relative link target resolves correctly.
