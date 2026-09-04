---
description: update-page conflict baseline comes from .clio-pages/meta.json unless the caller pins one; the MCP checksum argument is what makes the caller's get-page read authoritative
applies-to:
  - clio/Command/PageBaselineGuard.cs
  - clio/Command/McpServer/Tools/PageBaselineStore.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
ticket: GH-1320
date: 2026-09-03
---

**What is true** — `PageBaselineGuard.TryArm` splits the baseline in two, and only one half is
conditional. The CHECKSUM comparison prefers a caller-pinned `PageUpdateOptions.ExpectedChecksum` and
otherwise reads `.clio-pages/{schema}/meta.json`. The SCHEMA-IDENTITY half is armed from that same
on-disk baseline on **both** paths — `ExpectedSchemaUId` unconditionally, and only the schema-absent
marker (`ExpectedSchemaAbsent`) is withheld when the caller pinned a checksum. That on-disk
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

**Two scope limits, both deliberate and both still open.** First, the remedy is on `update-page`
only. `sync-pages` is the tool clio calls the canonical page write path (`update-page` even carries a
`ToolDeprecation` saying so), and `PageSyncPageInput` has no `checksum` member — `BuildUpdateRequest`
never sets `ExpectedChecksum`, so every `sync-pages` write is on the unpinned path with `force: true`
as its only escape. An agent following clio's own guidance takes that path. Second, because
`ExpectedSchemaUId` is armed from disk even on a pinned save, the two schema-identity checks run
BEFORE the checksum comparison: a save that redirects with `target-package-uid` / `target-schema-uid`,
or one whose on-disk `EditableSchemaUId` is stale, can be refused as `schema-deleted-externally` /
`schema-uid-mismatch` even though the pin matches the server exactly — and `BuildConflictErrorMessage`
answers that with "re-run get-page and retry", which re-pins the same checksum and loops. It fails
safe (a write is blocked, never corrupted), but the only exit is the `force` reflex this record exists
to remove. Both are tracked for their own change; see PR #1356's review threads.

**Why it is this way** — the on-disk baseline exists to protect plain CLI flows that have no way to
carry state between two process invocations. It is a fallback, not the truth.

**What breaks if you ignore it** — a tool surface that accepts no caller checksum silently drops it
and reports an external modification that never happened. The caller's only way forward is
`force: true`, which is the one flag that must stay reserved for real conflicts — so it gets used by
reflex, and the next genuine concurrent edit is overwritten without anyone noticing.
