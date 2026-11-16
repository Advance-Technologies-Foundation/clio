---
description: a test that writes the shared page ClioMcp_BlankPageToSave takes SeededBlankPage.LeaseAsync and the fixture's [TearDown] resets the page - the lease isolates the tests but FAILS the test on a page found not blank, a reset the first read does not show, a blank read of no or another schema, and two stored schemas in one package
applies-to:
  - clio.mcp.e2e/Support/Creatio/SeededBlankPage.cs
  - clio.mcp.e2e/PageSyncToolE2ETests.cs
  - clio.mcp.e2e/PageUpdateToolE2ETests.cs
  - clio.mcp.e2e/ClioPagesConcurrencyE2ETests.cs
  - clio.mcp.e2e/SeededBlankPageTests.cs
date: 2026-11-16
---

**What is true** — `PageSyncToolE2ETests` and `PageUpdateToolE2ETests` share one page on the stand and
build every probe body from the body they read first. A test that writes it starts with
`_seededPageLease = await SeededBlankPage.LeaseAsync(session, environmentName)` and the fixture's
`[TearDown]` ends the lease. Tests do not restore the page themselves (the visibility test restores
and checks its own restore, because that check is its subject). Dry-run tests take no lease, because
the reset is a write. The lease always brings the page back to blank, and it fails the test for every
state that only the product defect produces:

- **Start, page not blank** — reset, then fail ("found NOT blank before this test", with the section,
  the component names, the stored rows read before the reset). Only the first lease of the test
  process is exempt (process-wide flag): a developer's stand may start dirty.
- **Start, blank body but no editable schema** (`editable.editableSchemaExists=false`: the body
  `get-page` synthesizes) — the same: fail on any lease after the first, accept on the first, where a
  fresh site has no editable schema yet.
- **End (`[TearDown]`)** — the blank body is ALWAYS written, also when the page looks blank. The reset
  polls up to 6 reads so the next test starts clean, but it passes only when the FIRST read shows it:
  blank body, `editableSchemaExists=true`, and `editable.checksum` equal to the
  `pages[].digest.checksum` the reset save reported. A later read that shows it, or six blank reads
  with another checksum, fail the test.
- **Stored rows** — the first lease of each fixture lists the `SysSchema` rows of the page and fails
  when one package holds two. `ClioPagesConcurrencyE2ETests` (two simultaneous saves, the first writer
  on a fresh site) ends with the same check and requires exactly one row in the design package.
- **A failed test** — before the reset, `[TearDown]` writes what `get-page` resolves, the stored rows
  and what each `update-page` / `sync-pages` call of the test reported (`savedSchemaUId`, checksums) to
  the test output. `sync-pages` reports no schema UId; match its checksum to a stored row.

The decisions are pure functions (`Classify`, `DecideStart`, `WhyReadDoesNotShowReset`,
`JudgeResetReads`, `JudgeStoredRows`) and the two steps run over `IPageAccess`; `SeededBlankPageTests`
covers them without a stand.

**Why it is this way** — each test used to trust the cleanup of the test before it, and three of
those restores never checked their result. In TeamCity builds 16330337 and 16330482 one read that did
not show the latest save failed up to seven later tests with "no empty marker pair" errors. A reset
that only repairs the page would turn those red builds green and hide the defect: a first version of
the lease reset a non-blank page silently, waited six reads for its own write and accepted any blank
body. So the reset keeps the isolation and fails on the signature instead. The reset is in
`[TearDown]` because an exception thrown in `finally` replaces the assertion that failed in the test
body, while NUnit reports a teardown failure next to it.

**What breaks if you ignore it** — a new test that writes the page without a lease leaves its content
for the next test, and the next lease fails naming the wrong test. A restore added back into `finally`
hides the failure it was meant to clean up after. Relaxing a check of the lease (skip the write when
the page looks blank, accept the reset on a later read, drop the checksum comparison, reset a
non-blank page without failing) makes a build pass in which a completed save was not what the next
read returned. A failure of the lease is a report of the defect in
`McpServer/a-completed-sync-pages-write-is-not-always-visible-to-the-next-get-page.md`, not a flaky
test: read its message, do not retry it away.
