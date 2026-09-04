# Story 7: Sticky supervision + parent-owned configuration-build reservation

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 7
**Status**: ready-for-dev
**Size**: L

## As a
caller polling a long-running operation

## I want
my status polls to reach the worker that is running the operation

## So that
`compile-status` answers from the process that holds the compile, not from an empty registry in another one

## Design

### Prerequisite (inside this story): session-key normalisation lands BEFORE the registries move

**This is a first-class part of story 7, not a dependency on another story.** The original review finding
asked for `depends_on: story 5` — the story that owns normalisation in the credential threat model. That is
no longer available: **story 5 was deferred on 2026-08-18** (ADR §5, OQ-9), so a `depends_on` edge would
point at work nobody is doing and would silently block this story forever. The normalisation is therefore
re-homed here, in **stdio scope** — which is the whole scope the worker path has while the stdio-only gate
holds — and stated as an ordering constraint inside the story.

**Why the order is load-bearing.** Every registry this story moves is keyed by tenant. If the registries
move to the parent while a single target can still produce two different keys, the parent's registry is
split at birth: `compile-creatio` invoked with a registered environment name and `compile-status` polled
with an explicit URI for the same environment land in two buckets. The visible symptom is not an error —
it is `compile-status` answering "no such operation" for a compile that is running. Normalising afterwards
does not repair it either, because the split keys are already in flight. **Normalise first, then move the
registries.**

**The defect, cited from the current code** (`clio/Command/McpServer/Tools/ToolCommandResolver.cs:361-379`,
read 2026-08-18 — note the *pre*-ENG-94529 shape `options.Environment ?? settings.Uri` no longer exists):

```csharp
string identity = string.Concat(
    options.Environment ?? DefaultIdentifier, "|",   // DefaultIdentifier is the literal "default" (:72)
    settings.Uri ?? string.Empty);
```

So one target yields **two** keys — `myenv|http://x` through the name branch and `default|http://x` through
the URI branch. ENG-94529 put the URI into the identity (which fixed a re-pointed environment handing back
a stale client) but did **not** make the two branches converge. ADR rule 10 and §1.4 record the same fact.

**What this story must do, before AC-01…AC-04:**

- One resolved target produces **one** key, whether it was reached by registered name or by explicit URI.
- The folding follows the **conservative component-by-component algorithm in the credential threat model
  T-5** — scheme case, IDNA/Punycode and RFC 5952 host forms, default-port elision, one trailing slash,
  percent-encoding; `http`/`https` and hostname/IP stay **distinct**; userinfo, query and fragment are
  **rejected**, not normalised. Anything the algorithm does not name is a different target. Over-normalising
  merges two targets, and on a sticky worker that is a credential crossover, not a cache miss.
- Scope: **stdio only.** The principal and credential-fingerprint components of the sticky key (R-5) stay
  with the deferred story 5; on stdio the target is the whole key, because the child reads
  `appsettings.json` itself and no credential crosses the boundary. When `mcp-http` is revived (OQ-9), the
  same normalisation is reused with the other two components added — it is not rewritten.

### Sticky supervision

- **Private completion signal** between worker and parent (rule 5). "Reap on terminal status" cannot work: only two operation registries exist — `ICompileOperationRegistry` (`BindingsModule.cs:738`) and `IRestartOperationRegistry` (`:744`). `install-process-builder` and `create-app-section` have none, and `restart-by-credentials` is deliberately unreportable, so three of the four long-running modes have no terminal status to reap on.
- **Registry cardinality — two different keys, and conflating them fails either way** (cross-call state §3). The compile/restart *status* registries stay keyed like the sticky worker (`principal + normalised target + credential fingerprint`): they answer "whose operation is this". The `configuration-build` *exclusion* is keyed by `normalised target + resource` only: Creatio's configuration build is server-wide, so putting the principal in that key lets two principals on one environment compile concurrently and corrupt each other's package compilation state. Keying exclusion by target alone is correct but means one stuck build denies the whole environment — which is precisely why the **30-minute reclaim ceiling is the maximum lock-hold time**, not an incidental detail.
- Move the shared `configuration-build` reservation to the parent, keyed by **normalised tenant + resource**. Today it is `McpToolExecutionLock._configurationBuildInFlight` (`:215`), in-process, held by `CompileCreatioTool.cs:66` and `InstallProcessBuilderTool.cs:167`. Its 30-minute reclaim ceiling and monotonic ownership tokens carry over unchanged — they were designed for the "holder may never release" case.
- Prototype behaviour to preserve: `compile-creatio` returned in-progress at 8 s and three `compile-status` polls answered `running` from the same worker in 0.00–0.02 s.
- Sticky lifetime bounded by credential validity with an explicit maximum (T-8) — a threat that per-call workers do not have, and the reason stickiness stays confined to these four families.

