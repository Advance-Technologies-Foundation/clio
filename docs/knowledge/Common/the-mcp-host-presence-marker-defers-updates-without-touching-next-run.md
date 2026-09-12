---
description: mcp-server writes <clio home>/mcp-server.<pid>.lock while it is resident, and RunStartupUpdateCheck must test for it BEFORE TryScheduleAutoupdate, which advances next-run as part of deciding an update is due
applies-to:
  - clio/Common/McpHostPresence.cs
  - clio/Environment/ISettingsRepository.cs
  - clio/Program.cs
  - clio/Command/McpServer/McpServerCommand.cs
date: 2026-09-12
---

**What is true** — a non-worker `mcp-server` host writes a presence marker named
`mcp-server.<pid>.lock` into the clio home and deletes it on graceful shutdown. A separate clio CLI
process scans for one before its startup update check and, when a live one exists, skips **only** the
clio self-update. A marker whose pid is no longer running is deleted by the scan.

The ordering is load-bearing: `ISettingsRepository.TryScheduleAutoupdate` advances `next-run` and
saves it **as part of** answering "is this due", before the update callback runs. So the deferral has
to wrap the whole `RunIfDue(..., AutoUpdateTarget.Clio, ...)` call. Knowledge and toolkit updates are
not deferred - they replace no loaded assembly and no settings section.

**Why it is this way** — nothing inside the resident host can answer this question, because the
process that has to ask is a different one; a file in the shared clio home is the only channel the
two have. Liveness goes through `IProcessLivenessProbe` so a unit test can describe a dead pid
without creating or killing a real process, and `ISettingsRepository.IsAutoupdateDue` exists purely
so the notice can ask whether the update would have run without moving the schedule to find out.

**What breaks if you ignore it** — a deferral made *inside* the `RunIfDue` callback still skips the
update, but `next-run` has already moved a full frequency window (8 hours for clio) into the future.
The next cold start, with no resident host and nothing in the way, then reports nothing is due, and
the update the deferral was supposed to postpone by minutes is postponed by hours - invisibly,
because deferring and succeeding look identical from outside.
