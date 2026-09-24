---
description: During a modify request the sub-process CALLER's in-memory element already carries the callee's re-synchronized captions, but the resource manager its caption is bound to still answers the STORED ones - measured twice, including on cold caches, although two independent source traces predicted the opposite; CrtProcessBuilder's caption report reads there instead of SysLocalizableValue
applies-to:
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
  - clio.mcp.e2e/SubProcessElementToolE2ETests.cs
ticket: ENG-100077
date: 2026-09-24
---

**What is true** — the platform re-synchronizes every sub-process element on each design-time load, so by the time
CrtProcessBuilder holds the caller its element parameters carry the callee's captions. The resource manager the
element's caption is bound to (`LocalizableValue.ResourceManager`; for a design instance the workspace manager
named after the schema's UId) still answers what the caller STORED. Measured on a .NET Framework stand on
2026-09-24: a marker build answered "E1 date A2" while the sync wrote "E1 date A3", and after `restart-web-app`
the shipped reader answered "E1 date A4" while the sync wrote "E1 date A5"
(`spec/eng-100077-subprocess-caption-sync/…-measurement-log.md`, M1/M2). A `modify-as-new-version` clone is bound
to its SOURCE's manager by `ReadSchemaMetaData` and rebound to its own only in `ProcessEditPipeline`'s tail
(`InitializeLocalizableValues`), so during the operations it answers the source's stored captions. From
CrtProcessBuilder 1.6.6.19 `StoredCaptionReader` reads there; 1.6.6.16-1.6.6.18 ran a `Select` on
`SysLocalizableValue` instead.

**Why it is this way** — the owner chose the manager: it is the platform's standard resource mechanism, the one
the designer and the runtime read, it costs no extra database read, and keeping nodes in step is its job, not the
package's. The database read had been chosen on the strength of a trace (the design-load sync writes into the
in-memory values, the session's resource snapshot is serialized from them, the manager is refilled from that
snapshot). The measurement refuted it; a later review traced the same chain again and proposed that M1 was
confounded by a stale callee metadata instance, which M2 on cold caches refuted in turn.

**What breaks if you ignore it** — the trace is convincing and will be re-derived: "the manager holds the synced
captions, so read the database" brings back a second read path, the copy-to-original mapping a version clone
needed, and the per-request cache, all of which the manager made unnecessary. Moving the pipeline's rebind in
front of the operations makes every caption report on a version edit silently empty (TC-C38). Two limits are
real and accepted: the manager is app-pool-wide and ANY design session reloads it from its own snapshot
(`SchemaManager.FindDesignItem` → `UpdateResourceManager`), so a notice's "from" text can be another session's
snapshot; and a version resolves missing keys through its family root. Neither changes what is saved.