## Acceptance Criteria
- [ ] AC-00 — **Session-key normalisation lands first** (TC-U-706): a registered environment *name* and an explicit *URI* for one target resolve to **one** key, and the equivalence table is asserted **both directions** — equivalent pairs share a key, near-miss pairs do not — generated from the threat model's T-5 component table rather than from cases the implementer happened to think of. This AC is ordered **before** AC-03: moving the registries to the parent on a split key produces a `compile-status` that answers "no such operation" for a running compile, and that is not repairable after the fact.
- [ ] AC-01 — `compile-creatio` returns in-progress; subsequent `compile-status` polls reach the **same**
      worker (TC-E-701) **without acquiring an admission slot**, asserted with the sticky pool SATURATED.
      *(Amended 2026-08-18 — see the admission-slot blocker below. The clause matters: "reaches the same
      worker" is silent on admission, and an implementer who routes the poll through the ordinary spawn path
      satisfies the original wording and ships a deadlock. A poll on an idle host passes either way and
      proves nothing.)*
- [ ] AC-02 — Private completion signal reaps workers for the three families with no registry (TC-U-701).
- [ ] AC-03 — Parent-owned reservation excludes compile ↔ install-process-builder **across processes and across principals**, keyed by normalised tenant + resource with the 30-minute ceiling as its maximum hold (TC-U-702).
- [ ] AC-04 — Sticky lifetime bounded by credential validity, explicit maximum (TC-U-703).
- [ ] AC-05 — **OQ-4 resolved**: whether `create-app-section` gains a real registry or only the private signal.
- [ ] AC-06 — **The per-call floor holds** (added 2026-08-18): with the sticky pool fully saturated, an
      ordinary per-call cohort read still completes. This is the property the separate pools exist to
      provide and nothing asserts it today; without it, two pools whose caps merely sum to the processor
      count relabel the exhaustion instead of removing it.
- [ ] AC-07 — **G-1 closed** (added 2026-08-18): the concurrency cap is operator-configurable, following the
      existing `CLIO_MCP_WORKER_QUEUE_WAIT_SECONDS` precedent — parse with fallback, documented accepted
      range, sane behaviour for null / empty / non-numeric / out-of-range. The threat model names this gap
      under T-9 and says it becomes the thing an operator needs to change **precisely at Stage 7**, because
      sticky workers make a slot held for a whole lifetime the ordinary case rather than the exception.
      Saturating the sticky pool must refuse the next long operation immediately, by name, carrying the
      limit — never a 60-second queue followed by a generic refusal.

## Tests
E2E TC-E-701; unit TC-U-701…703 and **TC-U-706** (session-key normalisation, AC-00). **Full unit suite required.**

## BLOCKER, identified 2026-08-18: admission slots deadlock sticky supervision

**This is in neither the ADR nor the kill-safety inventory, and it defeats AC-01 as written.** It
must be resolved in the design before implementation starts, not discovered during it.

### The cycle

`WorkerProcessSupervisor.SpawnContainedAsync` takes a slot from `_perCallPool` **before** launching,
and the slot is returned only when the lease is disposed:

```csharp
WorkerSlotPool pool = _perCallPool;
await pool.AcquireAsync(QueueWaitBound, cancellationToken).ConfigureAwait(false);
```

`ConcurrencyCap = Math.Max(1, Environment.ProcessorCount)` and `QueueWaitBound` is 60 s, after which
the caller is refused with `WorkerQueueWaitExpiredException`.

A **sticky** worker's lease outlives the call that created it — that is the whole point of this story.
So a sticky worker holds an admission slot for the entire compile, which is minutes to an hour.

AC-01 requires that `compile-status` polls **reach the same worker**. Today there is exactly one path
to a worker, and it begins by acquiring a slot. So:

