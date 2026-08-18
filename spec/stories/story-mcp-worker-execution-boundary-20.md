# Story 20: a cohort tool stopped streaming progress

Found 2026-08-18 by TeamCity run 15893259 on this branch. **This is a regression introduced by
stage 6**, confirmed against the master baseline (run 15892347), where the same test passes.

## The failure

`Clio.Mcp.E2E.ApplicationSectionToolE2ETests.ApplicationTool_Should_Stream_Progress_For_LongRunning_Call`

```
Expected progress.Count to be greater than or equal to 1 because a long-running application tool must
stream at least one progress notification so the client resets its inactivity timeout instead of
timing out, but found 0 (difference of -1).
```

The test drives `list-app-sections` — a stage 6 cohort member — through the real clio MCP server with
a progress sink, having forced `CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS = 0.05` on the server process so
that a single backend round trip deterministically produces a beat.

## Why this matters more than one red test

`list-app-sections` declares `RequiresClientRequests = Progress`. The client-visible consequence of
zero beats is precisely what the assertion says: a client that relies on progress to reset its
inactivity timeout will time out mid-operation. That is the failure mode this feature exists to
remove, arriving through a different door.

## Leading hypothesis, NOT yet confirmed

The child worker's environment is built from an allowlist —
`WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist` — which carries `PATH`, `HOME`,
`CLIO_HOME`, the `DOTNET_ROOT` family and the locale variables, and does NOT carry
`CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS`. The child would then use the default interval, a fast
`list-app-sections` would finish well inside it, and no beat would ever fire.

If that is the whole story, the test's own instrument is what broke and the fix belongs with the same
allowlist gap that drops the proxy variables. **But it must be proven, not assumed**, because the
alternative is much worse: that progress notifications from a worker do not reach the client at all
for cohort tools, which would mean the relay's forwarding is not doing what four unit tests claim.

## Acceptance criteria

- AC-01 The cause is established by evidence, not by elimination. Distinguish "the child never emitted
  a beat" from "the child emitted beats that never reached the client" — those are different defects
  with different fixes, and only one of them is a test-instrument problem.
- AC-02 A cohort tool declaring `Progress` streams at least one notification to the client through a
  worker, asserted at the unit level so it is not gated on a live stand.
- AC-03 The e2e test passes on TeamCity.
- AC-04 Whatever the cause, the allowlist question is answered explicitly: which variables a child must
  inherit for its behaviour to match the parent's, and why each one is on or off the list. The list's
  own remarks claim it "carries every spelling the host may have used", which is currently false.
