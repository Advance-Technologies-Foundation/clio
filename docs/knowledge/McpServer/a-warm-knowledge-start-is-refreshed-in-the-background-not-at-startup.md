---
description: an MCP host refreshes knowledge through the existing autoupdate.knowledge policy evaluated INSIDE the running host (30 s after start, re-read every 5 min) — not at startup, and not through a policy of its own; the MCP verbs are still excluded from the CLI startup update check
applies-to:
  - clio/Command/McpServer/Knowledge/CuratedKnowledgeBackgroundRefresh.cs
  - clio/Command/McpServer/Knowledge/CuratedKnowledgeBootstrapService.cs
  - clio/Command/McpServer/McpServerCommand.cs
  - clio/Command/McpServer/McpHttpServerCommand.cs
ticket: ENG-99899
date: 2026-09-21
---

**What is true** — knowledge refresh for an MCP host is the ordinary `autoupdate.knowledge` policy,
evaluated from inside the serving process. Three facts that are easy to get wrong:

- The startup path was NOT changed and must not be: a warm artifact-backed start still activates the
  cached generation with no network call, and `Program.ShouldSkipUpdateCheck` still excludes
  `mcp-server` / `mcp-http` from the CLI startup update check.
- `CuratedKnowledgeBackgroundRefresh` owns only two timings — a 30 s start delay and a 5-minute
  wake-up. Whether a wake-up does anything is decided entirely by
  `settingsRepository.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, now)`, which reads the
  operator's `enabled` flag and `frequency-minutes` and advances the persisted `next-run`.
- `next-run` lives in `appsettings.json`, so the rate limit is per MACHINE: several MCP hosts, or a
  host plus a CLI command, produce one update between them.

A newer generation activates on the next `get-guidance` call, with no restart, because
`KnowledgeMultiSourceActivator.EnsureActivated` re-reads the activation marker on every uncontended
call.

**Why it is this way** — three constraints pull against each other. Startup must stay offline and
inside its five-second budget, so the check cannot live there. A long-running `mcp-http` host serves
for weeks, so a one-shot post-start check would not be enough. And clio already had a knowledge update
schedule with a documented opt-out, so a component-owned interval would have been a second policy that
silently overrode the operator's setting.

**What breaks if you ignore it** — moving the check onto the startup path regresses exactly the
guarantee the warm-start branch exists for: an operator with no network waits on a failing call before
MCP serves anything. Replacing the `TryScheduleAutoupdate` gate with a local interval re-breaks the
opt-out: `"knowledge": { "enabled": false }` stops meaning anything inside a host, and a machine
running several hosts pays one publisher call per host per interval instead of one in total.
