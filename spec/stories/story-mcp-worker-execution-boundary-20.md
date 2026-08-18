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


## Update 2026-08-18: the allowlist half of the hypothesis is now a fact

Checked directly. The child environment allowlist carries `PATH`, `HOME`, `USERPROFILE`,
`LOCALAPPDATA`, `APPDATA`, `SystemRoot`, `SystemDrive`, `windir`, `COMSPEC`, the temporary-directory
spellings, the `DOTNET_ROOT` family, `DOTNET_HOST_PATH`, `CLIO_HOME`, `LANG`, `LC_ALL` and — added
today — the six proxy spellings. It does **not** carry `CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS`.

So the mechanism is established: the end-to-end test forces a 0.05 second interval on the parent so a
single backend round trip deterministically produces a beat, the child never receives it, the child
uses the default, and a fast `list-app-sections` finishes well inside that default. Zero beats.

**Do not stop there and call it a broken test.** Two things follow, and only one of them is about the
test:

1. The test's instrument is defeated, which explains the red. Adding the variable to the allowlist
   would turn it green.
2. **A worker does not honour heartbeat configuration the parent was given.** That is a behaviour
   difference an operator hits in production, not a test artifact: someone who tunes the interval gets
   it in the parent and silently does not get it in any worker. The same argument the proxy variables
   just won applies here — the allowlist's own remarks claim it "carries every spelling the host may
   have used", and it does not.

AC-01 still stands, and is now sharper rather than answered: making the test green must not be
mistaken for proving that a worker's progress notifications reach the client at all. That second
question is untouched by the allowlist, and it is the one that would matter.


## Update 2026-08-18, later in the same run: the eleven failures are one cascade

The run ended at eleven failures, not five, and reading the messages changes what the list means.

`GetRelatedPageAddon_ShouldReportInvalidEnvironment_WhenEnvironmentMissing` expected an
environment-not-found error and got this instead:

```
clio settings bootstrap is broken. Repair [redacted-path]
Explicit uri/login/password remains available only as an emergency fallback.
```

That is not a variant of the expected failure. **The settings file was damaged during the run.** The
six failures that appeared after it are almost all "Could not parse <tool> MCP result" —
`get-fsm-mode`, `list-packages`, `find-entity-schema`, `get-record-rights`, the second
`get-related-page-addon` — which is what every tool does once it cannot read settings.

So this is one cascade with one root cause, not eleven independent problems, and the count is
misleading in both directions: most of the eleven are consequences, and the root cause is worse than
any single one of them.

### What the root cause most likely is, and why the branch owns it

The first failure in this family, appearing well before the cascade, is
`AppSettings_Should_YieldAWholeCatalog_When_ReadDuringRegWebAppWrites` — **a test this branch added**,
which deliberately performs concurrent registration writes against the settings catalogue to prove the
interprocess gate holds. It failed. Everything downstream that needs settings then failed too.

Read together, the most economical explanation is that the gate does not hold, the concurrent writes
tore the settings file, and the rest of the suite inherited the damage. That would mean two things at
once, and both matter:

1. **The gate is not doing its job for the settings catalogue.** Story 9 gave `.clio-pages` an
   interprocess gate and established that DbHub was already safe. The settings file was not given the
   same treatment, and this run is the first evidence about it either way.
2. **A test in this branch damages shared state for every test after it.** Even once the gate is
   fixed, a test that can corrupt the suite's own settings file must not run against the shared one.

### What is NOT yet proven, and must not be assumed

The ordering above is consistent with causation but does not establish it: the later tests could
simply belong to a batch that ran afterwards for unrelated reasons. The settings file being damaged is
directly evidenced by the message; that the concurrent-write test damaged it is inference.

The step that settles it costs one run: execute `AppSettings_Should_YieldAWholeCatalog_When_ReadDuringRegWebAppWrites`
alone on the build host and inspect the settings file afterwards. If it is intact, the cascade has a
different cause and this section is wrong.

### Consequence for AC-05 and AC-06

Both stand, and one is now sharper: whatever else is true, **a branch whose own test can corrupt the
settings file for the remainder of the suite does not merge.** That is independent of how the three
regressions above are eventually explained.
