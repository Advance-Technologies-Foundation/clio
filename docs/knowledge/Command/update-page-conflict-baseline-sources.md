---
description: update-page conflict baseline comes from .clio-pages/meta.json unless the caller pins one; the MCP checksum argument is what makes the caller's get-page read authoritative
applies-to:
  - clio/Command/PageBaselineGuard.cs
  - clio/Command/McpServer/Tools/PageBaselineStore.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
ticket: GH-1320
date: 2026-09-03
---

**What is true** — what `PageBaselineGuard.TryArm` arms depends on how the save was addressed, and
there are three cases. On the UNPINNED, non-redirected path all three fields come from
`.clio-pages/{schema}/meta.json`: the checksum, `ExpectedSchemaUId`, and the schema-absent marker
`ExpectedSchemaAbsent`. On a PINNED save (CLI `--expected-checksum`, MCP `checksum`) the caller's
checksum alone governs the comparison — neither identity field is armed from disk. On a REDIRECTED
save (`target-package-uid` / `target-schema-uid`) nothing is armed, the baseline is not even read, a
pin that was passed is dropped, and the response carries a warning that detection did not run. That
on-disk baseline is keyed by **(anchor directory, schema name)** only — not by schema UId — and the
anchor is resolved from the process cwd unless `output-directory` overrides it. It is rewritten both
by `get-page` and, post-save, by `RefreshOrDrop`.

Consequently a baseline can be present, environment-matched, and still not describe the body the
caller is editing: a different cwd between the `get-page` and the `update-page` call, or a post-save
value the server has since recomputed, both produce a `checksum-mismatch` conflict for an edit
nothing external touched.

The MCP `update-page` tool exposes a `checksum` argument for exactly this. Passing the
`editable.checksum` from the `get-page` response makes the comparison run against the body the caller
actually read. It does not weaken detection: a genuinely stale caller checksum still mismatches the
current `SysSchema.Checksum` and is still refused.

**A pin outranks the on-disk identity fields, because it is the more specific statement about the
same schema.** The checksum comparison runs against the schema this save resolved to, so a pin that
matches proves the caller read exactly that content and a stale on-disk `EditableSchemaUId` has
nothing left to establish. Detection is not weakened: a real identity change carries a different
checksum and is still refused, as `ChecksumMismatch` rather than `SchemaUIdMismatch`.
`schema-deleted-externally` still fires on a pinned, non-redirected save, because there the
resolution itself reports that a replacing schema must be CREATED while the caller pinned a checksum
for one that already exists. Arming the UId from disk instead made `BuildConflictErrorMessage` answer
a matching pin with "re-run get-page and retry", which re-pins the same checksum and loops, leaving
`force` as the only exit.

**A redirect makes the baseline inapplicable, not merely stale — so it is disarmed rather than
enforced.** `get-page` has no `target-package-uid` / `target-schema-uid`, so both baseline sources
describe the schema the hierarchy resolver picks automatically, never the one a redirect sends the
write to. Arming from it failed twice over: the write was refused as `schema-uid-mismatch` /
`schema-deleted-externally` for an edit nothing external had touched, and — because the guard also
reported armed — `RefreshOrDrop` then stamped the REDIRECTED schema's UId and checksum into a
`meta.json` keyed by schema name, corrupting the baseline of the automatically resolved schema so
that the next ordinary save of the same page was refused too. `TryArm` now returns not-armed with a
warning, which also keeps `RefreshOrDrop` away from that file. A warning and not `conflict: true`
deliberately: nothing about the write is wrong, it is only unverifiable, and a conflict would send
the caller into the retry loop and then to `force`.

**One scope limit is still open:** the remedy is on `update-page`
only. `sync-pages` is the tool clio calls the canonical page write path (`update-page` even carries a
`ToolDeprecation` saying so), and `PageSyncPageInput` has no `checksum` member — `BuildUpdateRequest`
never sets `ExpectedChecksum`, so every `sync-pages` write is on the unpinned path with `force: true`
as its only escape. An agent following clio's own guidance takes that path. Extending the
checksum contract to `sync-pages` is deliberately out of scope here and needs its own change.

**A pinned save always leaves a trace.** On the non-redirected path (a redirect returns its own
warning before any of this runs), `TryArm` warns whenever the caller pinned a checksum and no
on-disk baseline corroborates it - both when the baseline diverges and when none was matched at all
for the anchor and environment. The second case is not exotic: an explicit `output-directory`, or an
`--uri`/`--login` invocation that cannot satisfy `MatchesEnvironment`, both reach it, and the pin
still governs the comparison there because `TryCheckForExternalModification` gates on
`ExpectedChecksum` alone and never consults the armed flag.

**Why it is this way** — the on-disk baseline exists to protect plain CLI flows that have no way to
carry state between two process invocations. It is a fallback, not the truth.

**What breaks if you ignore it** — a tool surface that accepts no caller checksum silently drops it
and reports an external modification that never happened. The caller's only way forward is
`force: true`, which is the one flag that must stay reserved for real conflicts — so it gets used by
reflex, and the next genuine concurrent edit is overwritten without anyone noticing.
