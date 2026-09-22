# Read-triggered knowledge refresh

- **Status:** Accepted; implemented
- **Date:** 2026-09-22
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

So an installation used only through MCP — the normal case for an agent host — refreshed its guidance
exactly once, at its first cold start, and then never again. A `clio mcp-http` service task could hand
agents months-old guidance with no visible signal; `StaleCacheThresholdDays = 3` only formatted a
warning to stderr. Concretely: a cache on 1.15.38 while 1.15.46 was published.

## Decision

Keep the startup path exactly as it is, and make **a guidance read** the refresh trigger — the same
shape the component registry behind `get-component-info` already uses in this repository
(`ComponentRegistryClient`: serve the cached copy, revalidate beside the answer). `get-guidance`
returns the active generation immediately; when the cache is due for verification the lookup starts
the publisher check without blocking on it.

`KnowledgeGuidanceSource` — the guidance read surface — gains one line beside the activation it
already performs:

```csharp
private void EnsureCurrent() {
    _activator.EnsureActivated();   // local, synchronous: picks up a generation another process installed
    _refreshTrigger.TriggerIfDue(); // never blocks: starts a refresh for the NEXT reader
}
```

`KnowledgeRefreshTrigger` owns three things and no schedule of its own:

- **An eligibility check.** The refresh runs only in a long-lived MCP host — `McpHostTransport.Current`
  is `Stdio` or `Http`, and `McpWorkerEnvironment.IsWorkerProcess` is false — and only while
  `CLIO_NO_UPDATE_CHECK` is unset. This is load-bearing, not defensive: `KnowledgeGuidanceSource` serves
  the whole process, and an ordinary CLI verb reaches it (`clio config` reads the `knowledge-feedback`
  article). In a short-lived process the stale-while-revalidate premise inverts — the process abandons
  the refresh on exit, but `TryScheduleAutoupdate` has already advanced and persisted `next-run` in
  order to allow it, so the abandoned attempt CONSUMES the window and pushes the next one, CLI or host,
  a full `frequency-minutes` out. Without this gate the change would defeat itself. A worker is excluded
  for the same reason it is excluded from the worker path: it serves one call and exits. The transport is
  read through an injected reader, mirroring `McpWorkerPathGate`, so the process shape is stateable in a
  test instead of requiring a real host.
- **A damper.** A 5-minute in-memory interval plus a single-flight guard, so the hot path is one clock
  comparison and one interlocked read. Guidance is read many times a minute and asking the schedule
  touches `appsettings.json` under a lock, which must not happen per lookup.
- **A hand-off.** In a background task: `TryScheduleAutoupdate(AutoUpdateTarget.Knowledge, now)` and,
  when it answers true, `Update(sourceAlias: null)`.

Note which signal decides the host. `Program.IsMcpServerMode` is NOT it: `Program.IsMcpCommand` matches
only `mcp-server` and `mcp`, so keying on it silently excludes `mcp-http` — the host this feature exists
for. `McpHostTransport.Current` is set by both entry points (`Program.cs` to `Stdio`,
`McpHttpServerCommand` to `Http`) and is fail-closed at `Unknown`.

Everything else is the existing autoupdate policy's decision:

- an operator's documented opt-out (`"knowledge": { "enabled": false }`) stops the read-triggered
  refresh too;
- `frequency-minutes` sets the cadence, so the delay bound is configurable rather than hard-coded;
- `next-run` lives in settings, so concurrent clio processes — several MCP hosts, or a host and a CLI
  command — cost one update between them.

### Why the check is already cheap

No "check-only" transport method was added, because the retrieval path does not download anything it
does not need. `KnowledgeGitHubReleaseTransport.RetrieveCore` fetches `releases/latest`, conditional on
the stored ETag, and returns `NoCandidate` either on `304 Not Modified` or when the selected asset's
revision equals the active one — before `DownloadAsset`. `KnowledgeSourceManagementService` records the
publisher-check stamp on exactly that outcome. So the ordinary refresh is one conditional metadata
request: no download, no signature verification, no generation switch. The bundle is fetched only when
the publisher really moved.

### Deliberate properties

- **The startup path is untouched.** No network call was added before the transport serves, so the
  offline warm start and its five-second budget are unchanged.
- **A host nobody asks contacts nobody.** Unlike a timer, the trigger costs nothing on an idle host —
  and an idle host serving nothing stale harms no one.
- **No request ever waits on the publisher.** The refresh runs in a background task; the reader is
  answered from the generation that is already active.
- **Every enabled source, not just the built-in one.** `Update(null)` is what the CLI autoupdate path
  runs; refreshing only `creatio-curated` would give one setting two meanings and leave a partner
  library behind. A `git` override is therefore refreshed too, exactly as any CLI command already
  refreshes it when the policy is due.
- **Failures never reach the reader, and are not silent either.** A failed refresh can never surface to
  the agent that happened to trigger it. The FIRST failure in a process is a warning carrying the
  per-source diagnostics (`Success=false` is how a refused signature or an unreachable publisher
  arrives, not an exception); every later one drops to a debug line, so an operator with no network does
  not collect one warning per window. Plain `WriteDebug` alone would have been invisible: it is a no-op
  without `--debug`, which a service task does not have. A settings-write refusal is warned separately
  and always, sharing `Program.SettingsWriteRefusalReported` so the operator hears it once per process
  whichever path hit it.

## Alternatives rejected

- **A background polling loop in both hosts** (the first implementation, PR #1652). It worked and was
  verified live, but it duplicated the trigger `get-component-info` already establishes, added a
  long-running thread with its own shutdown ordering and cancel-before-dispose hazard, and it did work
  on idle hosts that nothing would have read. Superseded by this document.
- **Gating the read path itself** (`if (Program.IsMcpServerMode) _refreshTrigger.TriggerIfDue();`).
  Rejected twice over: it keys on a signal that excludes `mcp-http`, and it puts run-mode policy on a
  read surface where no test can state it. The eligibility belongs inside the trigger, which is where
  it is asserted.
- **A component-owned refresh interval** (an early draft: a 6-hour minimum publisher interval gated on
  the on-disk freshness marker). It ignored the operator's `autoupdate.knowledge` opt-out and gave a
  worse, unconfigurable bound.
- **Make the stale threshold act at startup.** Puts a network call back on the pre-serve path and
  regresses the warm-path budget the acceptance criteria protect.
- **Remove the MCP exclusion from `ShouldSkipUpdateCheck`.** Would run the check before the transport
  starts, which is what the exclusion exists to prevent.

## Scope note

`KnowledgeReferenceExampleService` (`list-knowledge-examples`) also activates knowledge and does NOT
trigger a refresh. Deliberate: the trigger belongs on the surface that is read constantly, and adding
it to a rarely-called tool would buy nothing a following `get-guidance` does not already buy.

## Documentation

`clio/docs/commands/mcp-server.md`, `clio/docs/commands/mcp-http.md`, their `clio/help/en/*.txt`
counterparts and `clio/docs/commands/autoupdate.md` state what triggers a refresh, what governs it and
what it costs, so the policy can be read without opening `KnowledgeRefreshTrigger`.
