# Shared contract — what the two lanes agree on

Discussion #1643. Published early so @vladimir-nikonov can build against a settled primitive rather than
one still being argued over. Limited to what the current cases require; nothing here is a scheduler, a
storage framework or a general-purpose API.

Each clause says whether it is **settled** (measured, and I do not expect it to move) or **provisional**
(may still move as my remaining admission/evidence work lands). Build against the settled ones.

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

## 3. Portable outcomes — settled, with one open extension

Anything crossing back to a caller must be host-owned portable data. A runtime-defined type retains its
release for as long as any caller holds an instance — measured by O1, where the release stayed alive
until the escaped value was dropped, despite the operation having terminated and the ledger having
released its own retention.

`OperationRecord` is the shape to follow: identifiers, timestamps, an enum, and an **opaque** `Code` the
host never interprets.

**Open:** the same rule must hold for runtime-defined exception types, progress payloads and callbacks.
The mechanism is identical and the rule is stated, but only returned values are measured. That is my
task 2 and it may add clauses here — it will not remove this one.

## 4. Ownership release — settled

Three events, in order, and they are distinct:

1. **Response completion** — the caller is answered. Implies nothing about the work.
2. **Outcome publication** — `Complete(state, code)`. The result is known and visible.
3. **Ownership release** — disposing the lease. Owned cleanup may legitimately run between 2 and 3.

Quiescence follows **ownership**, not the outcome — measured by P5, where an operation published
`Succeeded` and retirement stayed refused until the lease was disposed.

A release may be retired only when no lease retains it **and** nothing it defined has escaped (clause 3).

## 5. Admission reservation — settled as a mechanism, provisional as a policy

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

**Provisional — the policy, not the mechanism.** A reservation refuses legitimate work for the whole
drain, so an unbounded drain is worse than a deferred update: one stuck update becomes every subsequent
call failing. The caller owns the budget and **must release the reservation when it expires**, deferring
the update again rather than escalating. Reservation timeout and reopening behaviour are my task 3 and
may add clauses here.

## 6. Configuration snapshot identity — hook only, policy is @vladimir-nikonov's

The lifetime side needs exactly one thing: whatever identifies a configuration snapshot must be portable
data under clause 3, so a record naming it survives the release and the process. It is a string as far as
this contract is concerned.

Everything else — preparation, rejection, migration, rollback, retention, concurrent edits — is the
settings lane's, and this contract deliberately takes no position on it.

## What this contract does not cover

Package authenticity and trusted acquisition. It is an explicit open release gate; nothing here bears on
it and none of these cases should be read as progress toward it.
