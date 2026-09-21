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
| I2b | **Publishing an outcome and relinquishing ownership are different events.** `Complete` does the first; disposing the lease does the second. Quiescence follows ownership, not the outcome. | P5 | a release retired while owned cleanup is still running |
| I2c | Admission persists its evidence **before** registering. A failed admission write starts no work and leaves no registered operation. | P4 | a scope blocked forever by an operation that never began |
| I3 | An operation's owning release is retained until the operation terminates. **Termination releases the ledger's retention; it does not make the release collectible.** | C2, R1, **O1** | a release retired out from under live work |
| I9 | **Anything** crossing back to a caller must be host-owned portable data — returned DTOs, thrown exception types, and delegates alike. A runtime-defined type retains its release for as long as any caller holds an instance of it. | O1 (measured for returned DTOs; exceptions and delegates are the same mechanism, **not separately measured**) | a release that can never be reclaimed because a value escaped |
| I4 | Terminal evidence is durable **before** the terminal state is observable. | A5g; write-then-publish inside the window lock | a replacement acting on a state whose record was never written |
| I5 | A host that cannot establish an outcome reports **uncertainty**, never absence. | A4a, A4b, and control C1 which produces today's false `NotFound` | "no compile was started" while one is running |
| I6 | Quiescence is **taken and held**, never merely observed. | control A5a shows the check-then-act gap; A5h/A5i show the barrier and that the test detects its absence | work admitted into a scope a swap already decided was idle |
| I7 | Scope exclusion is bidirectional: a global hold excludes every target hold and vice versa. | A5e, A5f | a target window granted under a global one |
| I8 | Reconciliation establishes observed **state**, never completion of a given invocation, and never authorises interruption. | N1, N2, N3 | "the artefact is there, so my operation finished" |

## Cancellation

A cancelled operation still owes its caller a terminal state (I2). It must not write its external effect
after cancellation is observed.

**Narrowed to what was measured.** A3b/A3c cancel during the operation's own delay, *before* its effect
is written, and assert the effect file is unchanged. That is cancellation preventing an effect that had
not happened yet. It says nothing about rolling back an effect already applied to a remote system, and
nothing about cancelling work already in flight on the server — neither is measured here and neither
should be read into the contract.

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

**The rule, chosen rather than inherited.** `Complete` publishes the outcome; disposing the lease ends
ownership. They are deliberately separate, because a runtime may legitimately know its result before its
owned cleanup has finished. Retirement waits on ownership, not on the outcome — P5 measures both halves:

```
P5  outcomePublished=Succeeded  refusedDuringCleanup=true
    idleAfterRelease=true       windowGrantedAfter=true
```

The outcome was `Succeeded` throughout. Only ownership changed, and only ownership moved the gate.

A release may be retired only when no lease retains it **and nothing it defined has escaped**. C2 is the
control for the first half: the release is not collectible while its work is in flight, and is
immediately after.

**My first version stopped there, and it was wrong.** @kirillkrylov found the expensive boundary:
`OperationResult.Payload` is `object`, partner workflows return their own DTOs, and
`UpdatingComposition` passes them back unchanged — so a caller holding such a value holds the release
that defined its type. Case **O1** measures it against my own invariant:

```
O1  aliveWhileResultHeld=true   collectedAfterResultDropped=true
    escapedType=Clio10.DetachedOperationsFixture.DetachedRuntime+ReleasePayload
```

The ledger had already reported a terminal state and released its own retention. The escaped value had
not. Reclamation therefore depends on something the lifetime contract does not control — which is why
I9 exists, and why "execution completed" is not a sufficient condition for retirement.

The consequence for the boundary: results that cross a replaceable-runtime edge have to be host-owned
portable data, the same rule `OperationRecord` already follows. Typed feature DTOs remain fine for static
embedding, where nothing is ever unloaded. That matches @kirillkrylov's proposal; O1 is the measurement
under it.

