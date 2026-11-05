---
description: from Creatio 10.2 a schema save no longer deletes the SysLocalizableValue rows of a removed process element parameter (BaseElements.* keys); 10.1 did, so a removed attachment slot's caption and display-value rows stay orphaned
applies-to:
  - clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs
  - clio.mcp.e2e/Support/Mcp/ProcessDesignerStoredReads.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-95985
date: 2026-11-05
---

**What is true** — on 10.1 (`builds-windows/10.1.37`) `ConfigurationResourceDBWriter.WriteResources` deleted every
`SysLocalizableValue` row of the schema, package and culture and inserted the current set, so the rows of a parameter
the schema no longer declares disappeared on the next save. From the platform trunk of 2026-08-15 (commit `53e60e3504c`,
in every 10.2 build, e.g. `builds-windows/10.2.984`) the write is a merge: it deletes and rewrites only the keys the save
carries. The separate obsolete-key clean-up (feature `CleanUpObsoleteSchemaLocalizableValues`,
`SchemaManager.GetJudgeableResourceKeyGroups`) judges only the `Columns`, `LocalizableStrings` and `Methods` groups, so
a `BaseElements.<Element>.Parameters.<Parameter>.*` key of a removed parameter is kept by every save - the
CrtProcessBuilder's and, by the same `SchemaManager` path, the designer's (by code; not measured on a 10.2 designer).
A key the save carries with an EMPTY value is still deleted, which is why a reused slot whose outer display value is
written explicitly empty shows nothing of the row it replaced. Measured on TeamCity, Studio 10.2.984 PostgreSQL: after a
Send email replace removed `Attachments3`, its four rows (`Caption`, `DisplayValue [#Docs#]`, item `Caption`, item
`DisplayValue`) were still stored; the same on the dev stand built from trunk 10.2.996 (2026-11-05). On that stand a
LATER request that reused `Attachments3` for a single File left no `[#Docs` row under it: the explicit-empty outer
`DisplayValue` deleted the orphan, and the other three keys were rewritten (TC-I-05, passed).

**Why it is this way** — the platform keeps a deleted value only where the localization change log records it, and it
cannot tell a removed element parameter from a value a parameter skipped (a formula, a complex type), so it never calls
a `Parameters` key obsolete. The ENG-95985 measurements EM1/EM2 that recorded "the save deletes a removed slot's rows"
were taken on stand `Creatio`, core 10.1.37.

**What breaks if you ignore it** — an assertion that a removed parameter's resource rows are gone passes on 10.1 and
fails on every 10.2 stand (TC-I-05 failed this way on the process-designer TeamCity lane from its first run). The
user-visible risk is a reused parameter name showing an orphan's value: assert that no reused slot shows a removed row's
value, not that the rows are gone.
