# Story 20: the end-to-end delta this branch owes an explanation

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


## The full end-to-end delta (TeamCity 15893259 vs master baseline 15892347)

Taken at 62 percent of the run, so the list may still grow — and the baseline's own four
`ThemingSandboxE2ETests` failures had not been reached yet, so they are absent from this list rather
than fixed.

**Three tests passed on master and fail here. These are regressions.**

| Test | First read |
|---|---|
| `ApplicationTool_Should_Stream_Progress_For_LongRunning_Call` | The progress case above. |
| `CreateWorkspace_Should_Create_Empty_Workspace_When_Directory_Is_Omitted` | "Directory omitted" means the command uses the process working directory — and stage 6 changed which directory a worker starts in. That is the first thing to check, and it is checkable without a stand. |
| `ApplicationGetInfo_Should_Read_Virtual_Entity_After_SchemaSync` | No hypothesis yet. Could be environmental; do not assume either way. |

**Two are this branch's OWN new tests, and they fail.** Neither exists in the baseline run, so they
were added by wave 2 alongside the interprocess file gates (story 9):

- `AppSettings_Should_YieldAWholeCatalog_When_ReadDuringRegWebAppWrites`
- `BrowserSessionCache_Should_NeverExposeATornRead_When_WrittenConcurrently`

Both are concurrency tests over shared files, and both are exactly the hazard the design named: separate
address spaces isolate memory, not the filesystem. A green local unit suite says nothing about them —
they are end-to-end, and the end-to-end suite is not run by GitHub CI, so this is the first time they
have executed since they were written.

Read that carefully before deciding what it means. Two readings, and they need different work:

1. The gates genuinely do not hold under real concurrent processes, in which case story 9 is not done
   and the browser-session cache and the settings catalogue both need the treatment `.clio-pages` got.
2. The tests are flaky on the build host — shared temporary state, a fixed path, or a timing
   assumption. This repository has a documented history of exactly that.

Do not fix on hypothesis 1 without evidence, and do not dismiss as hypothesis 2 without evidence
either. Re-run them in isolation on the build host first; that single step separates the two.

## Additional acceptance criteria

- AC-05 Every one of the five is classified as regression, pre-existing, or flaky, with the evidence
  that decides it — not with an argument from plausibility.
- AC-06 The two new gate tests either pass on the build host or their failure is explained and the gate
  is fixed. A test this branch added, failing the first time it runs, is not something to carry into a
  merge.