1. Four sticky compiles on the measured four-core Windows stand hold all four slots.
2. A `compile-status` poll asks for a slot.
3. The only holders are the sticky workers — including the very one the poll is trying to reach.
4. The poll waits 60 s and is refused.
5. The slot frees only when the compile finishes, which the caller can no longer observe.

**That is hold-and-wait: the resource the poll needs is held by the thing the poll is trying to talk
to.** It is not starvation that resolves under load — the state is stable until the operation ends on
its own, and the configuration-build reservation's 30-minute reclaim ceiling is how long an
environment can stay in it.

The degenerate case removes any doubt: on a single-core host the cap is 1, so **one** sticky worker
makes every subsequent call — including its own status poll — unreachable.

### Why it was missed

The supervisor's own remarks come close and stop short. `DefaultQueueWaitBound` already says the cap
is "a shared, HELD resource … any worker that lives longer than the answer it produced occupies
capacity for its whole life", and `WorkerSlotPool` already anticipates "a second one with its own cap,
for workers whose lifetime outlives a single answer". But both frame it as **fairness** — sticky work
should not queue behind ordinary work. Neither states that the sticky worker's own status poll is one
of the callers it blocks, which is the part that turns a fairness problem into a deadlock. The ADR
models the cap against per-call workers, where a slot is never held longer than one answer, so the
cycle cannot arise there; the kill-safety inventory is about budgets and durable damage and does not
model admission capacity at all.

### Resolution — three parts, and the first is the one that removes the cycle

1. **Reaching an EXISTING sticky worker must not acquire an admission slot.** Admission governs
   *creating* a worker; it must not govern *talking to* one that already exists, because that worker
   is itself holding the slot the caller would wait for. A `compile-status` poll that resolves to a
   live sticky lease reuses that lease and takes no slot. This alone breaks the cycle, and it is also
   what the prototype's measured 0.00–0.02 s poll latency implies — a poll that queued for admission
   could not be that fast. **AC-01 should say so explicitly**, because "reaches the same worker" is
   silent on admission and an implementer who routes the poll through the ordinary spawn path
   satisfies the letter of it and ships the deadlock.

2. **Sticky workers draw from their own pool with its own cap** — the second pool the supervisor
   already sketches, so the release path does not change. Without this, sticky lifetimes still consume
   the capacity ordinary reads need.

3. **The sticky cap must be strictly less than the total, leaving the per-call pool a guaranteed
   floor.** Two separate pools whose caps sum to the processor count merely relabels the exhaustion;
   what ordinary reads need is capacity that sticky work can never take. The sticky cap then also
   *becomes* the stated maximum number of concurrent long operations one host supports, which is a
   product decision worth making deliberately: refusing the (N+1)-th compile immediately, naming the
   limit, is a far better answer than a 60-second queue followed by a generic refusal.

### Consequence for the acceptance criteria

- AC-01 gains: the poll must reach the worker **without acquiring an admission slot**, asserted with
  the sticky pool saturated — a test that polls while every sticky slot is held is the one that
  distinguishes a correct implementation from the deadlocking one. A poll on an idle host passes
  either way and proves nothing.
- A new criterion is needed for part 3: with the sticky pool fully saturated, an ordinary per-call
  cohort read still completes. That is the property the separate pools exist to provide, and nothing
  currently asserts it.


## AC-00 DELIVERED 2026-08-18 — session-key normalisation

`ISessionTargetNormalizer` / `SessionTargetNormalizer`
(`clio/Command/McpServer/Tools/SessionTargetNormalizer.cs`), a pure target→target fold producing
`scheme://host[:port][path]` and throwing `EnvironmentResolutionException` on T-5's four rejections.
Composed at `ToolCommandResolver.BuildTargetIdentity` rather than inside the normaliser, so R-5's other
two components — principal and credential fingerprint — are added AT THAT CALL SITE when `mcp-http`
returns, leaving the normaliser untouched. `BuildCacheKey` became an instance method with no static
overload left behind: a static that skips normalisation is a live trap, green in a test and divergent
in production.

**Red before green, on the real path and against unmodified production code:**

```
by-name: convergence-env|https://convergence.creatio.com:3541BD10…B712
by-uri:  default|https://convergence.creatio.com:3541BD10…B712
```

