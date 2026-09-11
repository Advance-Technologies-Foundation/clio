---
description: a sync-pages call that returned success is not always visible to the very next get-page - the write completing and the write becoming readable are not the same instant, so an immediate read-back can return the pre-write body
applies-to:
  - clio/Command/McpServer/Tools/PageSyncTool.cs
  - clio.mcp.e2e/PageSyncToolE2ETests.cs
ticket: clio#1381
date: 2026-09-08
---

**What is true** — `sync-pages` returning `success:true` for a page does not guarantee that a `get-page`
issued immediately afterwards reads the body that was just written. Measured on the shared e2e stand,
`PageSyncToolE2ETests.PageSyncTool_Should_Make_Completed_Write_Immediately_Observable` failed **2 times in
60 CI runs** with a stale read-back body — the assertion looked for a unique marker written by the
preceding `sync-pages` and got back the pre-write content instead. The window is short: a bounded poll of
6 attempts at 1-second intervals closes it, and a genuinely immediate write still satisfies the first
attempt.

Nothing in `docs/knowledge/` or `spec/adr/` declares an immediate-visibility contract for `sync-pages`, so
there is no guarantee being violated here — the guarantee simply never existed. What existed was a test
asserting one.

**Why it is this way** — the tool's answer is tied to the save completing, not to the saved schema
becoming readable through the separate read path. A probe that lands inside that gap can also get back an
envelope `EntitySchemaStructuredResultParser.Extract` cannot parse yet, which is why the read-back poll
treats an `InvalidOperationException` from the parser as "not yet" rather than as an abort.

**What breaks if you ignore it** — two distinct failures, and neither looks like an eventual-consistency
problem at first glance.

An **agent** chaining `sync-pages` then `get-page` reads the old body and concludes its own write was
dropped. The natural response is to re-issue the write, which does nothing, or to go looking for a bug in
`PageSyncTool` that is not there.

A **test author** reads the old body as a `sync-pages` regression. The tempting fix is the opposite of the
right one: re-adding a fixed post-save delay. A fixed delay is both slower in the common case (it always
pays the full wait) and still unsound in the bad case (nothing proves the chosen constant is long enough).
Poll and stop on first observation instead — that is what the test does now, and the assertion message
names the budget it exhausted so a genuine regression is still distinguishable from this window.
