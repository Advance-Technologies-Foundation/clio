# Execution-lifetime contract

Discussion #1643, round assignment. A contract, not an implementation: invariants, named failure
outcomes, and the split between what Core owns and what a runtime decides. Every invariant cites the
case that grounds it, or is marked as a gap. Nothing here proposes a persistence framework.

## The distinction everything else rests on

**Response completion and execution completion are different events, with different observers.**

A response completes when the caller is answered. Execution completes when the work, its cleanup and its
required outcome record are all finished. Today these are conflated, and the conflation is what the
original defect in this thread was: `create-app-section` answered `section-created: in-progress` —
correctly, the work *was* continuing — and the process was replaced 1029 ms later. Four and a half
minutes on, neither the section nor its entity schema existed. The response was truthful; the execution
never completed; nothing connected the two.

Note what this is **not**: clio is not naive about it. `McpProgressHeartbeat.cs:331-334` already refuses
to emit "keep polling" when shutdown is known at the moment the deadline is evaluated, precisely because
the detached `Task.Run` would die with the process. What has no answer is a kill arriving *after* a
legitimate in-progress reply. Guidance cannot be un-sent; only a later reader can be corrected.

## Invariants

| # | Invariant | Grounded in | Failure it prevents |
|---|---|---|---|
| I1 | An answered response never implies a completed execution. The two are recorded separately. | the `create-app-section` A/B | a caller treating "accepted" as "done" |
| I2 | Every operation that outlives its response reaches **exactly one** terminal state. Silence is not a terminal state. | A3a, A3b; the lease records a terminal on dispose rather than leaving a record at `Running` | an operation polled forever |
| I3 | An operation's owning release is retained until the operation terminates, and only then becomes collectible. | C2, R1 | a release retired out from under live work |
| I4 | Terminal evidence is durable **before** the terminal state is observable. | A5g; write-then-publish inside the window lock | a replacement acting on a state whose record was never written |
| I5 | A host that cannot establish an outcome reports **uncertainty**, never absence. | A4a, A4b, and control C1 which produces today's false `NotFound` | "no compile was started" while one is running |
| I6 | Quiescence is **taken and held**, never merely observed. | control A5a shows the check-then-act gap; A5h/A5i show the barrier and that the test detects its absence | work admitted into a scope a swap already decided was idle |
| I7 | Scope exclusion is bidirectional: a global hold excludes every target hold and vice versa. | A5e, A5f | a target window granted under a global one |
| I8 | Reconciliation establishes observed **state**, never completion of a given invocation, and never authorises interruption. | N1, N2, N3 | "the artefact is there, so my operation finished" |

## Cancellation

A cancelled operation still owes its caller a terminal state (I2). It must not write its external effect
after cancellation is observed — measured by A3c, which asserts the effect file is unchanged after both
the failure and the cancellation paths.

Cancellation of the **caller** is not cancellation of the **operation**. A client that stopped waiting
has not decided that the work should stop; clio 8 already draws this line explicitly in
`StickyWorkerPoll`. The contract keeps it: the operation's lifetime belongs to its owner, not to whoever
happens to be listening.

## Detached work

Work that outlives its response is owned, not orphaned. Ownership means three things concretely: the
owning release is retained (I3), a terminal state will be recorded (I2), and the fact that it started is
durable before anything else happens (I4/I5).

**A measured asymmetry in shipped clio 8 that makes this concrete.**
`McpServerCommand.DrainHostBackgroundWork` awaits exactly two things at shutdown — the component-registry
refresh and the telemetry flush, ten seconds each. **Nothing drains the heartbeat-detached operations.**
So a drain-before-exit mechanism already exists, with a budget, and covers housekeeping while abandoning
the work that mutates a customer's environment. Any answer to "what counts as finished" has to explain
why those two lists differ.

## Cleanup and release

A release may be retired only when no lease retains it. That is I3, and C2 is its control: the release is
*not* collectible while its work is in flight, and is immediately after.

Retirement therefore follows **ownership**, not filesystem behaviour. Whether assemblies were loaded by
path or from bytes does not decide whether deleting a release is safe — established by @kirillkrylov's
retirement probe and reproduced on macOS, where path-mode early deletion succeeds and still breaks a
later lazy dependency.

## Persistence failure — **gap, not a rule**

If the terminal record cannot be written, what may the host claim?

The probe's current behaviour is a defect, and case **P1 measures it** rather than describing it —
evidence-write failure is injected, the work finishes normally, and the result is:

```
P1  state=Running  quiescent=false  windowObtainable=false
```
`Complete` writes evidence before publishing the state, inside the lock. If that write throws, the state
is never published, the owner is never released, and **the operation stays `Running` for the life of the
process** — retaining its release forever and blocking every future swap window for that scope. The
lease's second `Complete` cannot rescue it because `_reported` is set before the ledger call, so
exactly-once has already been consumed.

Note the shape: this is the cost of invariant I4. Writing evidence before publishing is what stops a
replacement acting on an unrecorded terminal — and it is also what turns a disk failure into a stuck
scope. The two are the same ordering decision seen from opposite sides.

The options are a decision, not a derivation:

- publish the terminal state anyway and accept that a later process may report `Unknown` for something
  that actually finished — truthful uncertainty, at the cost of losing a known outcome;
- keep the operation `Running` and surface the persistence failure as its own condition, so the scope is
  visibly blocked rather than silently stuck;
- treat a failed evidence write as fatal to the host.

Each trades a different thing. **This needs a human decision.**

## What Core owns versus what a runtime decides

| Core | Runtime |
|---|---|
| issuing identity; retaining the owner; recording exactly one terminal state | what the operation *means* |
| durability ordering of evidence | the content of the opaque outcome code |
| quiescence, admission, scope exclusion | whether a given class is worth waiting for |
| release lifetime and retirement eligibility | reconciliation policy per operation class |
| answering `Unknown` versus `NotFound` | judging retry safety |

The dividing line is that Core stores and orders; it never interprets. The record carries portable data
only — no delegates, no runtime objects — which is what lets it be written to evidence and outlive the
release that produced it.

## Gaps that need a decision rather than an implementation

1. **Persistence failure** (above). The current behaviour is a stuck scope.
2. **Wait budget and starvation.** A5h shows admissions can be starved by an aggressive swapper. There is
   no fairness policy and no bound on how long a swap may wait for quiescence.
3. **Whether interruption is permitted at all.** Being able to report afterwards that an operation did
   not finish does not preserve it. This is interruption policy and it is Kirill's call, not a
   consequence of anything measured here.
4. **What "finished" includes.** Cleanup and postprocessing, or only the primary effect? The clio 8 drain
   asymmetry above is the concrete instance.
5. **Cross-process coordination.** Out of scope throughout; one Core instance is assumed.

## Limits

Everything above is grounded in a probe with one owner at a time, two threads in the contention cases,
no native libraries, no real Creatio dependencies, and no multi-process coordination. The clio 8
observations are readings of shipped source and one live stand, not a survey of the product line.
