---
description: from platform build 10.1.409 (ENG-94368) a schema save no longer deletes the SysLocalizableValue rows of a removed process element parameter (BaseElements.* keys); they stay as orphans, and only a key a later save carries - an explicitly empty value included - is deleted or rewritten
applies-to:
  - clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs
  - clio.mcp.e2e/Support/Mcp/ProcessDesignerStoredReads.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-95985
date: 2026-11-05
---

**What is true** — up to platform build 10.1.408, `ConfigurationResourceDBWriter.WriteResources` deleted every
`SysLocalizableValue` row of the (schema, package, culture) and inserted the current set, so a removed parameter's rows
went with the next save. Platform commit `fc2d858db` (ENG-94368, 2026-07-29; first build `builds-windows/10.1.409`)
made the write a merge: it deletes and rewrites only the keys the save carries, an explicitly empty value included.
The obsolete-key clean-up (feature `CleanUpObsoleteSchemaLocalizableValues`, `SchemaManager.GetJudgeableResourceKeyGroups`)
judges only the `Columns`, `LocalizableStrings` and `Methods` groups, so a `BaseElements.<Element>.Parameters.*` key of
a removed parameter is never deleted. Measured with a Send email attachment slot on Studio 10.2.984 and on a trunk
10.2.996 build: the removed slot's four rows stayed; a later save that reused the slot name for a single File deleted
the orphaned outer `DisplayValue` (written explicitly empty) and rewrote the other three keys.

**Why it is this way** — the old write dropped stored values a save did not carry, which reverted overridden captions
(SR-01526603). The platform deletes a value only where it can prove the key obsolete, and it cannot tell a removed
element parameter from a value a live parameter skips (a formula, a complex type), so it never judges `Parameters`
keys. The ENG-95985 measurements EM1/EM2, which recorded "the save deletes a removed slot's rows", ran on 10.1.37.

**What breaks if you ignore it** — an assertion that a removed parameter's resource rows are gone passes before 10.1.409
and fails after it (TC-I-05 failed this way on the process-designer TeamCity lane from its first run). The user-visible
risk is a reused parameter name showing an orphan's value, so assert that no reused slot shows a removed row's value,
and write every key of a reused name, an empty one explicitly.
