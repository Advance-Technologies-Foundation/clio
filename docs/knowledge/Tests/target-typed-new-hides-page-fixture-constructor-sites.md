---
description: page command and tool fixtures construct with target-typed `PageUpdateCommand x = new(...)`, so grepping `new PageUpdateCommand(` finds only a third of the sites a new constructor parameter breaks
applies-to:
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/PageGetOptions.cs
  - clio.tests/Command/McpServer/PageToolsTests.cs
  - clio.tests/Command/McpServer/PageSyncToolTests.cs
ticket: ENG-91317
date: 2026-08-19
---

**What is true** — the page fixtures overwhelmingly use the target-typed form,
`PageUpdateCommand command = new(client, urlBuilder, logger, ...)`, rather than
`new PageUpdateCommand(...)`. Counting the current tree: 37 target-typed sites against 18 spelled-out
ones for `PageUpdateCommand`, and 17 target-typed sites for `PageUpdateTool`. The same holds for
`PageGetCommand`, `PageSyncTool` and `PageGetTool`.

**Why it is this way** — nothing forced it; the fixtures were written that way and the count is now
large enough that it is the de-facto style.

**What breaks if you ignore it** — adding a constructor parameter to one of these types and then
grepping `new PageUpdateCommand(` to find the call sites reports the minority of them. The rest
surface as a long compile-error list from a file you believed you had already covered, one framework
at a time, which reads as a flaky build rather than an incomplete edit. Search for the declared type
name (`PageUpdateCommand `) or for `= new(`, not for `new <Type>(`.
