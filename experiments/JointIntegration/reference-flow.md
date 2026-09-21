# Reference flow: one selection, one admission, one failure story

Discussion #1643. This is the integrated narrative for the part I own — the runtime/configuration
selection and the admission boundary — written so the design can be understood without reading the
thread. Evidence is at the end, not throughout.

**Tested at `4f6c04e62fcf`: 12/12 on macOS, 12/12 on Windows from a clean clone.** The settings half is
`nikonov/supervisor-quiescence-probe@f70f454d4365`, linked rather than copied. Neither half is
reimplemented here.

---

## 1. The flow

A partner workflow is running against release `10.0.0.0` under configuration `cfg-1`. A new release
`10.1.0.0` and a new configuration `cfg-2` are prepared. Four things must hold and each is a separate
mechanism:

1. **Nothing already admitted moves.** An operation keeps the release and the configuration it was
   admitted under until it terminates. Both are captured once, at admission, and never re-read.
2. **A refused half changes nothing.** If the release is rejected — it declares a contract generation the
   host does not serve — or if the settings commit fails, the pair is simply never published. There is no
   rollback step, so there is no rollback step that can itself fail.
3. **A successful activation publishes both halves as one unit.** New admissions then take both from one
   read of that unit. They cannot observe the new release with the old configuration.
4. **Work that has finished stays finished, and work whose owner is gone says so.** The outcome and the
   health of the evidence about it are different axes, and neither is allowed to rewrite the other.

## 2. Who owns what, and until when

| State | Owner | Ends when |
|---|---|---|
| Operation records and durable evidence | ledger | never — a persisted record outlives the process that wrote it |
| Un-persisted outcomes (`UnpersistedOperations`) | ledger, in memory only | repaired, explicitly abandoned, or **lost** with the memory that held them |
| Retention, and whether an owner still exists | ledger | lease disposal, or the owner ceasing to exist |
| The committed `(runtime, configuration)` selection | ledger | replaced by the next successful publication |
| Snapshots, candidates, revisions, rollback retention | `SettingsStore` | explicit activate, abandon, or cleanup |
| Loading, unloading and refusing a release | host | retirement, which retention gates |
| Which pair to publish, and when | activation coordinator | the publication |

**The line that keeps a second source of truth from appearing:** the settings store owns snapshots; it
does not own what an operation is admitted under. That belongs to the selection, and the selection
belongs to the ledger.

**Where the ledger lives decides what a swap costs.** The contract is about the ledger's lifetime, not
about any particular process boundary, which is what lets a supervised host and a statically embedded
library share it:

| Deployment | Ledger lives in | Cost of replacing the backend |
|---|---|---|
| Supervised host | the supervisor, which outlives the swap | nothing — the ledger is not in the process being replaced |
| Reusable library / embedding | the embedding application | nothing is being swapped; the boundary is that application's own lifetime |
| Replacing the evidence owner itself | wherever the ledger is | persisted records survive; `UnpersistedOperations` do not |

## 3. The guarantees, stated together

- **Admission takes the pair from one read.** `BeginFromSelection` reads the selection and registers
  ownership in one critical section. A caller that reads the two halves separately can admit a pair that
  was never current — measured, not assumed.
- **Publication is compare-and-swap on a generation.** Two publishers cannot both believe they updated
  the same pair, and a failure means no publication happened at all.
- **Any activation of either half republishes the pair**, so the selected and the pinned snapshot are
  equal outside the commit window. Inside that window they are not, which is why the next point exists.
- **A snapshot named by a committed selection is retained.** `SelectedSnapshots` is the fourth ownership
  reason for cleanup, next to pinned, held and retained. Without it a cleanup landing in the commit
  window reclaims a snapshot an admission is about to use.
- **Ownership ends at disposal, or at the owner ceasing to exist.** A cross-process owner is asked
  (`IOwnerLiveness`) rather than trusted to dispose itself. An owner that cannot be asked is *reported*,
  never assumed dead.