The credential hash is byte-identical on both sides, which is what proves the test was failing on the
split identity rather than on a mismatched credential arrangement. 3 failed / 1 passed before, 4 passed
after. The fixture covers BOTH derivations — `GetTenantKey`, which the per-tenant lock and the operation
registries key off, and the key the session container is cached under.

**Two platform traps found by probing rather than by reading docs, and both would have silently
defeated T-5:**

| Probe | Result | Consequence |
|---|---|---|
| `new Uri("http://0177.0.0.1/").Host` | `127.0.0.1` | `System.Uri` performs the exact octal fold T-5 REJECTS. Reading the host through it would accept an octal literal as canonical and merge two targets. `IPAddress.Parse` does the same. Hence a manual authority split |
| `IdnMapping{UseStd3AsciiRules=true}.GetAscii("a_b.com")` | throws | underscore hosts are ordinary on dev stands, so IDNA runs only for non-ASCII hosts and ASCII takes a lowercase fast path |

**Coverage:** 59 cases — 23 equivalence, 18 near-miss, 11 rejection, 7 pinned — each labelled with the
T-5 row it derives from, covering every row in both directions. IDNA, IPv6 and the combined fold are
pinned to explicit expected strings rather than round-tripped, so a per-OS formatting difference fails
instead of silently agreeing with itself. One test asserts a rejection message never echoes the
userinfo password (T-6).

`Category=Unit&Module=McpServer` 3735 passed / 0 failed. Full suite 9262 passed / 20 failed, and
`Module!=Common` is 8256 passed / 0 failed — which is the discriminator showing none of the 20 is from
this work.

### Consequence handled: a security-relevant comment went stale the moment this landed

`RestartTool.cs` argued in two places that a credentials-started restart and a name-started
`restart-status` lookup live in **structurally disjoint** key spaces "because `BuildCacheKey` uses
`Environment ?? Uri`". Convergence removes that guarantee: the two now agree whenever the target AND the
credentials agree. The behaviour — refusing to advertise a poll target on the credentials path — is
deliberately unchanged and still correct, but its justification is no longer "can never match"; it is
"from here we cannot tell whether these credentials belong to a registered environment, so any poll
target we named would be a guess". Both comments corrected in place; `RestartToolTests` 15/15 still
green, since they assert message content and the messages did not move.

### Residual, examined and deliberately left

`GetTenantKey`'s `unresolved:{Environment ?? Uri}` fallback is the same split-key shape on the failure
path, and it now also receives rejected targets — including one carrying userinfo. Left alone with
evidence rather than by omission: every `tenantKey` consumer was grepped (`BaseTool`,
`McpToolExecutionLock`, `McpPassthroughRedaction`, and the tools that thread it) and it reaches no
logger, no error envelope and no tool result — it is a dictionary key and a prefix check. Fail-closed
still holds end to end: `GetTenantKey` keeps its never-throws contract, `Resolve` throws the explicit
rejection, `BaseTool` maps it to exit code 1. Pre-existing for every resolution failure, so fixing it
here would be scope creep.


## Admission capacity DELIVERED 2026-08-18 — the blocker's three parts, plus G-1

**Part 1, the seam that removes the cycle.** `IWorkerChannel` (the talking surface: process id, the
three streams, `HasExited`, `ExitCode`, `WaitForExitAsync`) is split out of `IWorkerLease` (talk **and**
own), and an existing worker is reached only through `IWorkerReach`, whose single member is
`IWorkerChannel ReachExisting(IWorkerLease)`. **The guarantee is the dependency, not the
documentation:** a component injected with `IWorkerReach` has no method that can acquire a slot, so
routing a poll through admission stops being a mistake someone might make and becomes code that does not
compile. The returned channel is a wrapper implementing neither `IWorkerLease` nor `IDisposable` — not
the lease under a narrower static type, because a static type is a suggestion and one stray `using` on
the poll path would otherwise kill the very operation being observed. Reaching an exited worker does not
throw; `HasExited` is how a caller learns.

**Parts 2 and 3 — corrected during review from a partition to a CEILING.** The first implementation
carved the total into two pools, `sticky = total / 2` and per-call the remainder. It satisfied the
letter of the blocker and cost something real for nothing: on a four-core host ordinary reads dropped
from four concurrent to two **on the day it shipped, with zero sticky workers in existence**, and on a
two-core build agent to one — serialising the end-to-end suite this branch needs green.

