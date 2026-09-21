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

## Implication for the proposed policy

"Stage the host, activate on the next natural disruption boundary" is safe **today
only for (e)**. Treating (a) as an equally safe opportunistic trigger is premature —
it is missing a concrete precondition (a real zero-outstanding-work check), not an
optimization to defer.

The precondition is not simply "count of in-flight requests." The `create-app-section`
A/B is direct evidence: the call had already returned to the client (`in-progress`,
response deadline fired) when the backend was swapped 1029ms later. From the transport's
point of view the session was completely idle; the work still died. A quiescence check
built only on open requests would have said "safe to apply now" and been wrong.

### Scoping the quiescence predicate

A predicate that must answer "is *anything* still running, anywhere" before allowing a
host update risks never firing in practice: a session juggling several environments —
which is normal usage, not an edge case — would rarely if ever report *global*
quiescence, even though most of those environments have no pending work at all.

The predicate should be scoped **per target/environment**, not per process, once the
operation record from the in-flight-operation experiment carries a target/environment
key. That turns scenario (a) into: *quiescent for environments {X, Y}, deferred only
for environment Z, which has a compile in flight* — allowing partial progress instead
of an all-or-nothing gate.

## What's still open (not resolved by this document)

- The zero-outstanding-work predicate itself — depends on the in-flight-operation
  experiment landing a portable operation record with target/environment scope.
- Call classification (read-only vs. side-effecting) for scenario (b) — undesigned.
- The remote-reconciliation fallback for scenario (d) — a smaller, likely separable
  fix from (c); not yet scoped as its own piece of work.
- **A small counterexample probe** demonstrating "idle transport ≠ idle process"
  directly (an idle stdio connection, zero in-flight requests, one detached background
  task still writing when the backend is replaced) — isolates the claim this whole
  document leans on, without duplicating either of the two implementation branches.
  Planned as a follow-up in this branch.

## Sources

- [Alexandr-Kravchuk's `create-app-section` A/B and the "one process = one session" finding](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539699)
- [Alexandr-Kravchuk's supervisor-swap drain-timeout measurement](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341)
- [kirillkrylov's runtime-retirement probe](https://github.com/Advance-Technologies-Foundation/clio/tree/krylov/runtime-retirement-probe)
- [Alexandr-Kravchuk's macOS reproduction and open-decision reply](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540212)
- [Split-ownership agreement](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540013)
