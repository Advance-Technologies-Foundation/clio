# Shared contract — what the two lanes agree on

Discussion #1643. Published early so @vladimir-nikonov can build against a settled primitive rather than
one still being argued over. Limited to what the current cases require; nothing here is a scheduler, a
storage framework or a general-purpose API.

Each clause says whether it is **settled** (measured, and I do not expect it to move) or **open**
(stated but not yet measured). Build against the settled ones. Task 3 has landed, so clause 5 is now
settled in full and clause 5b is new.

## 1. Runtime identity and compatibility — settled

A release is identified by its version string alone. Nothing about a runtime crosses the boundary as a
type: `OperationRecord.RuntimeVersion` is a `string` precisely so a record can outlive the release that
produced it.

Compatibility is the host's decision, made before activation and never mid-operation. An operation
selects its runtime when it is admitted and keeps it until it terminates — measured by A1a, where an
operation started on `10.0.0.0` kept answering as `10.0.0.0` after `10.1.0.0` was activated under it.

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

## 6. Configuration snapshot identity — hook only, policy is @vladimir-nikonov's

The lifetime side needs exactly one thing: whatever identifies a configuration snapshot must be portable
data under clause 3, so a record naming it survives the release and the process. It is a string as far as
this contract is concerned.

Everything else — preparation, rejection, migration, rollback, retention, concurrent edits — is the
settings lane's, and this contract deliberately takes no position on it.

## What this contract does not cover

Package authenticity and trusted acquisition. It is an explicit open release gate; nothing here bears on
it and none of these cases should be read as progress toward it.
