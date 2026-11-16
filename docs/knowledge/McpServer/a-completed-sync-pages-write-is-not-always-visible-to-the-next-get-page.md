---
description: a sync-pages call that returned success is not always visible to the next get-page - the read can return the pre-write body for more than 6 s, or an older saved body minutes later; cause not established, the page e2e tests print what each call resolved
applies-to:
  - clio/Command/McpServer/Tools/PageSyncTool.cs
  - clio/Command/McpServer/McpWorkerCohort.cs
  - clio/Command/PageGetOptions.cs
  - clio/Command/PageUpdateOptions.cs
  - clio.mcp.e2e/PageSyncToolE2ETests.cs
  - clio.mcp.e2e/Support/Creatio/SeededBlankPage.cs
ticket: clio#1381
date: 2026-11-16
---

**What is true** — `sync-pages` returning `success:true` for a page does not guarantee that a `get-page`
issued afterwards reads the body that was just written. Two measurements on the e2e stand:

- September 2026: `PageSyncTool_Should_Make_Completed_Write_Immediately_Observable` failed 2 times in 60
  runs with the pre-write body on a single read. A poll of 6 reads, 1 s apart, was added.
- 16 November 2026 (TeamCity builds 16330337 and 16330482, commits that touch no page code): the same
  poll returned the pre-write body on all 6 reads; a later read returned the body of an OLDER save; a
  pinned save answered `success` with the checksum it was pinned to; a stale-baseline save was not
  refused. So it is not only a short settling window.

No story, ADR or tool contract promises read-after-write for `sync-pages`. It is stated in two places
only: the test's name, and the remarks of `McpWorkerCohort.StageSixNames`, which keep `get-page` in the
host process because "read-after-write inside one agent session is exactly what an agent relies on".

**Why it is this way** — not established. clio keeps no cache on this path: `get-page` reads the
hierarchy from `ClientUnitSchemaDesignerService/GetParentSchemas` and the checksum from `SysSchema` on
every call. Two things in the code make a divergence possible. The body comes from the server's schema
designer, which `PageUpdateCommand.TryResetScriptCache` already documents as able to return the
pre-save hierarchy. And both tools find the schema by name with `rowCount: 1` and no order
(`PageSchemaMetadataHelper.QuerySysSchemaRow`, `QueryExistingSchemaInPackage`), so two stored schemas
of one page in one package would let a read and a write resolve different rows. Whether the stand
holds such rows is what the next failure will show: `SeededBlankPage` lists the `SysSchema` rows of
the page, and the visibility test prints the editable schema, checksum and design package of each read.

**What breaks if you ignore it** — an agent chaining `sync-pages` then `get-page` reads an old body and
concludes its write was dropped; a test author reads the same thing as a `sync-pages` regression and
adds a fixed delay or a longer poll. Neither is a fix: a delay does not help when the read resolves
another schema, and a longer poll in the visibility test would hide the one assertion that reports
the defect. Read the printed diagnostics first.
