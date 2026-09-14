---
description: an MCP args record without an enforced [JsonExtensionData] check silently drops unbound JSON fields, so a mis-keyed payload returns success and does nothing
applies-to:
  - clio/Command/McpServer/Tools/SchemaSyncTool.cs
  - clio/Command/McpServer/Tools/GenerateSourceCodeTool.cs
  - clio/Command/McpServer/Tools/McpToolArgumentSupport.cs
ticket: GH-1303
date: 2026-09-03
---

**What is true** — MCP argument records bind through `BindingsModule.CreateMcpSerializerOptions()`, a copy of
`McpJsonUtilities.DefaultOptions` that does **not** set `JsonUnmappedMemberHandling.Disallow`. Any JSON field
that does not match a `[JsonPropertyName]` is therefore discarded by System.Text.Json without an error. Adding
`[JsonExtensionData]` to the record only *captures* those fields — it changes nothing on its own. The field is
only reported if the tool explicitly inspects the bag, which is what
`McpToolArgumentSupport.BuildLegacyAliasError` is for. `SchemaSyncOperation` had the bag since before this
change but read exactly one key out of it (`operation`, the legacy spelling of `type`), so every other unknown
field was still dropped.

**Why it is this way** — the loose binding is deliberate for forward compatibility across MCP SDK versions, and
turning `Disallow` on globally would reject payloads that older/newer clients legitimately decorate. The
per-tool overflow-bag check is the chosen middle ground: each tool keeps its own alias table and decides what
counts as unknown, so a genuinely tolerated field can be exempted (`ConsumedOperationExtensionFields` in
`SchemaSyncTool`).

**What breaks if you ignore it** — the caller gets `success: true` for work that never happened, and nothing in
the response hints at it. Concretely (issue #1303): `sync-schemas` `create-lookup` sent with rows under
`seed-data` — a plausible mis-spelling, because `seed-data` is a valid value of `type` — answered
`outcome: "created"` with messages about publishing the schema and registering the lookup, while the lookup was
empty; the reporter did this for 15 lookups and had to backfill every one. The same class of failure hit
`generate-source-code`, whose MCP surface had no `timeout` argument at all: passing one was ignored and the call
ran to the 60-minute ceiling. When you add or edit an MCP args record, add the overflow bag **and** a
`BuildLegacyAliasError` check over it, seeded from `McpToolArgumentSupport.EnvironmentNameAliases`; a bag with
no check is the failure mode, not the fix.

Note the second-order trap: on a rejection, do not echo the offending operation back inside a resume plan that
tells the caller to resubmit it verbatim — `SchemaSyncTool` tracks
`BatchExecutionState.FailedOperationIsResubmittableVerbatim` to keep a field-shape rejection out of
`resume-plan.operations`.