- **Nothing here can replay anything.** The ledger stores that an operation happened, never how. That is
  a deliberate limit: a durable record does not make a retry safe, and this design does not pretend it
  does.

## 4. The failure story, as one narrative

Work succeeds. The evidence write fails.

The outcome is published as `Succeeded` with its own code, because successful work must not become failed
or retryable work merely because a disk write failed. The scope is marked degraded, the record joins
`UnpersistedOperations`, and automatic retirement of the release is refused — retiring it would destroy
the only place that outcome still exists. The record still names the configuration it ran under, which is
exactly when that is most useful.

Two honest endings, and no third:

- **Repair** re-persists the original record, once, with its original values. Nothing is re-executed.
- **Explicit loss** abandons it deliberately, and later readers are told `Unknown` — not `NotFound`,
  which would say nothing was ever started.

A degraded mark lifts only through one of those. It never lifts because the fault went away.

Separately: if the *owner* is lost — a killed backend — its operations resolve to `Unknown` and stop
retaining. That establishes the local executor is gone. It does not establish failure and does not undo
anything the work already did in a Creatio environment. A record that already published a terminal state
is never downgraded.

## 5. How a consumer reaches this flow

- **Through MCP**, as the long-lived host: the client holds one connection, admissions go through the
  selection, and an operation id issued before a backend swap stays valid and truthful afterwards.
- **Through embedding**, as a library: the same ledger, the same contract assembly, no supervisor. An
  embedding application must not acquire a hosting model in order to use the libraries — the contract
  assembly references `System.Runtime` and `System.Collections` and nothing else, so it does not drag one
  in.

A partner builds against that contract assembly alone and composes whichever release is pinned.

## 6. What this does not establish

- **A hung operation.** The drain budget expires, the operation is still `Running`, neither killed nor
  completed. Liveness does not touch it: the owner is alive and simply not finishing. A safe defer policy
  is not invalidated by it — safety holds while progress is not guaranteed — but the visible reason and
  the explicit cancellation action are undesigned. No automatic termination, and no replay to force
  progress.
- **Supervisor self-update.** Requires a restart, by design.
- **Package authenticity and trusted acquisition.** A named release requirement. Nothing here bears on
  it, and none of these cases should be read as progress toward it.

## 7. Evidence

`macos-results.json`, `windows-results.json`, both at `4f6c04e62fcf`.

| | |
|---|---|
| X1 | a partner keeps its pinned release and snapshot while a new pair arrives |
| X2 | a failed evidence write degrades the scope without touching the outcome or losing the snapshot |
| X3 | a cross-process owner registered without liveness holds its snapshot, and cleanup reports it |
| X4 | negative control — with a liveness-capable owner the snapshot is reclaimed |
| X5a | a refused runtime leaves the committed pair untouched |
| X5b | a settings commit failure after the runtime half leaves the previous pair usable |
| X5c | an admission held at the boundary observes one whole pair |
| X5d | mutation control — separate reads admit a pair that was never current |
| X6 | a settings-only activation through the coordinator lands admission on a live snapshot |
| X7 | mutation control — skipping the republication admits under a reclaimed snapshot |
| X8 | a cleanup inside the commit window reclaims the snapshot the selection still names |
| X9 | the ledger already holds the answer cleanup needs |

| X10 | mutation control — ignoring the selected-snapshot reason reopens the window |

X7 and X8 were reproducible until the selected-snapshot ownership reason landed; they are kept as the
acceptance cases for it rather than deleted. X10 is what keeps them honest: with that reason switched
off through the store's own flag, the window reopens exactly as it was measured. Observed failing the
guarantee, not merely applied.

X3 and X4 only tell you about owner liveness if the snapshot under test is not *also* retained for being
selected, so both move the selection off it first. Before that correction they would soon have passed for
a reason that has nothing to do with liveness.
