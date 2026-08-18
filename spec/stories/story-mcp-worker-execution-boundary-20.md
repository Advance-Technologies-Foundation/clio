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


## Final numbers, and what they are worth

Run 15893259 finished: **46 failed, 470 passed, 98 ignored** (614 recorded).
Master baseline 15892347: **4 failed, 585 passed, 10 ignored** (599 recorded).

The ignored count is the tell. Ten became ninety-eight. Tests do not skip because a feature regressed;
they skip because the environment they need is unreachable. Together with the "clio settings bootstrap
is broken" message, that is the signature of a run whose settings file died partway through and never
recovered.

**So this run cannot be read as a per-test delta, and it should not be quoted as one.** Forty-six is
not a count of defects; most of it is one root cause propagating, and the ninety-eight skips are the
same cause in a different disguise. Anyone comparing 46 against the baseline's 4 will draw a
conclusion the evidence does not support.

What the run DID establish, and it is worth more than the delta would have been:

- The settings file can be damaged during a run, with a first-class error message proving it.
- The branch's own gate test for concurrent registration writes fails.
- Three named tests that pass on master fail here, observed before the collapse and therefore not
  explained by it: the progress-streaming case, `CreateWorkspace` with the directory omitted, and the
  application-info read after a schema sync.

What it did NOT establish: anything about the tools that ran after the collapse. Their results are
void — neither evidence of a regression nor evidence of health.

The next run must not start until the concurrent-write question is settled, or it will spend an hour
producing another uninterpretable number.

## RESOLVED 2026-08-18: the cause, proven — and it is neither hypothesis this story offered

The isolated run this story asked for was executed. **The test passed and the settings file was
intact.** Read literally that refutes the leading hypothesis — but the hypothesis was aimed at the
wrong mechanism. The defect is not *tearing*, it is *targeting*: the writes never went where the test
believed. An isolated run leaves a whole file and still proves the defect, so "intact" settles
nothing on its own and must not be quoted as a clean bill of health.

### Fact 1 — the test's isolation is inert (proven)

