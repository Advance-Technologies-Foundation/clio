# Detached operations: explicit ownership across a runtime update

Discussion #1643; baseline `7225ebcaa`. This isolated probe changes no product/Core contracts. It does
not implement durable replay, a lease manager, or retirement policy.

## Why

Measured on clio 8, in one session with no reconnect: `create-app-section` returned
`section-created: in-progress` at its response deadline, the backend process was replaced 1029 ms later,
and 4.5 minutes on neither the section nor its entity schema existed. A parallel measurement had
`compile-status` answer `success: true, status: not-found` — *"No compile-creatio operation has been
recorded for this environment in the current MCP server session"* — while the compile was still running
server-side. Both are the same defect: work that outlives its response has no owner, and the host cannot
tell "nothing started" from "I no longer know".

## Hypothesis

An operation that outlives its initial response can be owned explicitly, so that activating a new runtime
mid-flight changes nothing about its completion, its external effect, or the truthfulness of its status —
and so that losing the process reports uncertainty rather than absence.

## Shape

Four tiny projects, no added packages. `Contract` is the only assembly crossing the load boundary and
carries data only — `OperationRecord` has no delegates and no runtime objects, so it can be written to
evidence and survive its runtime. `RuntimeV1`/`RuntimeV2` are one workflow source compiled as two
releases into `artifacts/detached/<version>/`. `Host` holds the generic ledger and the measurement runner.

The ledger is deliberately generic: it stores an opaque `Code` supplied by the runtime and never reads it.
Interpreting what an operation *means* stays with the runtime that owns it, so Core does not become a
catalog of business operations.

`OperationRecord` carries a `Target` (environment/tenant key) so quiescence can be asked per target rather
than per process — raised by @vladimir-nikonov, on the grounds that a session driving several environments
would almost never be globally idle and a global-only predicate would be correct but unusable.

## Cases

| id | case |
|---|---|
| A1a | the original operation still answers after a newer runtime is activated |
| A1b | quiescence is scoped per target while the operation runs |
| A2a | the terminal state is truthful and the external effect happened exactly once |
| A2b | the target is quiescent once the operation terminates |
| A3a | a failing operation reports `Failed`, not silence |
| A3b | a cancelled operation reports `Cancelled`, not silence |
| A3c | neither the failure nor the cancellation added an external effect |
| A4a | an operation lost with its process reports `Unknown`, with its target preserved |
| A4b | an identifier that was never issued is still reported as `NotFound` |
| A4c | the recovered host surfaces the lost operation rather than reporting a clean slate |
| A5a | **control** — reading quiescence does not hold it: work starts in the check-then-act gap |
| A5b | a swap window is refused while the target has work in flight |
| A5c | holding a swap window closes the gap for its scope and leaves other targets alone |
| A5d | the scope accepts real work again once the window is released |
| A5e | a target window is refused while a global window is held |
| A5f | a global window is refused while a target window is held |
| A5g | the terminal record is already on disk when a window is granted |
| A5h | under contention, no operation is admitted for a scope whose window is held |
| A5i | **mutation control** — the same regression detects a deliberately split admission |
| N1 | **counterexample** — a pre-existing artefact makes presence a false positive |
| N2 | **counterexample** — a partially completed operation makes presence a false positive |
| N3 | **counterexample** — an intervening operation makes a last-result read answer about the wrong one |
| P1 | a failed evidence write does not rewrite the execution outcome |
| P2 | the scope is visibly degraded and automatic retirement is refused |
| P3 | **correction** — retiring the runtime does not destroy degraded evidence; only replacing its owner would |
| P4 | a failed admission write starts no work and leaves no unfinishable owner |
| P5 | retirement is refused while owned cleanup is held, and allowed once ownership is released |
| L1 | a concurrent disposal cannot release ownership before the outcome is recorded |
| L2 | a rejected completion does not burn the report; the operation can still finish |
| M1 | **mutation control for L1** — the check detects a lease that reports its flag before the outcome |
| M2 | a torn terminal write keeps the outcome in memory and is never recovered as terminal |
| M3 | degradation clears only on an explicit action, and normal operation resumes after it |
| M4 | an update that cannot take its window defers, leaving the work untouched |
| D1 | **disproof of my own mechanism** — polling for an idle moment starves under continuous load |
| D2 | reserving admission first makes the drain finite under that same load |
| O1 | **counterexample to I3** — a runtime-defined result held by the caller keeps the release alive |
| R1 | the V1 release becomes collectible once no lease retains it |
| C1 | **control** — a host with no evidence answers `NotFound` for the very same lost operation |
| C2 | **control** — the release is retained while its operation runs, and only then collectible |

