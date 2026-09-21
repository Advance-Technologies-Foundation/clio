# Supervisor + quiescence composition: does gating a real process swap on the ledger actually work?

Discussion [#1643](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643); base
`7f49306e5` (`Alexandr-Kravchuk/detached-operation-probe`, the shared contract). This isolated probe
changes no product/Core contracts and adds no new files to that branch — it references
`Contract.csproj` and links `OperationLedger.cs` from it, so the two probes cannot drift apart or
collide.

**Fifth round: migrated off a primitive proven to starve.**
[Alexandr tested his own "defer, never kill" disproof criterion and it held](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542770):
`TryEnterSwapWindow`, polled under continuous load, never granted a window for a full 2-second
observation on either platform. That is the exact primitive every wait loop in S2/S4/S6–S9 used. All
four migrated to `TryReserveAdmission` (reserve-then-drain: close the scope to new admissions
immediately, then wait only for already-admitted work to finish, bounded by a caller-supplied
`drainBudget` — never a hardcoded constant, since a real `compile-creatio` runs minutes, not this
probe's milliseconds). **S10** is this stream's own disproof test, mirroring Alexandr's D1/D2 but
applied to the host-swap case rather than his in-process one: under continuous admission pressure, the
old poll never grants a window; the new reserve-then-drain always does.

**Sixth round: a real MCP client, not a synthetic pipe.**
[Named explicitly, twice](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542626):
"do not equate an open pipe with surviving execution." Two new projects — `McpHost` (a real
`ModelContextProtocol` stdio server, same pattern as `Clio10.Mcp.McpAdapter`, wrapping the ledger +
backend-swap logic behind three tools: `start-operation`, `query-operation`, `trigger-swap`) and
`McpClientProbe` (a real `McpClient` / `StreamClientTransport` connection, opened once and never
reconnected, per [Alexandr's five requirements for a shared client harness](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18543803)).
Genuine correlation measured: start an operation over MCP, query it mid-flight (`Running`), trigger a
real V1→V2 swap over MCP, query the **same opaque id on the same connection** afterward (`Succeeded`),
and confirm a real PID change. **4/4 on first run, 3/3 consecutive runs.**

**What this does not yet demonstrate, found while building it rather than assumed away:** the
three-outcome requirement's third state (`Unknown`, not just `Running`/`Succeeded`) needs the MCP
*pipe-owning* process itself to be lost while the client stays connected — which this design cannot
produce, because `McpHost` owns both the pipe and the ledger in the same process. Demonstrating
`Unknown` honestly needs the genuine three-tier split this whole discussion's Flow B describes (client
→ thin pipe-owning supervisor → separately replaceable backend/ledger owner), which is a materially
different architecture from `McpHost`, not a missing test case on top of it. Recorded as an
architectural finding, not a gap to paper over with a contrived crash.

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

**Fourth round: the missing proof kirillkrylov's next assignment asked for.**
[The checkpoint](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542626)
named four gaps explicitly: replacement readiness that avoids customer-side effects, failed V2 startup,
bounded fallback attempts, and a definite unavailable outcome if fallback fails. All four:

- **Readiness is now side-effect-free.** `Backend` gained `ping`/`pong` — no ledger interaction, no
  effect-file write — replacing the earlier `__readiness__ start` command, which exercised the same code
  path as real customer work and was flagged as a design constraint violation even though it was harmless
  in this sandbox.
- **S8/S9** exercise ordering C from the discussion's three-way comparison end to end: V2 fails to start
  (bounded attempts, each a real process that exits nonzero immediately), then a bounded fallback to
  restarting V1 from its retained binary — either recovering (S8) or, when the fallback also fails for
  the same reason V2 did (S9), reporting a definite `Unavailable` terminal rather than hanging.
- **Rebasing onto the ledger's P5 change surfaced a real bug in this probe, not just in the rebase.**
  Quiescence now follows lease *ownership* (`Dispose`), not the published *outcome* (`Complete`) — a
  runtime may legitimately hold a lease for owned cleanup after publishing its result. This probe's
  leases were never disposed, so after the rebase every scenario hung waiting for a window that could
  never open. Fixed by disposing immediately after completing, which is the correct behavior for this
  probe specifically: it has no owned-cleanup phase, so completion and ownership release happen together.

**What this round does not attempt:** an actual MCP client connection checking a post-handover request,
which the same checkpoint explicitly asked for ("do not equate an open pipe with surviving execution").
This probe's transport is a synthetic line protocol over stdio, not the MCP wire protocol — building a
real MCP SDK client against a real MCP-speaking supervisor is a materially larger task than extending
the existing harness, and is the next concrete step rather than something folded into this round.

## Why

Two mechanisms each have measured evidence on their own, but nobody had run them together:

- **Transport continuity** — [a thin supervisor keeping the client pipe open across a full backend
  replacement](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341):
  8.1.0.129 → 8.1.0.131, 1.0s macOS / 1.3s Windows, zero client reconnects.
- **Execution quiescence** — [E3's `OperationLedger`](https://github.com/Advance-Technologies-Foundation/clio/blob/Alexandr-Kravchuk/detached-operation-probe/experiments/DetachedOperations/README.md), `IsQuiescent(target)`
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
| S8 | **failed-startup fallback, recovery** | global | V1 killed; V2 fails twice (`--fail-startup`); fallback restarts V1, which succeeds and serves new work |
| S9 | **failed-startup fallback, exhausted** | global | V1 killed; V2 fails twice; fallback to V1 *also* fails twice; must report `Unavailable`, not hang |
| S10 | **starvation disproof for the migration itself** | n/a (drives the ledger directly, no backend process) | 8 concurrent rolling-window admissions on `envA`; asserts the old poll never grants a window and the new reserve-then-drain always does, under identical continuous load |

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
fail, and an assertion that both the ledger's terminal state and the effect file agree it failed. S8/S9
are ordering C end to end: S8 shows the fallback path actually recovers and serves work, not just that it
was designed to; S9 shows exhausting every attempt produces a truthful terminal outcome instead of an
infinite wait, which was previously only a design description, not a measured behavior.

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

## Observations, 2026-09-21 — with S10 added, migrated to TryReserveAdmission

macOS 27.0.0 (arm64) / Unix 26.6.2, .NET 10.0.4 runtime (SDK 10.0.103). **10/10 passed, exit 0, five
consecutive runs, numbers stable within noise:**

| case | effects present | swap latency | wait latency |
|---|---|---|---|
| S1 (naive) | `[]` | ~16-22ms | 0ms |
| S2 (global, one target) | `[envA]` | ~1239-1274ms | ~1218-1256ms |
| S3 (per-target, wrong scope) | `[envA]` only — **envB lost** | ~328-349ms | ~309-330ms |
| S4 (global, two targets) | `[envA, envB]` | ~1216-1252ms | ~1196-1228ms |
| S5 (handover admission, sampled) | refused during window: **true**; admitted after release: **true** | — | — |
| S6 (real swap, continuous pressure, `succeed`) | V1's op **and** V2's post-handover op both present (exact match), distinct PIDs | — | — |
| S7 (negative control, `fail`) | V1's op present; V2's post-handover effect **correctly absent**, ledger reports `Failed` | — | — |
| S8 (V2 fails, fallback recovers) | `outcome=Recovered`, 2 V2 attempts, 1 fallback attempt, recovered backend served new work | — | — |
| S9 (V2 and fallback both fail) | `outcome=Unavailable`, 2 V2 attempts, 2 fallback attempts, no hang | — | — |

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

**S8/S9, three consecutive runs:**

| run | S8 outcome | S8 attempts (V2/fallback) | S8 recovered PID | S9 outcome | S9 attempts (V2/fallback) |
|---|---|---|---|---|---|
| 1 | Recovered | 2 / 1 | 69877 | Unavailable | 2 / 2 |
| 2 | Recovered | 2 / 1 | 70369 | Unavailable | 2 / 2 |
| 3 | Recovered | 2 / 1 | 70694 | Unavailable | 2 / 2 |

S8's fallback always succeeds on its first attempt in these runs (the retained V1 binary has no reason
to fail once V2's simulated failure is over), so the second fallback attempt is exercised only by S9,
where both legs are configured to fail throughout. Both outcomes are read from the ledger and the
process's own PID, not inferred from control flow reaching a particular line.

One real bug found and fixed while building this round, on the record: the first version of S8/S9 called
`TryHandshake` on the fallback candidate before starting a reader on its stdout, so "pong" had nowhere to
land and every attempt looked like a failure regardless of whether the process actually started — S8
reported `Unavailable` even when `fallbackSucceeds: true`. Fixed by starting `PumpBackendOutput` before
each handshake attempt, not after.

**S10, five consecutive runs:**

| run | pollGranted | reserveGranted | reserveMs |
|---|---|---|---|
| 1 | false | true | 395 |
| 2 | false | true | 510 |
| 3 | false | true | 2809 |
| 4 | false | true | 590 |
| 5 | false | true | 429 |

`pollGranted=false` on all five runs is the same result Alexandr measured against his in-process case,
now confirmed for the primitive this probe actually depends on: the old poll-for-idle pattern never
grants a window under continuous admission, full stop. `reserveGranted=true` on all five confirms the
migration fixes it. The `reserveMs` spread (395–2809ms) is wider than Alexandr's ~130ms and is a property
of *this test's own load generation*, not of `TryReserveAdmission`: eight CPU-bound tight loops contend
for one lock and saturate the thread pool, which adds scheduling latency to how quickly the drain-wait's
own `Task.Delay(25)` gets to re-check — noted rather than smoothed over, since the honest number is the
wider one.

One design correction made while building S10, on the record rather than silently fixed: the first
version generated load with 8 workers each doing dispose-then-rebegin in a loop, which has a real gap
where a worker owns nothing between the two calls — independent workers occasionally hit that gap
simultaneously by chance, and the scenario flaked (`pollGranted=true` on one of three early runs). Fixed
by having each worker begin its next lease *before* disposing the current one (a rolling window that
never touches zero), which makes "load is continuous" a guarantee rather than a probability that more
workers would only have made less likely.

## Interpretation and limits

This demonstrates the composition works for the shape it tests: one backend process (two for S6/S7,
up to three for S8/S9), operations reported faithfully over stdout, a supervisor that already knows
every target in play. It does **not** demonstrate:

- **An actual MCP client connection.** [Explicitly requested](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542626)
  ("do not equate an open pipe with surviving execution") and not yet built — this probe's transport is
  a synthetic line protocol, not the MCP wire protocol. The next concrete step for this stream, not
  folded into this round given the size of the gap between the two.
- **Real transport continuity end-to-end.** This probe reuses the *measured* result from the earlier
  supervisor prototype rather than re-proving it; it does not itself keep an external client pipe open —
  adding that is straightforward (the earlier prototype already does it) but wasn't the open question.
- **A supervisor that doesn't already know every live target.** `TryReserveAdmission(null)`/`IsQuiescent`
  are correct *if* the ledger already has a record for every operation in flight. A target whose backend
  never reported it (a crash before the first `Begin`, or a target the supervisor doesn't yet track)
  would not block the gate and would still be lost — this is a narrower, sharper version of the "which
  operation classes are reconcilable" question, now answered by
  [E3's reconcilability tiers](https://github.com/Advance-Technologies-Foundation/clio/blob/Alexandr-Kravchuk/detached-operation-probe/experiments/DetachedOperations/reconcilability.md)
  for *which classes* this even matters for.
- ~~A busy-wait cost bound~~ — **the starvation half is now closed (S10)**; the cost-bound half is not.
  `AcquireDrainedWindow`'s drain wait still polls `IsQuiescent` every 25ms rather than using an
  event/callback, and while `drainBudget` is now a caller-supplied parameter rather than a hardcoded
  constant (per Alexandr's migration note), no policy exists yet for *what number to pass*, or for what
  happens after a deferral — retry immediately, back off, surface to an operator. That remains
  activation policy, still open.
- **A mutation control for the *effect/outcome oracle*** — now present, as S7, after kirillkrylov found
  the original oracle unfalsifiable (a fixed `Succeeded` and a PID-suffix match both hid a `fail`
  outcome). What S7 does **not** cover: a mutation control for the *rest* of the composed path — killing
  V1, starting V2, routing pressure. E3's A5i proves its admission barrier discriminates; this probe has
  that guarantee only for the outcome-reporting half, not for whether the kill/respawn/pressure sequencing
  itself would be caught if broken.
- **Concurrency beyond one swapper and one pressure loop**, native resources, real Creatio/DI dependencies,
  or any integration with `UpdatingComposition` — same boundary E3 draws, for the same reason: this probe
  loads nothing through Core and cannot conflict with either other branch.

## Real MCP client: shape, reproduce, results

Two projects, reusing the same linked `OperationLedger.cs` and `Backend`:

- **`McpHost`** — real MCP stdio server (`ModelContextProtocol` + `Microsoft.Extensions.Hosting`, same
  pattern as `Clio10.Mcp.McpAdapter`/`CreatioTools`). `SupervisorState` owns the ledger and the current
  backend process; `SupervisorTools` exposes `start-operation`, `query-operation`, `trigger-swap`.
- **`McpClientProbe`** — real `McpClient` over `StreamClientTransport(host.StandardInput.BaseStream,
  host.StandardOutput.BaseStream)`. One connection for the whole scenario.

```bash
dotnet build experiments/SupervisorQuiescenceComposition/Backend/Backend.csproj -c Release
dotnet build experiments/SupervisorQuiescenceComposition/McpHost/McpHost.csproj -c Release
dotnet build experiments/SupervisorQuiescenceComposition/McpClientProbe/McpClientProbe.csproj -c Release
dotnet run --project experiments/SupervisorQuiescenceComposition/McpClientProbe/McpClientProbe.csproj -c Release --no-build -- \
  experiments/SupervisorQuiescenceComposition/McpHost/bin/Release/net10.0/Clio10.SupervisorComposition.McpHost.dll \
  experiments/SupervisorQuiescenceComposition/Backend/bin/Release/net10.0/Clio10.SupervisorComposition.Backend.dll
```

**Results, three consecutive runs, macOS 27.0.0 (arm64), .NET 10.0.4 — 4/4 every run:**

| run | early query | swap outcome | post-swap query (same id) | new op on swapped backend |
|---|---|---|---|---|
| 1 | Running | `swapped 5833 -> 5835` | Succeeded | Succeeded |
| 2 | Running | swapped (new PID) | Succeeded | Succeeded |
| 3 | Running | swapped (new PID) | Succeeded | Succeeded |

Against Alexandr's five requirements: **(1)** real MCP SDK client — yes, `ModelContextProtocol.Client`,
not a hand-rolled writer. **(2)** one call returns an id, a second asks about it — `start-operation` /
`query-operation`. **(3)** the id crosses as an opaque string, read once via `GetProperty("id")` and
never reconstructed. **(4)** `Running` → `Succeeded` measured; `Unknown` is not — see the architectural
finding above. **(5)** one connection, never reconnected — `McpClient.CreateAsync` is called exactly
once per run, before the swap, and used for every call after it.

## Relationship to the other two streams

Consumes E3's contract and ledger without modifying them (`Contract.csproj` referenced, `OperationLedger.cs`
linked). Does not touch `krylov/runtime-retirement-probe`. Feeds directly into
[`docs/host-update-continuity.md`](https://github.com/Advance-Technologies-Foundation/clio/blob/nikonov/host-update-policy/docs/host-update-continuity.md)'s
open item on composing transport continuity with a durable ledger.
