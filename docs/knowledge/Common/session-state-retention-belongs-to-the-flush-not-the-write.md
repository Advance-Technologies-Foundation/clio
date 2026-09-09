---
description: session-state retention is already handled by TelemetryFlushService.PruneSessions on every flush - do not add a per-event sweep to TelemetryService, it is redundant, weaker and runs under the process-wide lock
applies-to:
  - clio/Common/Telemetry/TelemetryService.cs
  - clio/Common/Telemetry/TelemetryFlushService.cs
  - clio/Command/McpServer/Tools/SendTelemetryTool.cs
ticket: ENG-92551
date: 2026-08-31
---

**What is true** — the `sessions/` directory is pruned by `TelemetryFlushService.PruneSessions`, which is
the FIRST statement of `FlushCoreAsync` — ahead of the events-directory existence check, the endpoint check
and the consent check, so it runs on every flush even when nothing will be uploaded. It applies a 30-day
cutoff **and** a 500-file cap. `SendTelemetryTool.SendTelemetry` is the only caller of `TelemetryService.Send`
and schedules a flush after every recorded event, so the prune is always reached. `TelemetryService` itself
therefore does no retention work, and `UpdateSessionState` deliberately does not sweep.

**Why it is this way** — state is keyed per `(session_id, workflow)` rather than per session, so it grows
faster than it used to, and the temptation is to reclaim it where it is written. That path is the worst one
available: `Send` holds a STATIC `SyncRoot` shared by every `TelemetryService` in the process, so a sweep
there enumerates the whole directory and issues one `GetLastWriteTimeUtc` per file, on the caller's thread,
under a process-wide lock, once per event — to delete files the background flush had already taken. The ADR
(`spec/adr/adr-product-telemetry.md`) names the flush-time prune as the design.

**What breaks if you ignore it** — recording one event stops being O(1) and becomes O(retained state files),
paid synchronously by the MCP tool call. A sweep added there also has to pick a clock, and that is a trap of
its own: it compares against a filesystem mtime, so a test whose injected `TimeProvider` sits more than the
retention ahead of the real clock deletes the anchor it just wrote and duration inference silently returns
null for a reason the test never expressed. This was written once and removed in the same pull request.
