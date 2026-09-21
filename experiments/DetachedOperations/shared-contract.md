# Shared contract — what the two lanes agree on

Discussion #1643. Published early so @vladimir-nikonov can build against a settled primitive rather than
one still being argued over. Limited to what the current cases require; nothing here is a scheduler, a
storage framework or a general-purpose API.

Each clause says whether it is **settled** (measured, and I do not expect it to move) or **open**
(stated but not yet measured). Build against the settled ones. All three tasks have landed; clause 1 is
now measured rather than asserted, and clause 7 is new.

## 1. Runtime identity and compatibility — settled

A release is identified by its version string alone. Nothing about a runtime crosses the boundary as a
type: `OperationRecord.RuntimeVersion` is a `string` precisely so a record can outlive the release that
produced it.

Compatibility is the host's decision, made before activation and never mid-operation. An operation
selects its runtime when it is admitted and keeps it until it terminates — measured by A1a, where an
operation started on `10.0.0.0` kept answering as `10.0.0.0` after `10.1.0.0` was activated under it, and
by F1, where two operations ran at once against different releases and each release wrote its own
signature.

**Rejection happens before activation and touches nothing running** (F2). A release declaring a contract
generation the host does not support is loaded, inspected, discarded and its load context unloaded. Work
in flight across the rejection completed normally and work started afterwards succeeded — the rejected
release is never consulted, so a bad package is an unremarkable non-event rather than an incident.

## 2. Operation identity — settled

A host-issued opaque string, unique per operation, meaningful only to the host. The runtime never parses
it. The caller's only handle is this id; everything else about the operation is looked up through it.

An id is the unit of truth for *state*, never for *work* — holding an id does not entitle anyone to
resume, retry or cancel by replay.

## 3. Portable outcomes — settled, all forms measured

Anything crossing back to a caller must be host-owned portable data. A runtime-defined type retains its
release for as long as any caller holds an instance — measured by O1, where the release stayed alive
until the escaped value was dropped, despite the operation having terminated and the ledger having
released its own retention.

`OperationRecord` is the shape to follow: identifiers, timestamps, an enum, and an **opaque** `Code` the
host never interprets.

**No longer open — the rule is one rule, and every form is now measured.** A runtime-defined *exception*
retains its release exactly as a returned value does (G1: an escaping exception is a returned value with
extra steps). A runtime-defined *callback* does the same (G4). The portable form of the same failure —
an error code and message, both strings — lets the release go while the information survives (G2).

The mirror case matters too: a **host** delegate handed into a release must not be retained by it (G3),
or the host's object graph is tied to the release for as long as the release lives.

## 4. Ownership release — settled

Three events, in order, and they are distinct:

1. **Response completion** — the caller is answered. Implies nothing about the work.
2. **Outcome publication** — `Complete(state, code)`. The result is known and visible.
3. **Ownership release** — disposing the lease. Owned cleanup may legitimately run between 2 and 3.

Quiescence follows **ownership**, not the outcome — measured by P5, where an operation published
`Succeeded` and retirement stayed refused until the lease was disposed.

A release may be retired only when no lease retains it **and** nothing it defined has escaped (clause 3).

**When the owner is a process, disposal is the wrong event.** The three events above assume an
in-process owner that something will eventually dispose. A killed process disposes nothing, so the lease
stays open and the ledger keeps answering `Running` for work that has no process — measured on
@vladimir-nikonov's MCP host: eight polls over eight seconds for five seconds of work, `Running` every
time. A cross-process owner implements `IOwnerLiveness` and is **asked** instead, and an owner that is
gone resolves its operations to `Unknown` and stops retaining (H1, H2).

**What losing an owner does and does not establish** (@kirillkrylov's narrowing, and the code already
behaves this way):

- It establishes that **this local executor is gone**. It does not establish failure, and it does not
  roll anything back. Work that reached a Creatio environment may well have landed; `Unknown` says the
  host cannot establish the outcome, never that nothing happened, and it authorizes no replay.
- **A published terminal state is preserved.** An operation that already reported `Succeeded` is not
  downgraded because its worker later exited; only a record still `Running` is resolved.
- **Cleaning up a dead executor is not permission to discard evidence.** If persisting the resolution
  fails, the scope is marked degraded and the record joins `UnpersistedOperations`, exactly as any other
  failed evidence write does. Clause 5b still governs what happens to it.

Two consequences, both measured, and the second is the one that matters for a swap:

- **`Unknown` is an admission, not a verdict.** A genuine outcome arriving afterwards — the owner's last
  output still in a pipe buffer when it was declared gone — supersedes it, and both lines stay in the
  evidence. Disposal alone never supersedes it, because disposal is not knowledge; that would turn "I do
  not know" into a fabricated `Failed` (H5).
- **Without this, a drain after a lost owner never finishes.** The orphan retains forever, so the scope
  never reaches quiescence and the reservation can only expire. Mutation control with liveness disabled:
  H3's `afterLiveOwnerFinished` is `false`, permanently. Reserve-then-drain does not survive losing an
  owner unless the ledger can resolve one.

## 5. Admission reservation — settled, mechanism and policy

```csharp
IDisposable? TryEnterSwapWindow(string? target = null);   // requires quiescence, can starve
IDisposable? TryReserveAdmission(string? target = null);  // closes the scope, then you drain
```

**Use the second one for any wait.** The first requires quiescence *before* it grants anything, so
polling it under continuous load never succeeds — measured by D1, starved for the full observation on
both platforms. `TryReserveAdmission` closes the scope to new work without requiring it to be idle, so
the in-flight set is finite and drains (D2, ~130 ms under the load that starved D1).