The shipped model is **one pool of `ConcurrencyCap` slots plus a ceiling on how many of them sticky work
may hold**. While no sticky worker exists, per-call may use every slot; the floor appears the moment
sticky work does, because sticky can never occupy more than `StickyConcurrencyCap = total / 2`, leaving
`total - sticky` reachable by per-call work. AC-06 holds either way, and the interim cost is gone. A
counter-plus-semaphore replaces the second pool, reserved before the slot and released with it on every
failure path — a counter that drifts out of step either leaks capacity or refuses forever, and neither
shows up in a green suite.

Integer division keeps sticky strictly less than the total for every input and keeps the per-call
remainder the larger share, so the side answering ordinary reads is never the smaller one. Additive
capacity was rejected: an extra pool on top of the total would let the host exceed the measured ceiling
in ADR §2.4 and would falsify `ConcurrencyCap`'s published meaning.

**G-1 closed.** `CLIO_MCP_WORKER_CONCURRENCY` configures the **total** only, following the
`CLIO_MCP_WORKER_QUEUE_WAIT_SECONDS` precedent exactly — pure static resolver, invariant parse,
`0 < n <= 64`, fallback `Math.Max(1, ProcessorCount)`. Sticky stays derived: two independent knobs would
let an operator set sticky >= total and reintroduce precisely the exhaustion the ceiling removes, and
would mean clamping a relationship rather than a range. Excluded from the child-inherit allowlist for
the same reason the other supervisor variables are — a worker spawns no workers.

### Red-before evidence

Every behaviour was watched failing before it passed. Six mutations, each caught by exactly the intended
test:

| Mutation | Observed |
|---|---|
| `ReachExisting` acquires a slot first | `WorkerQueueWaitExpiredException` after **2.002 s**, cap 1, depth 1 — the hold-and-wait cycle reproduced verbatim: the poll waited out the bound for a slot held by the worker it was reaching |
| sticky spawns routed to the per-call path | ordinary read refused after 2.002 s |
| `sticky = total` | three tests, including the per-call read refused at an effective cap of 0 — independent proof of "relabelled exhaustion" |
| sticky queues instead of refusing | 2.002 s wait instead of the immediate named refusal |
| drop the `<= 64` clamp | override accepted 65 |
| `ReachExisting` returns the lease itself | "Expected channel to not be assignable to System.IDisposable" |

And for the ceiling correction specifically, reverting the shared pool to the partition turns **four**
tests red, including the new `…ShouldGivePerCallWorkTheWholeCap_WhenNoStickyWorkerExists`.

`Category=Unit&Module!=Common` → 8262 passed / 0 failed, which is the discriminator showing none of the
20 macOS baseline failures belongs to this work.

### Stated limitation, not a fallback invented on the spot

On a host where the total is 1, `StickyConcurrencyCap` is 0 and long operations are refused with a named
error carrying the limit and naming the knob. A `max(1, total / 2)` fallback was considered and
rejected: it would make sticky equal to the total on that host, leaving per-call work a floor of zero
and breaking AC-06 exactly where the guarantee matters most. An honest, actionable refusal — the
operator sets `CLIO_MCP_WORKER_CONCURRENCY=2` — beats a guessed fallback on a degenerate host that
nobody here can measure. If evidence later argues for the fallback, it is one line.

### What the next phase must wire (not done here, deliberately)

- `WorkerSpawnRequest.Lifetime = WorkerLifetime.Sticky` on the long-operation spawn; `PerCall` stays the
  default everywhere else, because a caller that does not say it is starting a long operation is not one.
- The poll path is injected with **`IWorkerReach`**, never the full supervisor — that injection is what
  makes the deadlock unrepresentable, and taking the supervisor there re-opens it.
- `IWorkerReach` must be registered as a **forwarder to the same singleton**. A second
  `WorkerProcessSupervisor` instance makes every `ReachExisting` throw "not issued by this supervisor"
  and gives each instance its own cap. CLIO005 flags a dead registration if it lands before a consumer
  exists, so the registration and the injection go in one change.
- Neither dispatcher catches `WorkerQueueWaitExpiredException` today, so `WorkerStickyCapacityExceededException`
  will propagate the same way; the next phase may want to map it to a tool-error envelope. Its message
  already names the knob.
