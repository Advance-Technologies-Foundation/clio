---
description: the mobileRuntimeVersion marker in MobileComponentRegistry.json is published irregularly - it appeared and vanished within two hours while the catalog content was unchanged - so nothing gates on it and nothing reports it, yet the mapping is kept so a re-published marker cannot fall into the unmapped-fields bucket
applies-to:
  - clio/Command/McpServer/Tools/ComponentInfoTool.cs
  - clio/Command/McpServer/Tools/ComponentInfoCatalog.cs
  - clio.tests/Command/McpServer/ComponentRegistrySnapshotTests.cs
ticket: ENG-96589
date: 2026-09-21
---

**What is true** — the top-level `mobileRuntimeVersion` marker cannot be used to tell the registry
generations apart, and clio neither gates on it nor reports it.

It appeared on `/api/mcp/latest/` carrying `{release: main, commit: d7a0c3bb...}` and was gone again when
the producer republished the file at 14:15 GMT the same day (2026-09-17), roughly two hours after it was
first observed — while the CONTENT stayed runtime-derived: identical `references.baseInputs`,
byte-identical component entries, only `crt.DataGrid` (and the 7 type definitions that became unreachable
without it) deliberately removed. It has not been published since.

`ComponentRegistryEnvelope.MobileRuntimeVersion` and `ComponentCatalogState.MobileRuntimeVersion` still MAP
it, and that is deliberate although nothing reads them: without the mapping the marker lands in the
envelope's `[JsonExtensionData]` bucket, which no assertion covers, and a producer change goes unnoticed —
which is exactly how this field was missed the first time.
`Synthetic_MobileRuntimeVersion_Payload_Should_Map_Every_Field` exercises the mapping, because the pinned
snapshot carries no marker and the guard's marker branch is otherwise dead.

**Why it is this way** — a provenance field that survives two hours is not a contract. A feature gated on
it switches itself OFF with nothing failing: no exception, no warning, and a response that looks exactly
like a page which happened to have nothing to prune. The generation is decided by the inherited
`baseInputs` surface instead, and whether the prune ran is reported as `propertyPruneApplied`.

**What breaks if you ignore it** — two symmetric failures. Gate on the marker and the property prune stops
running on the current published catalog, silently, so every undeclared web property ships to the device
again. Report the marker to callers and the field is absent from every real conversion, so an agent reading
its absence as "the prune did not run" treats every correctly pruned page as un-pruned — the same ambiguity
`propertyPruneApplied` was added to remove. If the producer starts publishing it dependably, re-introducing
it is a response-field change, not a gate change.
