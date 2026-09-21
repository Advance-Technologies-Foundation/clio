# Host-update continuity: scenario table and acceptance criteria

Discussion: [#1643](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643).

**Round status:** this review round is closed as of
[kirillkrylov's closing checkpoint](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542373) —
decisions handed to Kirill; this document's own recommendation (Flow A first, per
above) aligns with that checkpoint's "keep... conservative defaults" framing. The
[canonical round-conclusion record](https://github.com/Advance-Technologies-Foundation/clio/blob/089de2a75/docs/architecture-experiments.md#round-conclusion-for-kirill-1613)
lists this document's supervisor-handover cases (S1-S7) under measured support. No
further probe work is planned this round; this document stands as written unless new
feedback arrives.

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
| **Execution quiescence** | Is there outstanding work that would be silently lost? | [E3 ledger](https://github.com/Advance-Technologies-Foundation/clio/tree/Alexandr-Kravchuk/detached-operation-probe): `OperationRecord` is portable data (no delegates, no runtime objects), scoped **per target** (`IsQuiescent(target)`, measured in case A1b: envA busy, envB idle, global busy, same process, same moment) | Measured — but per-target scoping is the correct predicate for a **runtime** swap (which never needs it — already proven safe independent of quiescence), not license for a **host** swap to touch only the busy target's owner. For host-level swap, quiescence must be evaluated globally unless per-target isolation is separately built, which nothing here builds. **`IsQuiescent` alone is also no longer sufficient** — see below |

**Correction:** [Alexandr found](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541953)
quiescence and retirement-safety are not the same predicate — there are three
independent reasons a release/host can't be safely replaced, and only one of them is
something `IsQuiescent` can ever observe:

| reason | visible to | ends when |
|---|---|---|
| live work | the ledger, via `IsQuiescent` | the operation terminates |
| degraded evidence (a persistence write failed) | the ledger, via `DegradedScopes` | an operator resolves it |
| an escaped runtime-defined return value (I9/O1) | nobody — not observable from inside the host | the caller drops its own reference |

A scope can be fully quiescent — nothing running — and still unsafe to swap, because a
prior operation's outcome exists only in memory after a failed disk write. **My probe's
gate (`TryEnterSwapWindow`) checks quiescence only; it does not yet consult
`DegradedScopes`, and should.**
[Later correction](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542055):
degraded evidence blocks **host replacement** (the in-memory record would be lost)
but not **runtime retirement** (`_live` is a host-owned collection; unloading the
runtime doesn't touch it) — so this isn't one flag for "can't swap", it's two
different blockers with different scopes, and conflating them would over-restrict the
in-process runtime swap for a risk that's actually specific to host-level replacement.
| **Transport continuity** | Does the client's pipe survive the process being replaced at all? | Thin supervisor owning the client pipe, replaceable child process — [measured](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341): 8.1.0.129 → 8.1.0.131, 1.0s macOS / 1.3s Windows, zero client reconnects | Measured alone; **composed with execution quiescence, admission barrier repaired** — see below |
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

**Provisional, actively being narrowed — not settled.** Alexandr's
[three-tier classification](https://github.com/Advance-Technologies-Foundation/clio/blob/Alexandr-Kravchuk/detached-operation-probe/experiments/DetachedOperations/reconcilability.md)
split this further than "reconcilable vs. not", but the tier-2 conclusion this document
first adopted from it was
[retracted by its own author](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541475)
as circular: an end-marker only exists if the process survived long enough to write
one, which is exactly false in the case the whole thread is about — the process dying
*during* the operation. **The composition work (S6/S7) is unaffected; the specific
`compile-creatio` claim was wrong and is removed below**, not narrowed.

Current state of the table, per the
[latest revision](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541529)
(25/25, macOS + Windows):

| tier | must a swap wait? | after a process loss |
|---|---|---|
| 1 — desired-state verification (renamed from "attributable" — presence proves the world is in the shape requested, not that *this* invocation produced it; distinct from request attribution, per kirillkrylov's counterexamples) | a **policy choice**, not a technical consequence — being able to report non-completion afterward does not preserve the work or make destroying it acceptable | recoverable, but only as "is the artefact there", never "did my call finish" |
| 2 — state-reconcilable, not attributable | **yes, in practice** — `compile-creatio` interrupted mid-flight is uncertain-only, same as tier 3; the server-side "last result" cannot be bound to a specific invocation without a build id/timestamp Creatio doesn't expose today | recoverable only if the process survived long enough to write its own end-marker; otherwise uncertain |
| 3 — no answer through the inspected surface (narrowed from "not reconcilable" — clio's `get-info` carries no restart evidence; that is a property of the surface clio uses today, not proof no platform signal exists anywhere) | yes | `Unknown` through this surface |

What survives across both correction rounds: the classes differ in what they can
honestly report after an interruption, so the *cost* of a global gate is not uniform —
but that does not license skipping the drain for anything. A tier-1 answer establishes
an *outcome*, never *continuity* (kirillkrylov's original qualification, still holding).

Measured on one stand (Creatio 10.1.725.0, .NET Framework, MSSQL, clio 8.1.0.131);
`sync-pages` and `run-process` are reasoned about, not measured. Given two correction
rounds in a row on this specific classification, treat any further conclusion drawn
from it here as provisional until it settles.

**Qualification from review**
([kirillkrylov](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540898)):
reconciliation against Creatio only covers the state Creatio itself authoritatively
tracks. Any local orchestration or postprocessing clio was doing around that call —
writing a follow-up artifact, updating a local cache, chaining a next step — is not
reconciled by asking Creatio anything, and is exactly as lost as scenario (c)'s
`create-app-section` case. `Unknown` is truthful uncertainty about clio's own
bookkeeping, never proof that the work itself continued or permission to replay it.
So the "never needs to block a swap" half of this classification applies narrowly —
to the remote-tracked portion of an operation only — not to whatever local logic
wraps it.

That boundary — not a single global quiescence gate — is what should decide whether
a staged host update can trigger automatically after an unclean loss, or only after
a clean, quiescent shutdown.

## What's still open (not resolved by this document)

- The **global** (not per-target) execution-quiescence signal for host-level swap —
  depends on the in-flight-operation experiment's ledger, aggregated across targets.
- The reconcilable vs. uncertain-only classification of operation classes — **in
  progress, not done**; see the provisional table above and its two correction rounds.
- Call classification (read-only vs. side-effecting) for scenario (b) — undesigned.
- ~~Composing transport continuity with a durable ledger~~ — **done, including a real
  respawn under concurrent handover pressure, against the repaired barrier.**
  [`experiments/SupervisorQuiescenceComposition`](https://github.com/Advance-Technologies-Foundation/clio/tree/nikonov/supervisor-quiescence-probe/experiments/SupervisorQuiescenceComposition).
  History, on the record rather than smoothed over: the first pass (S1-S4) ran against
  `e3138962c`, which
  [kirillkrylov's review](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540898)
  found had three reproduced defects (admission race, one-directional window exclusion,
  completion-boundary race) that this probe's sequential scenarios hadn't been shaped
  to trigger either way. Alexandr
  [repaired all three at `551f25c92538`](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541013).
  Re-run against the repair, S1-S4 unchanged within noise as expected, plus **S5**
  (single sampled handover-admission attempt, refused). A
  [second independent review](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541164)
  then found S1-S4 were drain-before-termination measurements only — no respawn ever
  happened, despite the README's original "Kill + respawn" claim. **S6** closed that
  gap: a real V1→V2 respawn, admission closure held through termination *and* V2's
  confirmed readiness (not just the kill), and continuous concurrent admission pressure
  throughout the handover rather than one sampled attempt. A
  [third review](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541476)
  then showed S6's own oracle was unfalsifiable — changing the post-handover operation
  to `fail` still passed, because the outcome was never actually checked and the effect
  match was PID-suffix rather than exact. Fixed, and **S7** added as the negative
  control (a failing operation, asserted `Failed` with the effect line correctly
  absent), mutation-checked by reverting the fix and confirming S7 catches it before
  restoring it. **7/7, three consecutive runs.**

  Limits: assumes the ledger already has a record for every live target; polls rather
  than using an event/callback; no wait-budget/timeout policy if quiescence never
  arrives — activation policy, still open; no mutation control for the *rest* of the
  composed path (kill/respawn/pressure sequencing — only the outcome oracle has one
  now, via S7); concurrency beyond one swapper and one pressure loop is untested.

~~A small counterexample probe demonstrating "idle transport ≠ idle process"~~ —
**superseded.** [Alexandr-Kravchuk pointed out](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540612)
this is already exactly the `create-app-section` A/B plus E3's control C2; a third
demonstration of the same property adds no evidence. Retracted in favor of the
composition probe above.

## Two host-update mechanisms, compared

Per [kirillkrylov's mid-term checkpoint](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541706),
this is the deliverable: state precisely what each mechanism preserves, what remains
running, what cannot update transparently, how refused requests and failed startup
behave, and recommend the smallest mechanism meeting the chosen requirement.

### Flow A — stage the update, activate at a natural restart

**Correction to an earlier version of this section**
([kirillkrylov](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541834)):
"zero new mechanism, zero new failure modes" was an overclaim. Removing the *live
handover* doesn't remove the design surface — it only removes urgency from it, because
nothing is live when it happens. Four residual cases exist and are real work, not
free:

- **Staging.** The new host build must be acquired and verified before it's safe to
  activate — the trust/signing gate Alexandr flagged and explicitly deferred in the
  very first measurement post of this thread. Flow A does not avoid this question; it
  just isn't blocked by it the way a live-handover flow would be.
- **Version selection.** At restart, which staged version does the launcher pick? This
  is literally [Option A from the thread's opening measurement](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341) —
  "side-by-side install + newest-wins launcher" — restated, not superseded.
- **Settings compatibility.** Config/settings must remain readable across versions or
  be migrated. Not addressed anywhere in this thread yet.
- **Failed startup / rollback.** See the dedicated section below — this is the one
  kirillkrylov specifically asked to have described before any further probe work.

*What it preserves.* Everything already true today for the **live session**.
Compatible runtime updates (Composition+Primitives) already apply in-process, with no
restart at all — this flow changes nothing about that path.

*What remains running.* The **current host process**, for the live session, until the
client itself reconnects. For host/Core/transport-layer changes, this is exactly the
original problem the discussion opened with:
[a month-long session may never produce a natural reconnect](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539699),
so a fix can sit staged and undelivered indefinitely. This flow does not hide that
cost — it is the cost.

*What cannot update transparently.* All of it, for as long as the session runs without
reconnecting. There is no partial credit.

*Refused requests / failed startup, live session.* N/A — nothing is ever killed while
a client is attached without the client's own action, so there is no *live* handover
window to fail inside. This does not mean startup can't fail — see below, where it can,
just not in front of an attached client.

*New failure modes introduced, live session.* None. The residual cases above are new
design surface, but none of them execute while a client is connected.

### Flow B — supervisor-managed backend replacement

*What it preserves.* Transport continuity is measured
([1.0s macOS / 1.3s Windows, zero reconnects](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341)).
Execution continuity for **ledger-tracked** operations is measured on the happy path
(S1-S5) and under a real respawn with concurrent admission pressure (S6-S10, 10/10,
[independently cross-reviewed](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541706)
after its own oracle bug was found and fixed).

**No longer a synthetic pipe.** A real `ModelContextProtocol` client
([`McpClientProbe`](https://github.com/Advance-Technologies-Foundation/clio/tree/nikonov/supervisor-quiescence-probe/experiments/SupervisorQuiescenceComposition/McpClientProbe))
now drives a real MCP server ([`McpHost`](https://github.com/Advance-Technologies-Foundation/clio/tree/nikonov/supervisor-quiescence-probe/experiments/SupervisorQuiescenceComposition/McpHost),
same pattern as the shipping `Clio10.Mcp` adapter) through the same reserve-then-drain
swap: start an operation over MCP, query it mid-flight (`Running`), trigger a real
V1→V2 swap over MCP, query the **same opaque id on the same never-reconnected
connection** afterward (`Succeeded`), confirm a real PID change. 4/4 on first run,
3/3 consecutive runs. **Architectural finding, not yet closed:** the third of the
three required outcomes (`Unknown`, alongside `Running`/`Succeeded`) needs the
MCP *pipe-owning* process itself to be lost while the client stays connected —
`McpHost` cannot produce that, because it owns both the pipe and the ledger in one
process. Demonstrating `Unknown` honestly needs the genuine three-tier split Flow B
describes elsewhere in this document (client → thin pipe-owning supervisor →
separately replaceable backend/ledger owner) — a different architecture, not a
missing test case on the current one.

*What remains running.* A **second, permanent process** — the supervisor itself. It
becomes [its own compatibility boundary that must stay stable and can never update
itself mid-session](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539699).
Flow A has no equivalent standing liability; Flow B trades the reconnect gap for a
component that now has an indefinite lifetime of its own.

*What cannot update transparently.* Two categories, not one:
- Any operation the ledger doesn't know about. [Alexandr's fresh finding](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541695)
  is concrete evidence this gap is real *today*, not hypothetical: clio already drains
  two classes of detached work at shutdown
  (`ComponentRegistryClient.DrainAsync`, a flush scheduler) with a 10-second budget —
  and **explicitly does not drain the heartbeat-detached operations** that are the
  entire subject of this thread (`compile-creatio`, `create-app-section`). The
  drain-before-exit pattern this thread has been designing already exists in clio; it
  just doesn't cover the class that matters yet.
- Any swap where quiescence never arrives. No wait-budget or timeout policy exists
  (see S6/S7 limits above) — this is activation policy, and it is still open.

*Refused requests.* Measured and correct at the ledger level: a new operation for a
gated scope during the window throws `SwapWindowHeldException` (S5, S7's admission
pressure). Not yet designed: what an MCP-level caller sees when that happens, or a
retry/backoff contract — today it's a raw exception in a probe, not a defined client
experience.

*Failed startup.* **Now measured** (S8/S9, ordering C: bounded V2 attempts, bounded
fallback to a restarted V1, a definite `Unavailable` terminal if every attempt fails)
— see below for the full history, including the ordering bug this document itself
had to correct twice before the proof existed.

### Failed-startup / rollback behavior, both flows

Per kirillkrylov's request, described before any further probe work rather than
after.

**Flow A.** The failure surface is entirely between sessions — a launcher picking a
broken staged version at the *next* reconnect, with no client attached to notice
immediately. Two designs, not yet chosen between:
- **Newest-wins, no fallback.** A broken staged build blocks every future launch until
  fixed. Simplest, but a bad build is an outage for every session that reconnects
  until someone intervenes.
- **Newest-wins with fallback to last-known-good on startup failure.** Requires the
  launcher to verify success (a health check) before treating the new version as
  committed, and to retain the previous version until that check passes — which is the
  same retention/reclamation question E2's runtime-retirement probe already answers
  for runtime bundles, applied one layer up, to the host binary itself.

**Flow B.** Corrected from an earlier version of this section
([kirillkrylov](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541905)):
"confirm V2 first, then retire V1" is not one universal correct ordering — it is one
of three, and it has a precondition that must be stated, not assumed. Three orderings,
compared on concrete failure semantics rather than asserted:

| ordering | outage window if V2 fails | precondition | risk |
|---|---|---|---|
| **A. Kill V1, then start V2** (my probe's current code) | Total, indefinite — no backend exists until manually fixed | none | This is the actual gap; not a missing test, a wrong default |
| **B. Confirm V2, then retire V1** ("both generations coexist") | None, if the precondition holds | **V1 and V2 must not require the same exclusive resource.** Process coexistence itself is an established clio pattern (see `McpHostPresence` below); which specific *operations* would still conflict is an incomplete inventory, not a closed one | **Fixed, not just flagged.** The readiness check now uses `ping`/`pong` — no ledger interaction, no effect-file write — replacing the earlier synthetic `start`-shaped probe that ran the same code path as real work |
| **C. Kill V1, start V2; on V2 failure, restart V1 from its retained binary** | **Measured, not just corrected**: [S8/S9](https://github.com/Advance-Technologies-Foundation/clio/tree/nikonov/supervisor-quiescence-probe/experiments/SupervisorQuiescenceComposition) — bounded attempts on both legs, a real recovery (S8) and a real `Unavailable` terminal when both legs exhaust (S9), 9/9 across three runs | Bounded via explicit attempt counts (2 + 2) and a definite terminal outcome — no open-ended deadline policy yet, just a bounded retry count | V1 can itself fail to restart, or share whatever broke V2 (same config, same dependency) — S9 measures exactly this case rather than describing it |

**Exclusivity, inventoried — then explicitly walked back as incomplete, not settled.**
[Alexandr's first pass](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541984)
found:
- `iis-port-{port}.lock` (`IisDeploymentPortReservation`) — machine-wide, exclusive per
  port. Two generations deploying to the same IIS port genuinely cannot coexist.
- The DbHub TOML settings-store lock — exclusive, but held only per-write (`using`),
  not for the process lifetime. Too brief to block B in practice.
- `McpToolExecutionLock.CwdLock` — in-process only, irrelevant to a supervisor model
  where V1/V2 are separate processes.
- The client pipe is exclusive by construction — the shape of the transport, not an
  inventoriable lock.

**Retracted before it hardened**
([kirillkrylov](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542098):
"absence of a searched symbol does not establish coexistence"; [Alexandr's own
follow-up](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542168)
found four things the first pass missed):
- **`DeploymentTargetReservation`** — a *general* machine-wide reservation mechanism
  keyed by `(kind, identity)`. The IIS port lock is **one instance of it, not the only
  one** — the kind of exclusion isn't enumerated, only one example of it.
- **`StaleWorkerRegistry`'s `workers.lock`** — a cross-process registry lock not in the
  first pass.
- **`{AppSettingsFile}.lock`** in `ConfigurationOptions` — separate from the DbHub lock
  already found.
- **`McpHostPresence`** — the one finding that cuts the other way: clio already records
  every running MCP host on disk with its **PID and version**, and calls
  `TryFindResidentMcpHost` at startup. Concurrent hosts aren't just permitted, they're
  already tracked. This is real, positive evidence that process coexistence (ordering
  B's core requirement) is an established pattern, even though the exclusion inventory
  around it is incomplete.

**So, correctly stated:** ordering B's precondition is *not* "available except IIS
port" — that understates the exclusion set, since `DeploymentTargetReservation` is
general and its other kinds aren't enumerated. What's solid is narrower: clio already
supports concurrent MCP host processes as a tracked, working pattern (`McpHostPresence`),
and *some* operations (IIS deployment, at minimum) hold machine-wide exclusive
reservations that would block two generations acting on the same target simultaneously.
Which operations do is still an open inventory, not a closed one. Kirillkrylov's
sharper framing: process coexistence and *safe simultaneous operation* are different
questions, and the inventory so far only ever spoke to the first.

**Ordering C, corrected**
([kirillkrylov](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542014)):
"bounded but nonzero" overstated it — restarting V1 is an *attempt*, and the attempt
itself can fail, including for the same reason V2 failed (shared broken config or
dependency). A real design needs bounded retries with an explicit deadline and a
defined terminal "unavailable" outcome, neither of which exist yet. That's the same
activation-policy gap already open (A5h's wait-budget/starvation question), now with a
second concrete instance.

So the comparison isn't "A is wrong, B is right" — it's: **A has no fallback and
should not be the default; B is the zero-outage option, and process coexistence for it
is an established clio pattern, but the operations that would block it are an
incomplete inventory, not a closed one; C is the fallback for whatever exclusion B
can't clear, but needs a bounded-attempt policy before it can be called a guarantee
rather than a best effort.**

One more asymmetry worth naming: Alexandr's
[P1 finding](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541844)
— a disk-write failure during evidence persistence leaves an operation `Running`
forever, its scope permanently unswappable, because invariant I4 (evidence durable
*before* the terminal is observable) is what makes replacement safe against an
unrecorded terminal, and the same ordering is what turns a disk failure into a stuck
scope. That is orthogonal to V2-startup failure but compounds with ordering B above: a
stuck scope from a persistence failure would make even confirm-then-retire wait forever
for a window that structurally cannot open, which is exactly the wait-budget/starvation
gap already flagged as open (A5h) and now has a second, independent cause.

### Recommendation

**Flow A first, for host-layer changes specifically — runtime-layer changes
(Composition/Primitives) are a separately solved case and this recommendation does
not touch them.** Mirroring the format [Alexandr adopted for his own
recommendations](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542650) —
a stated preference, the simplest credible alternative, and what would disprove it —
rather than a bare preference.

**Simplest credible alternative:** ship Flow B (persistent supervisor) as the default
for host-layer changes too, now that S8/S9 close the failed-V2-startup gap with
measured recovery and a definite `Unavailable` terminal. It is genuinely more
complete than when this recommendation was first written.

**Why I still prefer Flow A first:** three items separated this document's "happy
path measured" from "safe to ship". **One is now closed** — S8/S9 measure real
fallback recovery and a truthful terminal outcome under bounded attempts, not just a
design description. **Two remain, and neither is a probe-scale fix:**
- The drain-coverage gap Alexandr found is a property of shipped clio, not of this
  probe: `DrainHostBackgroundWork` already drains two classes of work with a budget,
  and explicitly not the heartbeat-detached class this whole thread is about.
- The activation-policy timeout is still undesigned, and this is no longer a
  predicted risk — Alexandr
  [tested his own disproof criterion and it held](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542770):
  `TryEnterSwapWindow`, polled against continuous overlapping work, **never granted a
  window for the entire 2-second observation window, on both platforms**
  (`windowEverGranted=false`). Poll-for-idle degenerates into "the update never
  happens" — not a slower version of the fix, the original problem under a different
  name. This is the exact mechanism **my own S2/S4/S6-S9 wait loops use**
  (`while ((window = ledger.TryEnterSwapWindow(...)) is null) ...`); none of my
  scenarios triggered it only because none run continuous admission pressure during
  the wait, not because the mechanism is safe.

  **Ledger-vs-caller, settled**
  ([Alexandr](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542906),
  reversing his own earlier "say the word and I'll move it"): `TryReserveAdmission`
  must live on the ledger, not the caller. A caller can only *wait*; the wait becomes
  finite only if new admissions stop, and only `Begin` can refuse an admission. A
  caller-side gate that tried to close a scope from outside would have to re-implement
  admission authority one layer up, with two places that could disagree about whether
  a scope is open.

  **Migration done.** S2/S4/S6-S9 now use `AcquireDrainedWindow` (reserve via
  `TryReserveAdmission`, then wait only for already-admitted work to drain, bounded by
  a caller-supplied `drainBudget` parameter — never a hardcoded constant, per
  Alexandr's note that a real `compile-creatio` runs minutes, not this probe's
  milliseconds; an unbounded drain would be worse than the deferral it replaces).
  **S10** is this stream's own disproof, mirroring Alexandr's D1/D2 against the
  host-swap case: under continuous admission pressure (8 rolling-window concurrent
  workers), the old poll never grants a window and the new reserve-then-drain always
  does. **10/10, five consecutive runs.** One design bug found and fixed while
  building it: the first load-generation pattern (dispose then rebegin) had a real
  gap between releasing one lease and acquiring the next, and independent workers hit
  that gap simultaneously by chance on one of three early runs — fixed by overlapping
  lease lifetimes so ownership never touches zero, making continuity a guarantee
  rather than a probability.

Flow B also carries a standing cost Flow A doesn't: a permanent second process that
[becomes its own compatibility boundary and can never update itself mid-session](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539699).
That cost doesn't shrink as more of Flow B gets measured — it's structural, not a gap
to close.

**What would disprove this recommendation:** evidence that staging-and-waiting is not
actually "eventually delivered" in practice — that real long-lived sessions
essentially never hit a natural restart boundary, so a host fix staged under Flow A
would sit undelivered indefinitely rather than just later. If that's the common case
rather than a tail risk, "eventually" is functionally "never", and the calculus flips:
Flow B's standing cost becomes worth paying regardless of the two remaining gaps,
because Flow A would not actually be solving the problem this discussion opened with.
This is an empirical question about session shapes in practice, not one this document
or its probes can answer from the inside.

**Shared assumption, named explicitly rather than left as two separately-caveated
recommendations**
([Alexandr](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542906)):
this recommendation and his "defer, never kill" are the same shape — both choose
*wait rather than force*, and both are honest only if the waiting provably
terminates. His disproof is the bound (an unbounded drain is worse than the deferral
it replaces); mine is the empirical question above. Neither is answerable from inside
a fixture. Recorded together because a reader evaluating either recommendation in
isolation would miss that they share one unmeasured premise, not two independent
ones.

**The shared premise just failed its own test, once.**
[Alexandr measured a hung operation against the reservation mechanism itself](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18543530):
a reservation was held for its full budget, the operation was still `Running` when
the budget expired, the reservation released, and the hung operation was **neither
killed nor completed** — it just kept not-terminating while new work resumed around
it. This is not a new disproof; it is the *same* one (waiting must provably
terminate) landing on concrete evidence rather than staying hypothetical. It sharpens
what "activation policy" has to define: not just a drain-budget number, but an
explicit answer for what happens to a scope holding a hung operation after the
budget expires — leave it running and retry the deferral indefinitely, or something
else. Migrating S2/S4/S6-S9 to `TryReserveAdmission` (below) does not answer this;
it inherits the same open question.

## Position on the six candidate boundaries

Per [kirillkrylov's checkpoint](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542190),
from this document's lane only — measured evidence versus none, not a vote on the
other two streams' claims:

- **Complete runtime per workflow.** Not confirmed here — outside this document's
  scope entirely; nothing in S1-S7 touches runtime-composition packaging.
- **Separate response / outcome / ownership.** Not the origin of this claim (that's
  E3's I1/I2), but S6/S7 are consistent, independent evidence for it at a different
  layer: V1's operation completed and its outcome was correctly preserved across a
  *real process kill*, which is the process-level analogue of the same separation.
  Supporting evidence from a different axis, not proof of the claim.
- **Portable dynamic values.** Not measured here — E3's O1/I9 lane.
- **Host-owned evidence separate from runtime lifetime.** As stated, this is E3's
  in-process retirement axis (P3), not this document's. A related but distinct data
  point exists here: the ledger in S1-S7 lives in the supervisor process, not in
  V1/V2, so evidence already survives backend replacement — but that's evidence
  surviving *process* replacement, a different claim from evidence surviving
  *runtime-unload*.
- **Explicit versioned settings.** Not measured — open per this document's own
  "settings compatibility" residual case under Flow A.
- **Trusted extensions and explicit host activation guarantees.** This is the one in
  this document's actual lane, and it's confirmed only partially. The three-way
  ordering comparison and S1-S7 (7/7) characterize *what activation requires*, not that
  any specific guarantee is settled — the activation-policy timeout, a bounded
  failed-V2-startup retry, and the full exclusivity inventory are all still open, per
  the sections above. "Trusted extensions" (package signing) is unmeasured by anyone
  in this thread and has been an open gate since the opening post.

**Explicit retraction, per kirillkrylov's request:** "zero-outage except IIS" was
already retired in the exclusivity section above, before this checkpoint —
[recorded here](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18542199).
Restated for the record in this checkpoint's own terms: **startup coexistence is a
conditional option** (available where no exclusive resource is contended, established
as a working pattern via `McpHostPresence`), **not an established universal
property** — which operations contend for which resources remains an open,
incomplete inventory.

## Sources

- [Alexandr-Kravchuk's `create-app-section` A/B and the "one process = one session" finding](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539699)
- [Alexandr-Kravchuk's supervisor-swap drain-timeout and transport-continuity measurement](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18539341)
- [kirillkrylov's runtime-retirement probe](https://github.com/Advance-Technologies-Foundation/clio/tree/krylov/runtime-retirement-probe)
- [Alexandr-Kravchuk's macOS reproduction and open-decision reply](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540212)
- [Split-ownership agreement](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540013)
- [kirillkrylov's three-predicate correction and shared architecture-experiment record](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540566) / [`docs/architecture-experiments.md`](https://github.com/Advance-Technologies-Foundation/clio/blob/krylov/clio-10-experiment/docs/architecture-experiments.md)
- [Alexandr-Kravchuk's `detached-operation-probe` (E3), acceptance cases A1–A5, R1, controls C1/C2](https://github.com/Advance-Technologies-Foundation/clio/tree/Alexandr-Kravchuk/detached-operation-probe)
- [`nikonov/supervisor-quiescence-probe` — transport continuity composed with E3's quiescence ledger against a real process swap](https://github.com/Advance-Technologies-Foundation/clio/tree/nikonov/supervisor-quiescence-probe/experiments/SupervisorQuiescenceComposition)