`SettingsRepository.AppSettingsFolderPath` returns `CLIO_HOME` **verbatim** and only falls through to
`HOME` / `LOCALAPPDATA` when it is unset (`clio/Environment/ConfigurationOptions.cs`, "the single
source of truth for clio's home directory"). `TestConfiguration.Load` injects the suite-owned
`CLIO_HOME` into `ProcessEnvironmentVariables` for **every** spawned clio process
(`clio.mcp.e2e/Support/Configuration/TestConfiguration.cs:30-32`).

`AppSettings_Should_YieldAWholeCatalog_When_ReadDuringRegWebAppWrites` set only `HOME`/`LOCALAPPDATA`.
So its comment — "an ISOLATED clio home. Without it this test would race real reg-web-app writes
against the developer's own environment catalog" — described an isolation that did not exist.

Measured two ways. Direct probe:

```
$ CLIO_HOME=/tmp/probe-A HOME=/tmp/probe-B clio info --settings-file
/tmp/probe-A/appsettings.json
$ CLIO_HOME=/tmp/probe-A HOME=/tmp/probe-B clio reg-web-app cat-a ...
# cat-a lands in /tmp/probe-A; /tmp/probe-B stays empty
```

And observed during the real isolated run, by snapshotting the suite home while it executed:

```
Environments: [bench2, bench3, bench4, c2f_probe_0818, cat-a, cat-b, cat-c, cat-d,
               cat-e, cat-f, probe0823, sae_m_seeenu_15827662_0818, stubwedge]
```

All six `cat-*` in the **shared** catalog every other fixture resolves against. The private home the
test created and deleted was never written to at all.

### Fact 2 — "settings bootstrap is broken" does NOT mean the file was damaged (proven)

`SettingsBootstrapService` sets `CanExecuteEnvTools = resolvedEnvironment is not null` — that is, "the
`ActiveEnvironmentKey` resolves to a configured environment". A catalog that is whole, valid and
parseable still fails that test if its active key points nowhere. Driven through the real MCP server
on two files differing in one key and nothing else:

| `ActiveEnvironmentKey` | Result |
|---|---|
| `"d2"` — resolves | the ordinary "a configured environment name or an explicit URI is required" error |
| `"gone"` — does not resolve | `clio settings bootstrap is broken. Repair [redacted-path] …` |

**So this story's earlier conclusion — "The settings file was damaged during the run" — is not
supported by the message that was used to establish it.** The message proves an unresolvable active
key, nothing more.

### Fact 3 — what actually happened on the build host (primary source)

Pulled from TeamCity 15893259 rather than inferred. The 46 failures bucket cleanly:

| Bucket | Count | Signature |
|---|---|---|
| unresolvable active key | 24 | `clio settings bootstrap is broken. Repair [redacted-path]` where an environment-not-found error was expected |
| shared-file contention | 18 | `IOException: The process cannot access the file 'C:\TSAgent\temp\buildTmp\clio-mcp-e2e-shared-home-…\appsettings.json' because it is being used by another process` |
| downstream | 4 | `Could not parse <tool> MCP result` |

The contention bucket names the shared home **in the exception text**, with stack frames landing
inside `TemporaryClioSettingsOverride`:

- `CreateWorkspace_Should_Create_Empty_Workspace_When_Directory_Is_Omitted` → `SetWorkspacesRoot` line 30
- `SettingsHealth_Should_Report_Repaired_Status_When_Active_Environment_Key_Is_Invalid` → `ReplaceContent` line 42
- `AppSettings_Should_YieldAWholeCatalog…` → `reg-web-app cat-a` exited 1 with
  `The process cannot access the file because it is being used by another process`

Nine fixtures rewrite that one shared file through a plain, non-atomic `File.WriteAllText` that takes
none of the cross-process locks a real clio writer takes, and one of them
(`SettingsHealthToolE2ETests`) **deliberately installs a catalog whose `ActiveEnvironmentKey` does not
resolve**, relying on a `Dispose` restore to contain it. A sharing violation that kills the fixture
before or during that restore leaves the deliberate breakage in place for the rest of the run — which
is the 24-test bucket, and the 10→98 skip explosion (every reachability probe then fails into
`Assert.Ignore`).

**Why master is green:** all nine writers are pre-existing, but none of them spawns six real
`reg-web-app` processes doing locked read-modify-writes on that file over several seconds. This branch
added the tenth writer and it is the one that opens the window wide. **The branch owns the cascade —
but through inert test isolation, not through the interprocess gate. The gate was never involved.**

### AC-05 — every failure classified, with the evidence that decides it

| Test | Verdict | Evidence |
|---|---|---|
| `AppSettings_Should_YieldAWholeCatalog…` | **branch defect, fixed** | inert isolation, proven twice above |
| `CreateWorkspace_…_When_Directory_Is_Omitted` | **NOT a regression in its own behaviour** — casualty of the same contention | died in `ArrangeAsync` → `SetWorkspacesRoot` on the shared-home `IOException`; never reached the "directory omitted" path. This story's working-directory hypothesis for it is refuted |
| `BrowserSessionCache_…_When_WrittenConcurrently` | **genuine product defect, Windows-only, fixed** | see below — not the shared catalog |
| `ApplicationGetInfo_…_After_SchemaSync` | **environmental, not this branch** | `sync-schemas` returned `success:false` with `"columns were saved, but publishing the configuration failed: Error generating content for schema EntitySchemaManager.UsrCodex69153d48"` — a stand-side configuration build failure. Independently, none of `create-app`, `sync-schemas` or `get-app-info` is in `McpWorkerCohort.StageSixNames`, so no worker-routed code path is involved |
| `ApplicationTool_Should_Stream_Progress_For_LongRunning_Call` | separate defect — the heartbeat allowlist gap | tracked under AC-01/AC-02/AC-04 above; unaffected by the settings cascade |
| the other 41 | consequences | bucketed in Fact 3; their results are void as evidence either way |

### AC-06 — the two new gate tests

**`AppSettings_Should_YieldAWholeCatalog…`** — the gate was never the problem; the test was pointed at
the wrong file. Fixed by `clio.mcp.e2e/Support/Configuration/IsolatedClioHome.cs`, which sets
`CLIO_HOME` (the decisive one) alongside `HOME`/`LOCALAPPDATA`/`USERPROFILE`. The test now also asks
clio itself where it will write and refuses to run unless that path is inside its own home — verified
non-vacuous by removing the `CLIO_HOME` redirect and watching it go red naming the shared home.
Applied to `ClioPagesConcurrencyE2ETests`, `SettingsHealthToolE2ETests`,
`SkillManagementToolE2ETests`, `ReadResponseDeadlineToolE2ETests` and `CreateWorkspaceToolE2ETests` —
the five fixtures that redirected `HOME` without `CLIO_HOME`.

**`BrowserSessionCache_…_When_WrittenConcurrently`** — this one is hypothesis 1, and it is a real
product defect rather than a test artifact. It failed with six `UnauthorizedAccessException: Access to
the path is denied` from its writers, against its **own** temporary directory; the shared catalog is
not involved. Cause: `FileSystem.WriteOwnerOnlyTextToFileAtomic` publishes via
`File.Move(overwrite: true)`, which is `rename(2)` on Unix — indifferent to open readers — and
`MoveFileEx(MOVEFILE_REPLACE_EXISTING)` on Windows, which needs DELETE access on the destination and is
refused while any other handle is open on it. `File.ReadAllText` opens with `FileShare.Read`, and that
share mode denies the rename. **The real consumer is Playwright loading a cached `storageState` while
clio refreshes it**, so this is an operator-visible failure, not a test-only one — and it was
structurally invisible to a green macOS suite.

Fixed by retrying the publish over a bounded ~1.1 s window for contention shapes only; a genuine ACL
or read-only-file error still surfaces unchanged, and a non-contention failure
(`FileNotFoundException`) fails on the first attempt. Three unit tests in
`clio.tests/Common/FileSystem.Tests.cs` substitute the move so the contended path runs on every
platform; all three were confirmed red against the pre-fix code (two against the missing retry, one
against a broadened failure classifier).

### GAP CLOSED 2026-08-18: the retry measured under real Windows contention

The paragraph that stood here said the retry's behaviour was argued from `MoveFileEx` semantics and
pinned only by substituted-move unit tests, never observed on Windows. It has now been observed, on a
Windows 11 host (.NET 8), with a probe reproducing TC-E-902's exact shape — six concurrent writers
against one `File.ReadAllText` reader loop, three payload sizes, 180 publishes per arm:

| Arm | Publish failures / 180 | Notes |
|---|---|---|
| bare `File.Move(overwrite: true)` — pre-fix | **108, 113, 113, 103** across four runs | every one `UnauthorizedAccessException: Access to the path is denied.` |
| deadline-bounded retry — post-fix | **0, 0, 0, 0** | worst case 16 attempts; the deadline was never reached |
| bare move on macOS — control | **0** | `rename(2)` ignores open readers, exactly as specified |

The macOS control is the part worth keeping: it reproduces the blindness rather than merely failing to
reproduce the bug. A green macOS run of this probe is consistent with a completely broken publish.

**The measurement also corrected the fix.** The first version bounded the retry by an ATTEMPT COUNT of
12. Under this load the observed worst cases were 13, 15 and 16 attempts — so a count that looked
generous was already failing, and no substituted-move test could have revealed it because the substitute
decides how many attempts occur. The bound is now a **deadline** (2.5 s, capped linear backoff), which
states the guarantee directly and does not need re-tuning when the backoff curve changes. Four runs
never came close to exhausting it.

Note what this does and does not cover: it measures the platform semantic and the retry policy on real
Windows. That clio's `WriteOwnerOnlyTextToFileAtomic` actually routes through that policy is a separate
claim, and it is the one the substituted-move unit tests pin. Together they close the gap; neither does
alone.
