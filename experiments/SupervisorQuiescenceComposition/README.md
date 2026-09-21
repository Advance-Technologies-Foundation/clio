# Supervisor + quiescence composition: does gating a real process swap on the ledger actually work?

Discussion [#1643](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643); base
`551f25c92538` (`Alexandr-Kravchuk/detached-operation-probe`). This isolated probe changes no
product/Core contracts and adds no new files to that branch — it references `Contract.csproj` and
links `OperationLedger.cs` from it, so the two probes cannot drift apart or collide.

**Repair history, on the record.** The first measurement (below the fold, superseded) ran against
`e3138962c`. [kirillkrylov's independent review](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540898)
found three reproduced defects in `TryEnterSwapWindow` at that exact commit — an admission race
(`Begin` released its lock before registering the record), one-directional window exclusion, and a
completion-boundary race — none of which this probe's sequential scenarios happened to trigger. Alexandr
[repaired all three at `551f25c92538`](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541013)
with regression cases (A5e–A5h). S1–S4 below were re-run against the repair rather than left standing on
the broken commit, and S5 was added specifically to close the gap this probe's own design left open: a
new operation arriving *during* the handover, which is the concrete "work arriving during the handover"
case kirillkrylov asked for.

## Why

Two mechanisms each have measured evidence on their own, but nobody had run them together:

- **Transport continuity** — [a thin supervisor keeping the client pipe open across a full backend
  replacement](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341):
  8.1.0.129 → 8.1.0.131, 1.0s macOS / 1.3s Windows, zero client reconnects.
- **Execution quiescence** — [E3's `OperationLedger`](../DetachedOperations/README.md), `IsQuiescent(target)`
  and `TryEnterSwapWindow(target)`, exercised entirely in-process against an `AssemblyLoadContext` swap.

The supervisor prototype that proves transport continuity swaps **immediately**, with no ledger
consultation at all — it is exactly the naive mechanism that lost the `create-app-section` operation
in the original measurement. E3 proves the ledger's predicate is correct, but only against an in-process
runtime swap, never against a real process being replaced. This probe runs the actual combination: a
supervisor that gates a **real** `Process.Kill` + respawn on the ledger.

## Hypothesis

Gating a real backend-process replacement on `TryEnterSwapWindow` prevents the loss that an ungated
swap causes, for the exact detached-work shape that broke `create-app-section` — and a per-target-only
gate does **not** extend that protection to a different target sharing the same host, which operationalizes
the correction already accepted in the discussion: quiescence must be global for a host-level swap unless
per-target isolation is separately demonstrated (nothing here demonstrates that).

## Shape

Two tiny projects, no added packages beyond the referenced `Contract`.

- **`Backend`** — a real, separate OS process driven over stdin/stdout. `start <target> <opId> <workMs>
  <outcome>` replies `accepted <opId>` immediately (mirrors a response-deadline reply), then keeps running
  in the background and appends one line to an effect file when the work finishes — the only durable proof
  of work done, matching the shape of a real side effect rather than the operation record itself.
- **`Supervisor`** — starts a `Backend` process, opens an `OperationLedger` (the same class E3 publishes,
  linked not copied), calls `ledger.Begin`/`lease.Complete` as the backend reports progress over its
  stdout, then swaps under one of three gating modes before killing the backend and measuring which
  targets' effects survived.

## Cases

| id | scenario | gate | targets (work) |
|---|---|---|---|
| S1 | control — naive swap | none | envA (1200ms) |
| S2 | gated swap, single target | global | envA (1200ms) |
| S3 | gated swap, wrong scope | per-target on envA only | envA (300ms), envB (1200ms) |
| S4 | gated swap, correct scope for a host | global | envA (300ms), envB (1200ms) |
| S5 | work arriving during the handover | global | envA (300ms), then a second `Begin` while the window is held |

S1 is the control: without it, S2 passing would not be evidence the gate does anything — a suite that
cannot fail is worth nothing. S3 is the one that operationalizes the per-target correction: a supervisor
that only checks the busy target it happens to know about still destroys a different target's in-flight
work, because replacing the process affects everything the host owns, not just the scope that was checked.
S5 checks the ledger's own admission guarantee through this probe's composed path rather than only
directly against the ledger the way E3's A5b/A5h already do: while a window is held, a new request for
that scope must be refused (`SwapWindowHeldException`), and the scope must accept work again once the
window is released.

## Reproduce

From the repository root:

```bash
dotnet build experiments/SupervisorQuiescenceComposition/Backend/Backend.csproj -c Release
dotnet build experiments/SupervisorQuiescenceComposition/Supervisor/Supervisor.csproj -c Release
dotnet run --project experiments/SupervisorQuiescenceComposition/Supervisor/Supervisor.csproj -c Release --no-build -- \
  experiments/SupervisorQuiescenceComposition/Backend/bin/Release/net10.0/Clio10.SupervisorComposition.Backend.dll
```

Exit code 1 means a case failed; JSON on stdout carries the raw observations, including swap and wait
latency per scenario. The probe uses a unique temporary directory per scenario and writes nothing outside it.

## Observations, 2026-09-21 — against the repaired barrier (`551f25c92538`)

macOS 27.0.0 (arm64) / Unix 26.6.2, .NET 10.0.4 runtime (SDK 10.0.103). **5/5 passed, exit 0, three
consecutive runs, numbers stable within noise:**

| case | effects present | swap latency | wait latency |
|---|---|---|---|
| S1 (naive) | `[]` | ~19ms | 0ms |
| S2 (global, one target) | `[envA]` | ~1248ms | ~1220ms |
| S3 (per-target, wrong scope) | `[envA]` only — **envB lost** | ~336ms | ~311ms |
| S4 (global, two targets) | `[envA, envB]` | ~1237ms | ~1218ms |
| S5 (handover admission) | refused during window: **true**; admitted after release: **true** | — | — |

S1's ~19ms swap against S2's ~1220ms wait is the whole claim in two numbers: the naive supervisor acts
before the work is done and loses it; the gated one waits exactly as long as the work takes and does not.
S3 swaps at ~336ms — right after envA's 300ms operation finishes — and takes envB down with it, still
mid-flight on its 1200ms operation. S4, gated globally against the same two operations, waits for the
slower one (~1218ms, matching envB's 1200ms) and loses neither. S5 confirms the repaired admission
barrier holds through this probe's own composed usage: a second `Begin` for the gated target during the
window throws `SwapWindowHeldException` every run, and the target accepts work again the instant the
window is disposed.

All four numbers (S1–S4) are unchanged within noise from the pre-repair measurement, which is expected:
the three defects kirillkrylov found are contention bugs, and none of S1–S4's sequential single-caller
scenarios contend for the window. S5 is the scenario that would have been able to show the difference,
and it now passes against the repair.

## Interpretation and limits

This demonstrates the composition works for the shape it tests: one backend process, operations reported
faithfully over stdout, a supervisor that already knows every target in play. It does **not** demonstrate:

- **Real transport continuity end-to-end.** This probe reuses the *measured* result from the earlier
  supervisor prototype rather than re-proving it; it does not itself keep an external client pipe open —
  adding that is straightforward (the earlier prototype already does it) but wasn't the open question.
- **A supervisor that doesn't already know every live target.** `TryEnterSwapWindow(null)` is correct
  *if* the ledger already has a record for every operation in flight. A target whose backend never
  reported it (a crash before the first `Begin`, or a target the supervisor doesn't yet track) would not
  block the gate and would still be lost — this is a narrower, sharper version of the "which operation
  classes are reconcilable" question E3's README already leaves open, not a new one.
- **A busy-wait cost bound.** S4's 1215ms wait is `TryEnterSwapWindow` polled every 25ms; a production
  design would want an event/callback instead of polling, and a policy for what happens if the wait
  exceeds a budget (retry later? force after a timeout, converting into the S1 case deliberately?) —
  that policy question is explicitly out of scope here, per the discussion's activation-policy row.
- **Concurrency beyond two sequential targets**, native resources, real Creatio/DI dependencies, or any
  integration with `UpdatingComposition` — same boundary E3 draws, for the same reason: this probe loads
  nothing through Core and cannot conflict with either other branch.

## Relationship to the other two streams

Consumes E3's contract and ledger without modifying them (`Contract.csproj` referenced, `OperationLedger.cs`
linked). Does not touch `krylov/runtime-retirement-probe`. Feeds directly into
[`docs/host-update-continuity.md`](https://github.com/Advance-Technologies-Foundation/clio/blob/nikonov/host-update-policy/docs/host-update-continuity.md)'s
open item on composing transport continuity with a durable ledger.
