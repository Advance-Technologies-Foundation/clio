# Host-update continuity: scenario table and acceptance criteria

Discussion: [#1643](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643).

## Scope

This document does not implement or re-derive either of the other two experiment
streams; it consumes their evidence and defines the acceptance surface the combined
design has to satisfy. It owns one question: **what does the clio MCP host actually
promise a long-running client session about updates, and where does that promise
currently hold or fail?**

Two mechanisms are in scope:

- **Runtime-level swap** (Composition + Primitives, in-process, `AssemblyLoadContext`) —
  proven working for the case it targets; see
  [`runtime-update-proof.md`](runtime-update-proof.md) and
  [`primitive-contract-boundary.md`](primitive-contract-boundary.md).
- **Host-level swap** (Core / Transport / Adapters) — no working mechanism exists yet;
  this is the frozen ~30–40% of clio's own change volume the runtime swap cannot reach
  by construction, per the commit-classification estimate in the discussion.

Working assumption under test, stated by kirillkrylov in the discussion: *"compatible
runtime updates need no user action; host upgrades are explicitly outside that
guarantee, and are applied only at a natural disruption boundary."* This document's
job is to say, scenario by scenario, whether that assumption currently holds.

## Scenario table

| # | Scenario | Runtime-level swap | Host-level swap | What must survive | Client observes today | Rests on / still owned by |
|---|---|---|---|---|---|---|
| **(a)** | Idle connection, no active call, *assumed* no detached work | ✅ proven safe | ⚠️ **not actually safe** — "idle" is only zero in-flight requests, not zero outstanding work | An explicit "no outstanding work" signal that doesn't exist yet | Nothing, if truly idle — but we can't currently *prove* it's truly idle | Unowned as a predicate; the operation-lifetime experiment is the prerequisite (see below) |
| **(b)** | An ordinary call is executing (e.g. blocked on a slow/unreachable network call) | ✅ proven — old call finishes on its captured runtime, Core untouched | ❌ proven unsafe as a *silent* swap — measured 15.8s drain-timeout, then `backend closed pipe` | Whether the call is read-only vs. side-effecting (no classification exists today) | For effect-bearing calls: a false failure indistinguishable from "didn't happen" | Call-classification is undesigned; drain cost is measured, unmitigated |
| **(c)** | Call returned an operation-id, detached work continues past the response deadline | ❓ not yet proven either way — retaining runtime instances across a swap "does not yet establish an owned lifetime for detached work" | ❌ proven unsafe — measured A/B on `create-app-section`: control run produced the section + entity schema at T+4.5min; swap run, backend replaced at T+1029ms after the response deadline fired, produced **neither**, while the tool's own guidance said "do NOT retry" | Task ownership/scope across the swap, plus durable/queryable outcome evidence | A stuck agent told to poll for something that will never appear | **Blocking gap for both mechanisms.** Owned by Alexandr's in-flight-operation experiment (acceptance cases published [here](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540212)); Core-owned-record design proposed, unbuilt |
| **(d)** | External system (Creatio) accepted work that outlives clio itself (e.g. `compile-creatio`, which Creatio itself serializes/rejects retries for) | same unproven status as (c) | same unsafe status as (c), but the source of truth lives on the **remote** system, not just clio's memory | Not the operation state itself — a reconciliation contract ("ask Creatio about X, here's how") | `success:true, status:not-found` — actively false, worse than "unknown" | Narrower/cheaper fix than (c): status tools could fall back to the remote source instead of trusting only local memory. Not yet scoped as its own ticket |
| **(e)** | Client naturally reconnects | irrelevant — already transparent | ✅ unambiguously safe **today**, via the existing close-stdin/wait-for-exit mechanism | Any pending operation must be resolvable *after* reconnect too | Version change; ideally a summary of what's mid-flight | Mechanically solved. Not a trigger the policy should be designed around (a month-long session may never produce one — see discussion [comment](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540385)), but a fallback path that exists independently of whatever predicate is built for (a). Only *complete* as a handoff once (c)/(d) are solved |

## Implication for the proposed policy — revised: three separate predicates

**Correction to an earlier version of this document.** The first draft treated "safe
to swap" as one scoped quiescence check. Per-review
([kirillkrylov](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540566)),
that conflates three genuinely independent predicates, and the per-target scoping
proposed below was overreach: it read as licensing *partial* host replacement
(swap for environments {X,Y}, defer only Z), which nothing demonstrates. One host
process serves every target; replacing it affects all of them regardless of which
target was busy. Target A being idle does not protect target B's in-process work.
[Alexandr-Kravchuk independently reached the same correction](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540587)
while publishing the evidence below.

The three predicates, kept separate:

| Predicate | Question | Mechanism with evidence | State |
|---|---|---|---|
| **Execution quiescence** | Is there outstanding work that would be silently lost? | [E3 ledger](https://github.com/Advance-Technologies-Foundation/clio/tree/Alexandr-Kravchuk/detached-operation-probe): `OperationRecord` is portable data (no delegates, no runtime objects), scoped **per target** (`IsQuiescent(target)`, measured in case A1b: envA busy, envB idle, global busy, same process, same moment) | Measured — but per-target scoping is the correct predicate for a **runtime** swap (which never needs it — already proven safe independent of quiescence), not license for a **host** swap to touch only the busy target's owner. For host-level swap, quiescence must be evaluated globally unless per-target isolation is separately built, which nothing here builds |
| **Transport continuity** | Does the client's pipe survive the process being replaced at all? | Thin supervisor owning the client pipe, replaceable child process — [measured](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341): 8.1.0.129 → 8.1.0.131, 1.0s macOS / 1.3s Windows, zero client reconnects | Measured, but it is exactly the mechanism that independently loses operation continuity (child process dies, in-memory registries with it) — solving this predicate alone does not solve execution quiescence |
| **Activation policy** | Given the other two are satisfied, *when* does a swap actually trigger? | — | **Open.** Not the same question as quiescence — a natural reconnect (scenario e) is an existing transport-reinitialization opportunity, not proof of detached-work or status continuity for the general case, and the held-call runtime proof (scenario b) should not be read as proof of every dead-socket case either |

### Why durable evidence changes the shape of the quiescence requirement

Quiescence is only a hard requirement for operation classes whose outcome **cannot**
be reconstructed after the process is gone. The detached-operation probe's recovery
cases narrow that set directly: case A4a reports a lost in-flight operation as
`Unknown`/`history-unavailable` rather than falsely `NotFound`, and A4c shows a
recovered host can surface it as `recovered-unknown`. That is not resumption — the
work itself is still lost — but a truthful "uncertain" answer is a materially
different failure than today's false `not-found` (scenario c/d above), and it means
a swap doesn't strictly need to *wait* for that class of work to finish, only to
guarantee it will be reported honestly afterward.

This splits scenario (c)/(d)'s remaining gap in two, per
[Alexandr's open question](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540587):
**which operation classes can be reconciled against an authoritative source
(Creatio, for scenario d) and therefore never need to block a swap at all, and which
can only ever report uncertain and therefore genuinely require quiescence first.**
That boundary — not a single global quiescence gate — is what should decide whether
a staged host update can trigger automatically after an unclean loss, or only after
a clean, quiescent shutdown.

## What's still open (not resolved by this document)

- The **global** (not per-target) execution-quiescence signal for host-level swap —
  depends on the in-flight-operation experiment's ledger, aggregated across targets.
- The **reconcilable vs. uncertain-only** classification of operation classes, which
  determines whether quiescence can be skipped for a given operation type.
- Call classification (read-only vs. side-effecting) for scenario (b) — undesigned.
- **Composing transport continuity with a durable ledger — this is the concrete next
  artifact for this stream.** The record shape is no longer hypothetical: E3 publishes
  `OperationRecord(Id, Target, StartedUtc, RuntimeVersion, State, FinishedUtc?, Code?)`
  and `IsQuiescent(target?)` in
  [`experiments/DetachedOperations/Contract/Contract.cs`](https://github.com/Advance-Technologies-Foundation/clio/blob/Alexandr-Kravchuk/detached-operation-probe/experiments/DetachedOperations/Contract/Contract.cs).
  Nobody has run a supervisor that consults `IsQuiescent(target)` before swapping,
  against the exact workload that broke (`create-app-section`, detached past its
  response deadline). Two possible outcomes, both informative: the failure does not
  recur (activation policy on top of the two existing mechanisms is sufficient), or it
  does recur despite passing quiescence (the gap is a race between the check and the
  swap, or in the two mechanisms' composition, not in either alone). This is a real
  implementation task, not a documentation update — scoping it before building.

~~A small counterexample probe demonstrating "idle transport ≠ idle process"~~ —
**superseded.** [Alexandr-Kravchuk pointed out](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540612)
this is already exactly the `create-app-section` A/B plus E3's control C2; a third
demonstration of the same property adds no evidence. Retracted in favor of the
composition probe above.

## Sources

- [Alexandr-Kravchuk's `create-app-section` A/B and the "one process = one session" finding](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539699)
- [Alexandr-Kravchuk's supervisor-swap drain-timeout and transport-continuity measurement](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341)
- [kirillkrylov's runtime-retirement probe](https://github.com/Advance-Technologies-Foundation/clio/tree/krylov/runtime-retirement-probe)
- [Alexandr-Kravchuk's macOS reproduction and open-decision reply](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540212)
- [Split-ownership agreement](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540013)
- [kirillkrylov's three-predicate correction and shared architecture-experiment record](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540566) / [`docs/architecture-experiments.md`](https://github.com/Advance-Technologies-Foundation/clio/blob/krylov/clio-10-experiment/docs/architecture-experiments.md)
- [Alexandr-Kravchuk's `detached-operation-probe` (E3), acceptance cases A1–A4, R1, controls C1/C2](https://github.com/Advance-Technologies-Foundation/clio/tree/Alexandr-Kravchuk/detached-operation-probe)