### Barrier repair, after @kirillkrylov's cross-review

The first version of the barrier had four defects, all found by review of the published source and all
now repaired with regression cases:

1. **Split admission.** `Begin` checked the held scopes, released the lock, then registered the record.
   A window could be granted in that gap and work accepted underneath it. Admission and registration are
   now one critical section (A5h).
2. **One-directional exclusion.** A target window was granted while a global window was held. Exclusion
   is now checked both ways (A5e, A5f).
3. **Completion boundary.** `Complete` published the terminal state before writing its evidence, so a
   window could be granted while the end record was still unwritten — and a replacement acting on that
   window would lose it. The write now happens before the state is published and inside the same lock
   (A5g). The cost is that a completion serialises against window acquisition, including its fsync; a
   production ledger would likely want a two-phase commit instead.
4. **A5d tested the wrong thing.** It re-acquired a window instead of starting work after release, and
   leaked the handle. It now starts real work and waits for it to succeed.

### Proving A5h can fail, after @kirillkrylov's second qualification

He pointed out that A5h sampled the invariant once, immediately on acquiring the window, and so would
miss an admission that slipped in later during the hold — and that closure rested on source inspection
rather than on the test being known to detect the defect. Both are now addressed:

- the invariant is sampled **throughout** the hold, not once on acquisition;
- `OperationLedger` has a test-only constructor flag that restores the original split admission, and
  **A5i runs the identical harness against it**. A concurrency test that has never been seen to fail
  proves nothing about the code it guards.

Measured on macOS, three consecutive runs, near-identical traffic in both arms:

| arm | violations | admitted | refused | windows |
|---|---|---|---|---|
| A5h repaired | **0, 0, 0** | 47-50 | 94-100 | 94-100 |
| A5i split admission | **40, 15, 24** | 47-52 | 90-92 | 95-98 |

A5h's `refused` count is reported but deliberately not asserted: whether a start lands inside a held
window is timing-dependent, and asserting it would turn an invariant test into a flaky one. The pass
condition is zero violations plus evidence that both sides ran. Observed on macOS across four runs:
~155 windows, ~145 refusals, 6-15 admissions, 0 violations.

A5 was originally added after @vladimir-nikonov pointed out, while scoping the composition probe, that
`IsQuiescent(target)` is only an observation: reading it and then swapping is check-then-act, and an
operation can begin in the gap. A5a measures that the gap is real; `TryEnterSwapWindow(target)` takes
quiescence and holds it in one step, and A5c measures that it closes the gap for its scope without
freezing unrelated targets. While a window is held, `Begin` for that scope is **refused rather than
queued** — a refusal is observable, a stall is not.

C1, C2 and A5a exist because a suite that passes on its first run is worth nothing until it has been shown to
fail when the property is absent. C1 reproduces today's wrong answer inside the same harness: the same
identifier, queried by a ledger with no evidence, comes back `NotFound`. C2 shows retention is real rather
than incidental — the release is *not* collectible while its work is in flight.

A4 kills a real child process with `Process.Kill(entireProcessTree: true)` while work is in flight, so
nothing gets a chance to write a terminal state. Evidence is flushed with `FileStream.Flush(true)` for
that reason.

## Reproduce

From the repository root:

```bash
dotnet build experiments/DetachedOperations/RuntimeV1/DetachedRuntime.V1.csproj -c Release
dotnet build experiments/DetachedOperations/RuntimeV2/DetachedRuntime.V2.csproj -c Release
dotnet build experiments/DetachedOperations/Host/Host.csproj -c Release
dotnet run --project experiments/DetachedOperations/Host/Host.csproj -c Release --no-build -- \
  run artifacts/detached/10.0.0.0 artifacts/detached/10.1.0.0
```

