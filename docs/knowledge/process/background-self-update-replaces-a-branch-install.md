---
description: an opted-in globally installed clio self-updates at startup (RunStartupUpdateCheck), so a branch build installed as the global tool is silently replaced mid-measurement - disable it with clio autoupdate --disable or CLIO_NO_UPDATE_CHECK
applies-to:
  - clio/Program.cs
  - clio/Command/Update/SetAutoupdateCommand.cs
ticket: ENG-88474
date: 2026-08-19
---

**What is true** — eligible clio invocations run `Program.RunStartupUpdateCheck` before the command.
When automatic clio updates are enabled and due, this upgrades the installed tool in the background (`Updating clio X -> Y in background...`). If you
installed your branch build as the global tool in order to measure its behaviour, a later invocation
can quietly restore the released package underneath you. Turn it off for the duration of the
measurement with `clio autoupdate --disable`, or set `CLIO_NO_UPDATE_CHECK` for a spawned process
tree.

**Why it is this way** — clio updates are disabled by default, but explicit opt-ins are preserved. `ShouldSkipUpdateCheck` exempts only the update verbs themselves, `mcp-server` / `mcp`, `mcp-http`,
`--version` and the help flags, because those are the paths where an update would be actively
harmful. Since issue #1462 an ordinary CLI invocation ALSO defers the clio self-update while a
resident `mcp-server` host has a presence marker in the clio home — so a measurement run is not
protected by starting an MCP session either; that deferral lapses the moment the host exits. Nothing exempts "the operator is deliberately running a non-released build" — the tool has
no way to know that.

**What breaks if you ignore it** — the measurement stays green and measures the wrong binary. Half a
run executes your branch, the rest executes release, and the difference reads as flakiness or as an
unreproducible behaviour change rather than as a swapped tool. Because the MCP transports are exempt
from the check, an MCP session can keep running your branch while the shell has already been
downgraded, so the two surfaces disagree about the same claim.
