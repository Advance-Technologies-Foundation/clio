---
description: knowledge is refreshed by a get-guidance READ (stale-while-revalidate), bounded by the existing autoupdate.knowledge policy — not at startup, not by a timer, and not by a policy of its own; the MCP verbs are still excluded from the CLI startup update check
applies-to:
  - clio/Command/McpServer/Knowledge/KnowledgeRefreshTrigger.cs
  - clio/Command/McpServer/Knowledge/KnowledgeGuidanceSource.cs
  - clio/Command/McpServer/Knowledge/CuratedKnowledgeBootstrapService.cs
ticket: ENG-99899
date: 2026-09-22
---

**What is true** — knowledge refresh in an MCP host is triggered by a guidance read and bounded by the
ordinary `autoupdate.knowledge` policy. Four facts that are easy to get wrong:

- The startup path was NOT changed and must not be: a warm artifact-backed start still activates the
  cached generation with no network call, and `Program.ShouldSkipUpdateCheck` still excludes
  `mcp-server` / `mcp-http` from the CLI startup update check.
- `KnowledgeGuidanceSource.EnsureCurrent()` is the seam: activate (local, synchronous), then
  `TriggerIfDue()` (never blocks). Delete the second line and an MCP-only installation silently freezes
  on its first generation again — that is what `EveryRead_ShouldAskForARefresh_AfterActivating` pins.
- `KnowledgeRefreshTrigger` owns a 5-minute in-memory damper, a single-flight guard, and an eligibility
  check. Whether a refresh may run is
  `settingsRepository.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, now)`, which reads the
  operator's `enabled` flag and `frequency-minutes` and advances the persisted `next-run`. Because
  `next-run` is in `appsettings.json`, the rate limit is per MACHINE.
- **The eligibility check is load-bearing, not defensive.** The guidance source serves the whole
  process and `clio config` reaches it (through `KnowledgeFeedbackPolicyService`, which reads the
  `knowledge-feedback` article). A short-lived process abandons the refresh on exit, but
  `TryScheduleAutoupdate` has ALREADY persisted `next-run` to allow it — so the abandoned attempt burns
  the window and the resident host waits another `frequency-minutes`. Hence: hosts only
  (`McpHostTransport.Current` is `Stdio` or `Http`), never a worker, and never while
  `CLIO_NO_UPDATE_CHECK` is set. Do NOT reach for `Program.IsMcpServerMode` here — `IsMcpCommand`
  matches only `mcp-server`/`mcp`, so it silently excludes `mcp-http`, the host that runs for weeks.
- **The refresh does not download the bundle to find out whether it needs to.**
  `KnowledgeGitHubReleaseTransport.RetrieveCore` returns `NoCandidate` on a `304`, and again when the
  published revision equals the active one — both before `DownloadAsset`. That outcome is also what
  writes the publisher-check stamp. So the ordinary case is one conditional metadata request.

A newer generation activates on the next `get-guidance` call, with no restart, because
`KnowledgeMultiSourceActivator.EnsureActivated` re-reads the activation marker on every uncontended
call.

**Why it is this way** — four constraints pull against each other. Startup must stay offline and inside
its five-second budget, so the check cannot live there. A long-running `mcp-http` host serves for weeks,
so a one-shot post-start check is not enough. clio already had a knowledge update schedule with a
documented opt-out, so a component-owned interval would have been a second policy silently overriding
the operator. And a timer does work on hosts nobody reads guidance from, which is waste — staleness
only matters to a reader.

**What breaks if you ignore it** — moving the check onto the startup path regresses the guarantee the
warm-start branch exists for: an operator with no network waits on a failing call before MCP serves
anything. Putting it on the read path *synchronously*, or letting `TryScheduleAutoupdate` run there,
makes a `get-guidance` answer wait up to thirty seconds on a contended settings write lock. Replacing
the `TryScheduleAutoupdate` gate with a local interval re-breaks the opt-out: `"knowledge": { "enabled":
false }` stops meaning anything inside a host. Removing the damper turns every guidance lookup into an
`appsettings.json` read.