While a scope is held, `Begin` for it throws `SwapWindowHeldException` — **refused, not queued**, so a
caller sees a retryable refusal rather than a hidden stall. Exclusion is bidirectional: a global hold
excludes every target hold and vice versa.

**Now settled, including the policy.** A reservation refuses legitimate work for the whole drain, so an
unbounded drain is worse than a deferred update. The caller owns the budget and **must release the
reservation when it expires**, deferring the update again rather than escalating.

E1/E2 measure the case that forced this: a **hung** operation never terminates, so even a closed scope
never drains. The reservation expired after its budget, the scope reopened, new work was admitted, and
the hung operation was neither killed nor completed. A finite in-flight set does not imply a terminating
one, and the budget is the only thing standing between "the update defers" and "the scope is shut
forever".

## 5b. Evidence repair versus explicit loss — settled

A degraded scope means one or more outcomes exist **only in memory**. Three facts, measured:

- **Replacing the evidence owner loses them** (E3). Clearing a flag never changed that — the record was
  never on disk.
- **Repair is the only thing that makes them durable** (E4): re-persisting the original record, once,
  with its original values. It is not a replay; nothing is re-executed. Removing the storage fault alone
  changes nothing until the repair runs.
- **When repair cannot succeed, explicit loss is the honest alternative** (E6). The outcomes are
  abandoned deliberately and later readers are told `Unknown` rather than given a guess.

`UnpersistedOperations` is the list that matters; `DegradedScopes` is derived from it. A scope's mark
lifts when its list empties — by repair or by explicit loss, never by the fault merely going away.

## 6. Configuration snapshot identity — seam settled, policy is @vladimir-nikonov's

The lifetime side needs exactly one thing: whatever identifies a configuration snapshot must be portable
data under clause 3, so a record naming it survives the release and the process. It is a string as far as
this contract is concerned.

Everything else — preparation, rejection, migration, rollback, retention, concurrent edits — is the
settings lane's, and this contract deliberately takes no position on it.

**The seam is now built and measured**, so the settings lane has something concrete to attach to rather
than a promise. `OperationRecord.ConfigurationSnapshot` is a `string?`, captured at admission and never
re-read:

- An operation keeps the snapshot it was admitted under when another is activated beneath it (J1) — the
  same rule as the runtime release, for the same reason.
- The identity survives into evidence and back out, including on a record whose outcome this host cannot
  establish (J2). An `Unknown` that cannot say which configuration produced it is much less useful.
- A storage failure that loses the outcome does not also lose the configuration, because the admission
  line already carries it (J4).
- Nothing new crosses the boundary: the seam is a `string` and the contract assembly still references
  `System.Runtime` and `System.Collections` only (J3).

**One trap, found by breaking it rather than by thinking about it.** The seam was first added as an
optional parameter on the existing `Begin`. That is source compatible and **binary incompatible**: every
release already built against the previous contract died at its first operation with
`MissingMethodException: Method not found: IOperationLedger.Begin(String, String, Object)`. Keeping
already-shipped releases working is the entire point of this contract, so the old signature stays and the
snapshot form is a separate overload (J5, measured against binaries this run never recompiles).

Adding to this contract is therefore an **addition**, never a modification — including the kinds of
change a compiler accepts silently.

## 7. What may cross the boundary — settled

The assembly that both sides compile against references `System.Runtime` and `System.Collections`, and
nothing else (F4). It carries no CLI type, no MCP type and no vendor release type. That is the property
that makes the rest of this document implementable: a record can outlive its producer only because
nothing in it belongs to the producer.

A third party builds against this assembly alone. F3 loads a partner workflow from its own assembly — it
references the contract and no vendor release — hands it the pinned runtime, and gets back three strings.
The partner composed vendor capability without linking to a vendor version, which is what lets the vendor
ship a new release without rebuilding anyone.

This clause is **measured, not enforced**: F4 reads `GetReferencedAssemblies()` on the built contract
assembly, which is a snapshot of today's build, not a gate that would stop tomorrow's addition from
pulling a dependency in. Making it a gate is a build-side change and is not done.

The practical rule for anything added here later: if a type would be meaningless after its defining
assembly is gone, it does not belong in this contract. Clause 3 is the same rule seen from the caller's
side.

## 8. How a case earns the right to be cited — settled by repetition

A case title is a claim. The assertions are not the claim; they are evidence for it, and they can be
satisfied by a run that does not exhibit the named behaviour at all. This has now happened three times,
in two harnesses that share no code:

| Case | Title claimed | What also satisfied every assertion |
|---|---|---|
| D1 | polling under continuous load starves | the poll ran before the load existed and won on an empty scope |
| S10 (@vladimir-nikonov) | the old path never grants a window under admission pressure | a load pattern that left gaps in ownership |
| F1 | two releases execute concurrently | a strictly sequential run |

None of the three was found by reading the code. All three were found by constructing the run that
should fail and discovering it passed.

So the rule, for both harnesses: **a case whose title names a temporal or contention property — first,
concurrently, under load, never, while — carries a mutation control, and the mutation is reported
alongside the result.** If no mutation can be constructed that fails the case, the case is not measuring
its title. The cost is one extra run; the thing it buys is that a cited number means what its name says.

**And the mutation must be observed to FAIL, not merely applied.** The first attempt at F1's successor
mutation was `if (true) return`, which tripped CS0162, failed the build, and left `--no-build` running
the previous binary — which reported a confident full pass. A mutation that silently did not run looks
exactly like a mutation the code survived. Report the mutated run's failing case names, not the fact
that a mutation was made.

## What this contract does not cover

Package authenticity and trusted acquisition. It is an explicit open release gate; nothing here bears on
it and none of these cases should be read as progress toward it.