Retirement therefore follows **ownership**, not filesystem behaviour. Whether assemblies were loaded by
path or from bytes does not decide whether deleting a release is safe — established by @kirillkrylov's
retirement probe and reproduced on macOS, where path-mode early deletion succeeds and still breaks a
later lazy dependency.

## Persistence failure — proposed behaviour demonstrated, not approved

My first version listed three options and asked for a decision. @kirillkrylov supplied the one that
separates the axes correctly: **execution outcome and evidence health are different things, and
successful work must not become a failed or retryable business operation because storage failed.**
Implemented and measured:

```
P1  state=Succeeded                      (outcome unchanged by the storage failure)
P2  degradedScopes=[envP]  quiescent=true
    targetWindow=refused   globalWindow=refused
```

A failed terminal write now publishes the true outcome, marks the scope degraded, and refuses automatic
retirement. Nothing is replayed, nothing is rewritten, and clearing a degraded scope is deliberately not
automatic — it is an operator decision this contract does not make.

**This is demonstrated proposed behaviour, not an approved decision.** Clearing degradation and whether
forced interruption is permitted remain open for Kirill.

What the earlier version did instead, and why it was wrong: the state was never published, so a
*successful* operation stayed `Running` forever and its scope was silently stuck. That reported a
storage fault as a business outcome, which is exactly the conflation the fix removes.

### The consequence I did not anticipate: quiescence is not the retirement predicate

Look at P2 again. The scope is **quiescent** — nothing is running — and still refused.

**A correction I had wrong, caught by @kirillkrylov and now measured as P3.** I wrote that the refusal
followed from retiring the release destroying the only copy of the outcome. That is false. `_live` holds
`OperationRecord`s — host-contract types in a host-owned collection — so unloading the runtime does not
touch them:

```
P3  runtimeCollected=true   stateAfterRetirement=Succeeded   stillDegraded=true
```

The release that produced the operation was retired and collected; the record and the degraded flag
survived it. **Runtime retirement and replacement of the evidence owner are different operations**, and
only the second destroys in-memory evidence. P2 refuses both, which is defensible conservatism — but it
is *policy*, not a consequence of anything P2 measures, and the contract now says so.

That distinction matters beyond this case: it is the same axis the whole round turns on. Updating a
runtime does not endanger host-owned state; replacing the host does.

So there are two independent reasons to refuse a swap, and they are not the same shape:

| reason | visible to | ends when |
|---|---|---|
| live work | the ledger, via `IsQuiescent` | the operation terminates |
| degraded evidence (blocks **host replacement**; runtime retirement is safe, per P3) | the ledger, via `DegradedScopes` | an operator resolves it |
| an escaped runtime-defined value (I9, O1) | **nobody** | the caller drops its reference |

A gate built on quiescence alone admits a swap in the second and third cases. The retirement predicate
is quiescence **and** evidence health — and, per O1, even that is not sufficient while a runtime-defined
value can escape to a caller.

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

1. ~~Persistence failure.~~ **Resolved** — degraded scope, outcome preserved, retirement refused, no
   replay. What remains a decision is how a degraded scope is *cleared*, which is operator policy.
2. **Wait budget and starvation.** A5h shows admissions can be starved by an aggressive swapper. There is
   no fairness policy and no bound on how long a swap may wait for quiescence.
3. **Whether interruption is permitted at all.** Being able to report afterwards that an operation did
   not finish does not preserve it. This is interruption policy and it is Kirill's call, not a
   consequence of anything measured here.
4. **What "finished" includes.** Cleanup and postprocessing, or only the primary effect? The clio 8 drain
   asymmetry above is the concrete instance.
5. **Cross-process coordination.** Out of scope throughout; one Core instance is assumed.

## Limits

The injected persistence failure fires **before** `Append`, so partial-write and fsync-failure recovery
are outside the measured claim — a torn write is a different failure from a refused one.

Everything above is grounded in a probe with one owner at a time, two threads in the contention cases,
no native libraries, no real Creatio dependencies, and no multi-process coordination. The clio 8
observations are readings of shipped source and one live stand, not a survey of the product line.
