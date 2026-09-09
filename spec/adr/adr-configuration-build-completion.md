# ADR: configuration-build completion is observed, not answered

- Status: accepted
- Date: 2026-09-09
- Driver: [issue #1422](https://github.com/Advance-Technologies-Foundation/clio/issues/1422)

## Context

`compile-configuration` decided that a build had finished from one thing: the HTTP response to the
`WorkspaceExplorerService.svc/Build` / `/Rebuild` request that started it. It waited for that
response with `Timeout.Infinite`, which also silently discarded any `--timeout` the caller passed.

That response does not arrive. A configuration build's last step is reloading the application, which
tears down the connection the response would travel on. Measured on a live stand (core 10.0.0.934,
.NET Framework, on-prem), six of six builds ended with zero response bytes and a connection reset.

Two failures follow from it, both observed:

- **Reset path.** `RemoteCommandOptions.MaxAttempts` defaults to 3, so the request was re-sent. One
  `clio cc --all` ran the full rebuild three times, forced three runtime reloads, took 8m42s, and
  ended by tripping IIS rapid-fail protection, which stopped the application pool. Where the retry
  happened to land after the build and get an answer, the success clio reported came from the retry
  rather than from the build it started.
- **Held-socket path.** An intermediary that keeps an idle socket open rather than resetting it —
  a load balancer in front of a cloud instance, which is the reporter's environment — means the
  request never fails, so no retry fires and nothing terminates. `compile-status` reported `running`
  indefinitely, and the parent-held configuration-build reservation was not released until its
  65-minute reclaim ceiling, refusing every later compile on that environment.

clio already solved the same problem for packages: `PackageBuilder.CompileWithPolling` sends the
request asynchronously and detects completion from `CompilationHistory`, with a comment naming the
platform change ("In Creatio 8.3.3+, RebuildPackage no longer sends back an HTTP response").
`compile-configuration` was never converted.

## Decision

Determine completion by observing the environment. The response, when one arrives, remains the
best evidence — but it stops being the only one.

The request is sent once, asynchronously and cancellably, with `maxAttempts: 1`. A watcher polls
`CompilationHistory` through a failure-tolerant loop for the duration. Completion is decided by an
ordered rule (`ICompilationCompletionDecider`):

1. **The response arrived** → its payload is the verdict. Checked first because it is also the
   compile-*error* path: a build that fails does not reload the runtime, so the server stays up,
   answers, and carries the compiler diagnostics no other source has.
2. **Activity, then a runtime reload** → confirmed. The reload is observed as the environment
   becoming unreachable and then answering again, and the verdict is read from
   `api/ConfigurationStatus/GetLastCompilationResult` after it.
   **Availability is probed on that verdict endpoint, NOT on the compilation-history channel.** A first
   implementation inferred the reload from history-poll failures and never once observed a reload on a
   real build. Measured directly across an application-pool recycle: the verdict endpoint stopped
   answering for 44 seconds while the history poller reported not one failed read. Probing the endpoint
   the verdict is about to be read from also makes "the environment answers again" mean exactly "the
   verdict is readable". The probe carries a short 5-second timeout, because during the outage requests
   hang until their own timeout rather than failing fast.
3. **The request ended, but no reload was seen** → keep waiting. This is the false-success guard: an
   intermediary resetting an idle socket does so in the *middle* of a build, and treating that as
   completion would report the previous build's verdict while this one still runs.
4. **Activity that stopped for a five-minute quiet window, no reload** → inferred, and reported as
   inferred. This is the reporter's case, where the request never ends at all.
   The window has to OUTLAST the reload or it makes rule 2 unreachable. Measured on the stand: the
   reload lands about two minutes after the last compilation-history row, and a first implementation
   that reused `ICompilationSettleTracker`'s 45-second window (calibrated for the gaps between
   projects, a different question) ended every ordinary build on this rule — reading the undated
   verdict about 30 seconds before the application had actually reloaded.
5. **The request failed and nothing was ever built**, past a 90-second startup grace → transport or
   authorization failure, reported rather than waited out.
6. `--timeout` bounds the whole operation and is honoured again.

`CommandSuccess` is set explicitly on every path. It defaults to `true` and used to be cleared only
inside `ProceedResponse`, which a build with no response never reaches — which is why a failed run
printed its error and then "Compilation finished" immediately after it.

## Consequences

- The tracked MCP operation always resolves, so `compile-status` stops reporting `running`
  indefinitely and the configuration-build reservation is released without restarting the server.
  The separate reconciliation in `compile-status`, and a change to the ENG-95262 worker completion
  protocol, are both unnecessary as a result.
- `clio cc` no longer multiplies a heavy operation: one request, one build.
- The verdict for a build with no response is read from an **undated** endpoint. It is trustworthy
  only because it is read after the reload that ended the build; rule 3 exists to keep it that way.
- Completion detection depends on two independent channels: compilation history answers "is it
  building", and the verdict endpoint answers "did it restart". They fail differently on purpose -
  the history channel survives the reload, which is why it cannot be the one that detects it.
- The quiet-window path (rule 4) is circumstantial. It is reported as inferred, and it is reachable
  only when the request never terminates.

## Alternatives rejected

- **Bound the wait and keep the response authoritative.** Shortens the hang from forever to 60
  minutes and changes nothing else: the verdict is still derived from a message the platform does
  not send. It also cannot be combined with `maxAttempts: 1` safely under the old model, because the
  retry was accidentally load-bearing for the only path that worked.
- **Reconcile inside `compile-status`.** Leaves `clio cc` broken, leaves the reservation held, and
  puts environment round-trips into a read-only status poll.
- **Reuse `ICompilationSettleTracker` as the completion rule.** Its quiet window answers "is the gap
  between two projects over", not "has the build ended", and at 45 seconds it fires before the
  reload — see rule 4. The tracker is left to `watch-compilation`, which is what it was calibrated
  for.
- **Gate completion on the `Terrasoft.Configuration.ODataEntities.csproj` marker**, as
  `watch-compilation` does. Measured false: a configuration compile does not write that row (see
  `docs/knowledge/Command/odata-entities-is-not-a-compile-completion-marker.md`), so the rule would
  never fire and every successful compile would wait out its timeout.
- **Ask the platform whether a build is running.** No such endpoint exists on this platform; every
  candidate answers 404, including `IsODataBuildRunning`, which newer platforms do expose. If a
  configuration-build status endpoint appears, it becomes a better rule 2 and this design should
  adopt it.

## Not verified

- Behaviour on .NET Core / Kestrel stands was not measured. Rule 1 covers "the server answers", so
  the design degrades to today's behaviour there rather than breaking.
- Whether a *failed* compile reloads the runtime was not measured directly; rule 1 is ordered first
  on the assumption that it does not, which matches the response arriving on the error path.
