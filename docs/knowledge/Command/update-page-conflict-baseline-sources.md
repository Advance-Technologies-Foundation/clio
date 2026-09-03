---
description: update-page conflict baseline comes from .clio-pages/meta.json unless the caller pins one; the MCP checksum argument is what makes the caller's get-page read authoritative
applies-to:
  - clio/Command/PageBaselineGuard.cs
  - clio/Command/McpServer/Tools/PageBaselineStore.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
ticket: GH-1320
date: 2026-09-03
---

**What is true** — `PageBaselineGuard.TryArm` prefers a caller-pinned `PageUpdateOptions.
ExpectedChecksum` and otherwise arms the check from `.clio-pages/{schema}/meta.json`. That on-disk
baseline is keyed by **(anchor directory, schema name)** only — not by schema UId — and the anchor is
resolved from the process cwd unless `output-directory` overrides it. It is rewritten both by
`get-page` and, post-save, by `RefreshOrDrop`.

Consequently a baseline can be present, environment-matched, and still not describe the body the
caller is editing: a different cwd between the `get-page` and the `update-page` call, or a post-save
value the server has since recomputed, both produce a `checksum-mismatch` conflict for an edit
nothing external touched.

The MCP `update-page` tool exposes a `checksum` argument for exactly this. Passing the
`editable.checksum` from the `get-page` response makes the comparison run against the body the caller
actually read. It does not weaken detection: a genuinely stale caller checksum still mismatches the
current `SysSchema.Checksum` and is still refused.

**Why it is this way** — the on-disk baseline exists to protect plain CLI flows that have no way to
carry state between two process invocations. It is a fallback, not the truth.

**What breaks if you ignore it** — a tool surface that accepts no caller checksum silently drops it
and reports an external modification that never happened. The caller's only way forward is
`force: true`, which is the one flag that must stay reserved for real conflicts — so it gets used by
reflex, and the next genuine concurrent edit is overwritten without anyone noticing.
