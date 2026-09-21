# Running the knowledge autoupdate schedule inside an MCP host

- **Status:** Accepted; implemented
- **Date:** 2026-09-21
- **Feature:** `knowledge-bundle-runtime`
- **Ticket:** ENG-99899
- **Amends:** [`knowledge-bundle-runtime-github-release-delivery.md`](knowledge-bundle-runtime-github-release-delivery.md)

## Context

clio already refreshes knowledge on a schedule. `autoupdate.knowledge` is enabled by default with a
60-minute frequency (`clio/Common/AutoUpdateSettings.cs`), and `Program.RunStartupUpdateCheck` calls
`IKnowledgeSourceManagementService.Update(null)` whenever that policy is due at the start of an
ordinary CLI command.

Two deliberate decisions together took every MCP-only installation out of that path:

1. `Program.ShouldSkipUpdateCheck` returns true for `mcp-server`, `mcp`, `mcp-http` and for
   `IsMcpServerMode`, so a host process never evaluates the schedule. Correct in itself — the check
   runs before the transport starts, and startup must not wait on the network.
2. The GitHub Release delivery model made a warm start fully offline: when a published generation is
   already cached, the bootstrap activates it and returns before it would contact the publisher.

So an installation used only through MCP — which is the normal case for an agent host — refreshed its
guidance exactly once, at its first cold start, and then never again. A `clio mcp-http` service task
could hand agents months-old guidance with no visible signal; `StaleCacheThresholdDays = 3` only
formatted a warning to stderr. Concretely: a cache on 1.15.38 while 1.15.46 was published.

## Decision

Keep the startup path exactly as it is, and evaluate the **existing** `autoupdate.knowledge` policy
from inside the running host: `CuratedKnowledgeBackgroundRefresh`, started by both hosts (stdio and
HTTP) after the bootstrap, ended by the host's shutdown token. Workers never start it.

The whole gate is one call:

```csharp
if (settingsRepository.TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, now)) {
    sourceManagementService.Update(sourceAlias: null, cancellationToken);
}
```

`TryScheduleAutoupdate` answers false for a disabled policy and for one whose `next-run` has not
arrived, and advances and persists `next-run` as part of answering true. Consequences, all of them
free because the mechanism already existed:

- an operator's documented opt-out (`"knowledge": { "enabled": false }`) stops the in-host refresh too;
- `frequency-minutes` sets the cadence, so the delay bound is configurable rather than hard-coded;
- `next-run` lives in settings, so concurrent clio processes — several MCP hosts, or a host and a CLI
  command — cost one update between them, not one each;
- there is no second policy for an operator to discover, and no new on-disk state.

Only two timings belong to this component:

| Timing | Value | What it governs |
|---|---|---|
| Start delay | 30 s | How long after the transport starts serving the first schedule check may run. It keeps the loop off the pre-serve budget. |
| Wake-up interval | 5 min | How often the schedule is re-read. Granularity only: whether anything happens is the policy's decision. |

Worst-case delay for a newly published release to reach a running host is therefore the configured
frequency plus one wake-up interval — about 65 minutes on the defaults. A newer generation activates
on the next guidance lookup, because the activator re-reads the marker on every call; no restart is
involved.

### Deliberate properties

- **The startup path is untouched.** No network call was added before the transport serves, so the
  offline warm start and its five-second budget are unchanged.
- **Every enabled source, not just the built-in one.** `Update(null)` is what the CLI autoupdate path
  runs; refreshing only `creatio-curated` would give one setting two meanings and leave a partner
  library behind.
- **Failures stay quiet.** A failed check writes a debug line, keeps the cached generation serving and
  retries on the next wake-up. An operator with no network would otherwise collect one warning every
  five minutes, and the 3-day staleness warning at startup remains the signal that a host stayed
  behind.
- **The loop is non-throwing end to end** and runs on a dedicated long-running thread: it is
  fire-and-forget, so a fault would surface only as an unobserved exception at collection time, and
  its wait blocks its thread for the whole host lifetime.

## Alternatives rejected

- **A second, component-owned interval** (the first draft: a 6-hour minimum publisher interval gated
  on the on-disk freshness marker). It duplicated a policy clio already had, ignored the operator's
  `autoupdate.knowledge` opt-out, and gave a worse and unconfigurable bound.
- **Make the stale threshold act at startup** (contact the publisher within the startup budget when
  the cache is older than three days). Smallest diff, but it puts a network call back on the pre-serve
  path and regresses the warm-path budget the acceptance criteria protect.
- **A one-shot post-start check.** Fixes a restart, not a host that runs for weeks — the case the
  ticket names as the worst one.
- **Remove the MCP exclusion from `ShouldSkipUpdateCheck`.** That would run the check before the
  transport starts, which is exactly what the exclusion exists to prevent.

## Documentation

`clio/docs/commands/mcp-server.md`, `clio/docs/commands/mcp-http.md`, their `clio/help/en/*.txt`
counterparts and `clio/docs/commands/autoupdate.md` state where the schedule is evaluated, what
governs it and what the resulting bound is, so the refresh policy can be read without opening
`CuratedKnowledgeBackgroundRefresh`.
