---
description: a test that writes the shared page ClioMcp_BlankPageToSave takes SeededBlankPage.LeaseAsync and the fixture's [TearDown] resets the page - a restore in a finally block hides the test's own failure and one failed restore fails every later test
applies-to:
  - clio.mcp.e2e/Support/Creatio/SeededBlankPage.cs
  - clio.mcp.e2e/PageSyncToolE2ETests.cs
  - clio.mcp.e2e/PageUpdateToolE2ETests.cs
date: 2026-11-16
---

**What is true** — `PageSyncToolE2ETests` and `PageUpdateToolE2ETests` share one page on the stand and
build every probe body from the body they read first. A test that writes it starts with
`_seededPageLease = await SeededBlankPage.LeaseAsync(session, environmentName)`: the page is read and,
when it is not blank, reset through `sync-pages` with the save and its read-back asserted. The
fixture's `[TearDown]` ends the lease, which does the same after the test. Tests do not restore the
page themselves. Dry-run tests take no lease, because the reset is a write.

**Why it is this way** — each test used to trust the cleanup of the test before it, and three of
those restores never checked their result. In TeamCity builds 16330337 and 16330482 one read that did
not show the latest save failed up to seven later tests with "no empty marker pair" errors. The reset
is in `[TearDown]` because an exception thrown in `finally` replaces the assertion that failed in the
test body, while NUnit reports a teardown failure next to it: in build 16330482 the cleanup assertion
of the visibility test hid whether the visibility assertion itself had passed.

**What breaks if you ignore it** — a new test that writes the page without a lease leaves its content
for the next test whenever it fails midway, and the next test then fails while building its probe,
naming the wrong test. A restore added back into `finally` hides the failure it was meant to clean up
after. The reset cannot repair a stand whose reads do not show completed writes; it fails with the
page's stored layers in the message instead.
