---
description: a sync-pages call that returned success is not always visible to the next get-page - the read can return the pre-write body for more than 6 s, or an older saved body minutes later; cause not established, the page e2e tests fail on it (stale or non-blank state, two stored schemas) and print what each call resolved
applies-to:
  - clio/Command/McpServer/Tools/PageSyncTool.cs
  - clio/Command/McpServer/McpWorkerCohort.cs
  - clio/Command/PageGetOptions.cs
  - clio/Command/PageUpdateOptions.cs
  - clio.mcp.e2e/PageSyncToolE2ETests.cs
  - clio.mcp.e2e/Support/Creatio/SeededBlankPage.cs
  - clio.mcp.e2e/ClioPagesConcurrencyE2ETests.cs
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
of one page in one package would let a read and a write resolve different rows. `update-page` creates
the replacing schema of a page after a lookup with no lock around lookup-and-save, so two simultaneous
first saves can leave such a pair; `ClioPagesConcurrencyE2ETests` makes exactly those two saves. It
tests that case only when the design package holds no schema of the page before it runs (it ran before
the page fixtures in the builds checked, but NUnit order is no contract), and it logs whether it did.

The e2e suite does not tolerate the gap, it reports it. Every one of these fails a test, with what each
call resolved and the stored `SysSchema` rows in the message:

- the visibility test: the marker is not read within its 6-read poll; its own restore is not what the
  single next read returns;
- `SeededBlankPage` (every test that writes the seed page): a reset that the FIRST read does not show
  (the poll after it only keeps the next test clean); a blank read that resolves no editable schema or
  carries another checksum than the reset save reported; a reset save that did not move the stored
  `ModifiedOn` of the schema the first read resolved; a page found not blank, or blank from another
  schema or checksum than the previous verified reset left, at the start of a test (first lease of the
  process exempt; the message blames the product only when the previous lease ended verified and
  nothing wrote the page outside a lease since);
- two stored schemas of the page in one package: at the end of the concurrency test (a deliberate
  tripwire: exactly one row in the design package, and `get-page` must resolve that row's UId and
  checksum) and at the first lease of each page fixture.

**What breaks if you ignore it** — an agent chaining `sync-pages` then `get-page` reads an old body and
concludes its write was dropped; a test author reads the same thing as a `sync-pages` regression and
adds a fixed delay or a longer poll. Neither is a fix: a delay does not help when the read resolves
another schema, and a longer poll in the visibility test, or a lease that waits for its reset and
passes, would hide the assertions that report the defect. Read the printed diagnostics first: the
editable schema UId and checksum of each read against the checksum each save reported and the stored
rows tell "the read resolved another schema" from "the save did not land".