Exit code 1 means a case failed; JSON on stdout carries the raw observations. The runner uses a unique
temporary directory and writes nothing outside it.

## macOS observations, 2026-09-21

macOS 27.0.0 (arm64), .NET 10.0.12. **39/39 passed, exit 0, three consecutive runs.** The contract derived from these cases is in
[`execution-lifetime-contract.md`](execution-lifetime-contract.md).

- The operation started on `10.0.0.0` kept answering `Running` and stayed owned by `10.0.0.0` after
  `10.1.0.0` was activated mid-flight.
- One external effect line, written by V1, after the update: `v1-executed-by-10.0.0.0`.
- Failure and cancellation both produced terminal states and no effect line.
- The killed child's operation came back `Unknown` / `history-unavailable` with `Target` intact; a
  never-issued identifier still came back `NotFound`; the same identifier without evidence came back
  `NotFound`.

## Interpretation and limits

Evidence is not replay. Two lines are written per operation and nothing about the work itself, so recovery
can say an outcome is unavailable but can never resume or retry. Persisting a record does not make a retry
safe and this probe does not claim it does.

Retention in C2 comes from both the ledger's owner reference and the detached work's own closure; the
probe shows the release outlives the update, not which of the two references achieves it.

Not covered: contention beyond two threads (A5h/A5i run one starter against one swapper, not a storm),
disk-write failure handling, post-`Complete` task cleanup, (M1 closed the missing mutation arm for L1),
fairness or starvation under sustained pressure — A5h shows admissions can be starved when a swapper
polls aggressively, and there is no wait-budget policy — native libraries or resources, real
Creatio/DI dependencies, GC timing guarantees, multi-process coordination, server-side reconciliation
(asking Creatio what is actually running), and any integration with `UpdatingComposition` — this probe
loads releases through its own collectible context rather than through Core, deliberately, so it cannot
conflict with the retirement branch.

## Windows observations, 2026-09-21

Windows 10.0.26200, .NET 10.0.11 (a different patch from the macOS run, which used 10.0.12).
**39/39 passed, exit 0**, from a clean clone of this branch at `75295deb8e0a` — nothing modified.
The mutation control discriminates here too (repaired 0 violations, split admission 22), but on fewer
samples: Windows produced 11-14 windows against macOS's ~95, because of coarser timer granularity. The
Windows arm is therefore weaker evidence for A5h/A5i than the macOS arm, not equal evidence.
A5h's mix differs from macOS (53 admitted / 67 refused / 35 windows versus roughly 10 / 145 / 155),
which is timer granularity rather than behaviour; the invariant — zero violations — holds on both.
Raw output in `windows-results.json`.

Every case behaves identically to macOS, including the two controls. This matters most for A4 and R1,
which are the ones with platform-sensitive mechanics: the external kill still leaves `Unknown` rather
than `NotFound`, and the release still becomes collectible only after its operation ends.

An independent reproduction by @kirillkrylov is still welcome — this one is mine, on my own probe.

## Reconcilable versus uncertain-only

Answered in [`reconcilability.md`](reconcilability.md), measured against a live stand: the split is three
tiers, not two. Artefact-named operations (create-app-section, package installs) are attributable and do
not need a swap to wait; `compile-creatio` is state-reconcilable but not attributable, and converts to
recoverable purely by having a durable terminal marker; `restart` has no external answer at all, so
`Unknown` is permanent rather than temporary.

## Original smallest next question

`IsQuiescent(target)` is answered from this process's own ledger. After a process loss it reports
`Unknown` operations but cannot say whether their work is still running somewhere — for Creatio-side work
the authoritative answer lives on the server. So: which operation classes can be reconciled against
Creatio, and which can only ever be reported as uncertain? That boundary decides whether a staged host
update can ever be triggered automatically after a crash, or only after a clean shutdown.
