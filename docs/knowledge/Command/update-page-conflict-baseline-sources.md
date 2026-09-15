---
description: update-page conflict baseline comes from .clio-pages/meta.json unless the caller pins one; the MCP checksum argument is what makes the caller's get-page read authoritative
applies-to:
  - clio/Command/PageBaselineGuard.cs
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/McpServer/Tools/PageBaselineStore.cs
  - clio/Command/McpServer/Tools/PageUpdateTool.cs
  - clio/Command/McpServer/Tools/PageSyncTool.cs
ticket: GH-1320, GH-1464, GH-1538
date: 2026-09-15
---

**What is true** — what `PageBaselineGuard.TryArm` arms depends on how the save was addressed, and
there are three cases. On the UNPINNED, non-redirected path all three fields come from
`.clio-pages/{schema}/meta.json`: the checksum, `ExpectedSchemaUId`, and the schema-absent marker
`ExpectedSchemaAbsent`. On a PINNED save (CLI `--expected-checksum`, MCP `checksum`) the caller's
checksum alone governs the comparison — neither identity field is armed from disk. On a REDIRECTED
save (`target-package-uid` / `target-schema-uid`) the disk baseline is READ but arms nothing: its
schema identity travels as `ConditionalBaselineSchemaUId` and the comparison is decided only after
the target is resolved, and a caller pin is retained for comparison with that resolved target; the
response warns that the disk baseline did not apply directly. That
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

**A redirect makes the disk baseline inapplicable, not merely stale — so it is skipped rather than
enforced.** `get-page` has no `target-package-uid` / `target-schema-uid`, so both baseline sources
describe the schema the hierarchy resolver picks automatically, never the one a redirect sends the
write to. Arming from it failed twice over: the write was refused as `schema-uid-mismatch` /
`schema-deleted-externally` for an edit nothing external had touched, and — because the guard also
reported armed — `RefreshOrDrop` then stamped the REDIRECTED schema's UId and checksum into a
`meta.json` keyed by schema name, corrupting the baseline of the automatically resolved schema so
that the next ordinary save of the same page was refused too. `TryArm` now returns not-armed with a
warning, which also keeps `RefreshOrDrop` away from that file. A caller-supplied checksum is retained
and compared with the resolved target after hierarchy resolution; this protects a target-package-uid
write when it names the existing package and fails safe with a checksum conflict when the pin came from
another schema. With no explicit pin, the write is only unverifiable, so the warning is not
`conflict: true` and does not send the caller into a retry loop.

**A selector that resolves to the page's OWN schema is not a redirect, and the refresh decision is
separate from the conflict decision.** `PromoteConditionalBaselineWhenTargetMatches` compares
`ConditionalBaselineSchemaUId` with the resolved `EditableSchemaUId` and, on a match, sets
`ConditionalBaselineApplied` — which is what the post-save `refreshBaseline || ConditionalBaselineApplied`
gate reads in all three writers. On a match with NO caller pin the disk baseline is also promoted to
govern the conflict check; with a pin the pin keeps that role and the match decides only the refresh.
Tying the two together (GH-1538) meant a successful PINNED same-target save left `meta.json` holding a
superseded checksum, and the caller's next UNPINNED save of the same page conflicted with its own
previous save. The cost of the fix is one baseline read under its lock on a pinned selector save — the
same read the unpinned selector path already performed.

**`sync-pages` carries the same contract.** `PageSyncPageInput` has a per-page `checksum`, and
`BuildUpdateRequest` passes it VERBATIM into `PageUpdateOptions.ExpectedChecksum` — `TryArm` is the
single normalization chokepoint (it trims, and collapses whitespace-only to "not supplied"), so a
second trim at the mapper could only drift from `update-page`'s rule. Every case above therefore
applies per page. Until GH-1464 that tool — the one clio calls the canonical write path, and the one
`update-page`'s own `ToolDeprecation` points callers at — had no checksum member at all, so every
`sync-pages` write was on the unpinned path with `force: true` as its only escape.

**A pinned save always leaves a trace.** `TryArm` warns whenever the caller pinned a checksum and no
on-disk baseline corroborates it - both when the baseline diverges and when none was matched at all
for the anchor and environment. The second case is not exotic: an explicit `output-directory`, or an
`--uri`/`--login` invocation that cannot satisfy `MatchesEnvironment`, both reach it, and the pin
still governs the comparison there because `TryCheckForExternalModification` gates on
`ExpectedChecksum` alone and never consults the armed flag.

The selector path emits the SAME divergence trace, and that is a consequence of the refresh rule
above rather than a cosmetic addition: once a pinned selector save can refresh `meta.json`, staying
silent would let `RefreshOrDrop` overwrite the only local record that the pin ever diverged from the
baseline - the bypass `AppendPinnedBaselineDivergenceWarnings` exists to expose. It also surfaces the
corrupt-`meta.json` read warning the non-selector path already accumulated.

**Why it is this way** — the on-disk baseline exists to protect plain CLI flows that have no way to
carry state between two process invocations. It is a fallback, not the truth.

**What breaks if you ignore it** — a tool surface that accepts no caller checksum silently drops it
and reports an external modification that never happened. The caller's only way forward is
`force: true`, which is the one flag that must stay reserved for real conflicts — so it gets used by
reflex, and the next genuine concurrent edit is overwritten without anyone noticing.
