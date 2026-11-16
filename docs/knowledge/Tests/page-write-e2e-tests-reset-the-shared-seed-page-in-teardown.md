---
description: a test that writes the shared page ClioMcp_BlankPageToSave takes SeededBlankPage.LeaseAsync and the fixture's [TearDown] resets the page - the lease isolates the tests but FAILS the test on a page found not blank, a blank read of no or another schema than the last verified reset, a reset the first read does not show or whose save did not move the stored ModifiedOn, and two stored schemas in one package
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
  process is exempt: a developer's stand may start dirty, and the code cannot know whether the page
  was ever saved on this site.
- **Start, blank body but no editable schema** (`editable.editableSchemaExists=false`: the body
  `get-page` synthesizes) — the same: fail on any lease after the first, accept on the first.
- **Start, blank but not what the last verified reset left** — the process-wide `LeaseLedger`
  remembers the editable schema UId and checksum of the first read after the previous lease's verified
  reset. A later lease that reads the page blank from another UId, or the same UId with another
  checksum, resets and fails. Nothing is compared when the previous lease did not end verified or when a
  test wrote the page outside a lease since: `ClioPagesConcurrencyE2ETests` calls
  `SeededBlankPage.RecordWriteOutsideLease` before its two saves (the only writer outside a lease; the
  unleased page tests are dry runs, which do not write).
- **What a later lease's failure says** depends on how the previous lease ended (`PreviousLeaseEnd`):
  it blames the product only after a verified reset with no writer outside a lease since; after a
  failed reset ("did NOT end with a verified reset") or a lease that never recorded its end (its
  TearDown or lease start threw: "did not complete") it says the state may be what that test left.
- **End (`[TearDown]`)** — the blank body is ALWAYS written, also when the page looks blank. The reset
  polls up to 6 reads so the next test starts clean, but it passes only when the FIRST read shows it:
  blank body, `editableSchemaExists=true`, `editable.checksum` equal to the `pages[].digest.checksum`
  the reset save reported, AND the stored `SysSchema.ModifiedOn` of the schema that read resolved
  moved past its value read before the save (a blank-over-blank save that did not land has the same
  body and, if the checksum is content-only, the same checksum). Both `ModifiedOn` values come from
  `execute-esq` on the site, so the agent's clock takes no part; `get-page`'s `editable.modifiedOn` is
  not used, because the server formats it from a parsed date in its own culture, to whole seconds. A
  later read that shows the reset, six blank reads with another checksum, or a `ModifiedOn` that did not
  move fail the test.
- **Stored rows** — the first lease of each fixture lists the `SysSchema` rows of the page and fails
  when one package holds two; on any lease but the first of the process it also fails when the rows
  cannot be read or a row carries no package. `ClioPagesConcurrencyE2ETests` logs the rows and what
  `get-page` resolves before and after its two simultaneous saves (the "after" also in `finally`),
  logs whether the design package held no schema of the page before the race (only then did it test two
  simultaneous FIRST saves - it is the first writer only when it runs first), and requires exactly one
  row in the design package and a `get-page` that resolves that row's UId and checksum.
- **A failed test** — before the reset, `[TearDown]` writes what `get-page` resolves, the stored rows
  and what each `update-page` / `sync-pages` call of the test reported (`savedSchemaUId`, checksums) to
  the test output. `sync-pages` reports no schema UId; match its checksum to a stored row. Every lease
  log line is written to the output when it is observed, so an exception later in the lease does not
  lose it.

The decisions are pure functions (`Classify`, `DecideStart`, `WhyReadDoesNotShowReset`,
`JudgeResetReads`, `JudgeResetLanded`, `JudgeStoredRows`, `JudgeReadAgainstDesignPackageRow`), the two
steps run over `IPageAccess`, and the ledger and the lease's end take their dependencies as arguments
(`LeaseLedger`, `IToolResultObservable`); `SeededBlankPageTests` covers them without a stand.

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
