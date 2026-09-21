# Supervisor + quiescence composition: does gating a real process swap on the ledger actually work?

Discussion [#1643](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643); base
`551f25c92538` (`Alexandr-Kravchuk/detached-operation-probe`). This isolated probe changes no
product/Core contracts and adds no new files to that branch — it references `Contract.csproj` and
links `OperationLedger.cs` from it, so the two probes cannot drift apart or collide.

**Repair history, on the record.** The first measurement ran against `e3138962c`.
[kirillkrylov's independent review](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540898)
found three reproduced defects in `TryEnterSwapWindow` at that exact commit — an admission race
(`Begin` released its lock before registering the record), one-directional window exclusion, and a
completion-boundary race — none of which this probe's sequential scenarios happened to trigger. Alexandr
[repaired all three at `551f25c92538`](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541013)
with regression cases (A5e–A5h). S1–S4 were re-run against the repair, and S5 was added to close the gap
this probe's own design first left open: a new operation arriving *during* the handover.

**Second review, second gap.**
[kirillkrylov's cross-review of S1–S4](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541164)
(reproduced independently on Windows) found a real mismatch between claim and code: `RunScenario` kills
the backend and never respawns, despite the README then claiming "Kill + respawn". S1–S4 are honestly
drain-before-termination measurements — they show what survives a kill, not that a replacement comes up
correctly. **S6 is the actual swap proof**, added in response: a real V1→V2 respawn, admission closure
held through termination *and* replacement readiness (not just the kill), and concurrent pressure against
the outgoing backend during the handover rather than a single sampled attempt.

**Third review, an unfalsifiable oracle.**
[kirillkrylov reproduced S6's 6/6](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541476),
then showed it was a false positive: changing only the post-handover operation's outcome to `fail` still
passed 6/6. Two independent bugs made this possible — `PumpBackendOutput` completed every lease as
`Succeeded` regardless of what the backend actually reported, and the effect check matched any line
ending in the expected PID, which the unrelated `__readiness__` probe's own line also satisfied. Both
fixed: the reported outcome is now parsed and completed correctly, effect matching requires the exact
`target:opId:done-by-pid-<pid>` line, and the terminal state is asserted against the ledger before the
effect file is even read. **S7** is the negative control this bug argued for: the identical path with a
failing outcome, asserting `Failed` and a correctly *absent* effect line. Before restoring the fix, the
broken version was re-run against S7 and it failed with an explicit mismatch
(`post-handover operation reached Succeeded, expected Failed`) rather than a silent pass — the fix is
demonstrated to matter, not just applied.

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
| S1 | control — naive swap (drain-before-termination) | none | envA (1200ms) |
| S2 | gated swap, single target (drain-before-termination) | global | envA (1200ms) |
| S3 | gated swap, wrong scope (drain-before-termination) | per-target on envA only | envA (300ms), envB (1200ms) |
| S4 | gated swap, correct scope for a host (drain-before-termination) | global | envA (300ms), envB (1200ms) |
| S5 | work arriving during the handover, single sampled attempt | global | envA (300ms), then a second `Begin` while the window is held |
| S6 | **the swap proof** — real V1→V2 respawn under concurrent handover pressure | global | envA (400ms), a post-handover op on V2 (`succeed`), continuous `Begin` pressure throughout |
| S7 | **negative control for S6's oracle** | global | identical to S6, but the post-handover op is told to `fail` |

S1 is the control: without it, S2 passing would not be evidence the gate does anything — a suite that
cannot fail is worth nothing. S3 is the one that operationalizes the per-target correction: a supervisor
that only checks the busy target it happens to know about still destroys a different target's in-flight
work, because replacing the process affects everything the host owns, not just the scope that was checked.
S5 checks the ledger's own admission guarantee through this probe's composed path with one sampled
attempt. S6 goes further on every axis S5 left open: the window is held through V2 startup and a real
readiness check (not released the instant the kill returns), admission is pressured *continuously* by a
background task rather than sampled once, and the probe verifies a genuinely different process — distinct
PID, confirmed by an exact `target:opId:done-by-pid-<pid>` match — actually serves the post-handover
operation. S7 is what makes S6's checks falsifiable rather than assumed: same path, an operation told to
fail, and an assertion that both the ledger's terminal state and the effect file agree it failed.

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

## Observations, 2026-09-21 — against the repaired barrier (`551f25c92538`), with S6/S7 added

macOS 27.0.0 (arm64) / Unix 26.6.2, .NET 10.0.4 runtime (SDK 10.0.103). **7/7 passed, exit 0, three
consecutive runs, numbers stable within noise:**

| case | effects present | swap latency | wait latency |
|---|---|---|---|
| S1 (naive) | `[]` | ~16-20ms | 0ms |
| S2 (global, one target) | `[envA]` | ~1240-1256ms | ~1217-1226ms |
| S3 (per-target, wrong scope) | `[envA]` only — **envB lost** | ~328-336ms | ~309-312ms |
| S4 (global, two targets) | `[envA, envB]` | ~1216-1237ms | ~1196-1220ms |
| S5 (handover admission, sampled) | refused during window: **true**; admitted after release: **true** | — | — |
| S6 (real swap, continuous pressure, `succeed`) | V1's op **and** V2's post-handover op both present (exact match), distinct PIDs | — | — |
| S7 (negative control, `fail`) | V1's op present; V2's post-handover effect **correctly absent**, ledger reports `Failed` | — | — |

S1's ~19ms swap against S2's ~1220ms wait is the whole claim in two numbers: the naive supervisor acts
before the work is done and loses it; the gated one waits exactly as long as the work takes and does not.
S3 swaps at ~330ms — right after envA's 300ms operation finishes — and takes envB down with it, still
mid-flight on its 1200ms operation. S4, gated globally against the same two operations, waits for the
slower one and loses neither.

**S6/S7, three consecutive runs:**

| run | S6 V1 PID | S6 V2 PID | S6 result | S6 pressure (attempts/refused/admitted) | S7 V2 PID | S7 result |
|---|---|---|---|---|---|---|
| 1 | 33315 | 33316 | both effects present | 13 / 13 / **0** | 33318 | Failed, effect absent |
| 2 | 33346 | 33347 | both effects present | 8 / 8 / **0** | 33349 | Failed, effect absent |
| 3 | 33359 | 33376 | both effects present | 10 / 10 / **0** | 33380 | Failed, effect absent |

V1 and V2 are distinct real processes every run. V1's original operation survived the swap; V2 — the new
process — actually executed and recorded the post-handover operation, verified by an exact
`target:opId:done-by-pid-<pid>` line rather than a PID-suffix match (which is what let the earlier version
false-positive against the readiness probe's own effect line). Every pressure attempt tagged as occurring
while the window was held was refused; zero were silently admitted into the outgoing backend during the
handover. S7 confirms the fix is real, not cosmetic: told to fail, the operation's ledger state is
`Failed` and no effect line exists for it — and re-running the *pre-fix* code against S7 threw
`post-handover operation reached Succeeded, expected Failed` rather than passing, which is the mutation
check for this fix.

All S1–S4 numbers are unchanged within noise from the pre-repair measurement, which is expected: the
three defects kirillkrylov found are contention bugs, and none of S1–S4's sequential single-caller
scenarios contend for the window. S5's single sampled attempt and S6's continuous pressure are the
scenarios that could have shown a difference, and both pass against the repair.

## Interpretation and limits

This demonstrates the composition works for the shape it tests: one backend process (then two, for S6),
operations reported faithfully over stdout, a supervisor that already knows every target in play. It
does **not** demonstrate:

- **Real transport continuity end-to-end.** This probe reuses the *measured* result from the earlier
  supervisor prototype rather than re-proving it; it does not itself keep an external client pipe open —
  adding that is straightforward (the earlier prototype already does it) but wasn't the open question.
- **A supervisor that doesn't already know every live target.** `TryEnterSwapWindow(null)` is correct
  *if* the ledger already has a record for every operation in flight. A target whose backend never
  reported it (a crash before the first `Begin`, or a target the supervisor doesn't yet track) would not
  block the gate and would still be lost — this is a narrower, sharper version of the "which operation
  classes are reconcilable" question, now answered by
  [E3's reconcilability tiers](https://github.com/Advance-Technologies-Foundation/clio/blob/Alexandr-Kravchuk/detached-operation-probe/experiments/DetachedOperations/reconcilability.md)
  for *which classes* this even matters for.
- **A busy-wait cost bound.** `TryEnterSwapWindow` is polled every 25ms (S1-S5) or admission is attempted
  every 5ms (S6's pressure loop); a production design would want an event/callback instead of polling,
  and a policy for what happens if the wait exceeds a budget — that's activation policy, still open.
- **A mutation control for the *effect/outcome oracle*** — now present, as S7, after kirillkrylov found
  the original oracle unfalsifiable (a fixed `Succeeded` and a PID-suffix match both hid a `fail`
  outcome). What S7 does **not** cover: a mutation control for the *rest* of the composed path — killing
  V1, starting V2, routing pressure. E3's A5i proves its admission barrier discriminates; this probe has
  that guarantee only for the outcome-reporting half, not for whether the kill/respawn/pressure sequencing
  itself would be caught if broken.
- **Concurrency beyond one swapper and one pressure loop**, native resources, real Creatio/DI dependencies,
  or any integration with `UpdatingComposition` — same boundary E3 draws, for the same reason: this probe
  loads nothing through Core and cannot conflict with either other branch.

## Relationship to the other two streams

Consumes E3's contract and ledger without modifying them (`Contract.csproj` referenced, `OperationLedger.cs`
linked). Does not touch `krylov/runtime-retirement-probe`. Feeds directly into
[`docs/host-update-continuity.md`](https://github.com/Advance-Technologies-Foundation/clio/blob/nikonov/host-update-policy/docs/host-update-continuity.md)'s
open item on composing transport continuity with a durable ledger.
