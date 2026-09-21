# Joint integration proof

The case @kirillkrylov assigned: **a partner workflow pinned to one release while another arrives with
changed settings, with an outcome-storage failure injected.** It needs both lanes, so neither lane could
have produced it alone.

Nothing here is reimplemented. `OperationLedger.cs` is the lifetime lane's
(`../DetachedOperations/Host/`), `SettingsStore.cs` is @vladimir-nikonov's
(`../SettingsVersioning/`, taken from `nikonov/supervisor-quiescence-probe@3c67789449a9`), and both are
linked into this project rather than copied.

```bash
dotnet build experiments/DetachedOperations/Contract/Contract.csproj -c Release
dotnet build experiments/DetachedOperations/RuntimeV1/DetachedRuntime.V1.csproj -c Release
dotnet build experiments/DetachedOperations/RuntimeV2/DetachedRuntime.V2.csproj -c Release
dotnet build experiments/DetachedOperations/Partner/Partner.csproj -c Release
dotnet build experiments/JointIntegration/JointIntegration.csproj -c Release
dotnet run --project experiments/JointIntegration/JointIntegration.csproj -c Release --no-build -- \
  artifacts/detached/10.0.0.0 artifacts/detached/10.1.0.0 artifacts/detached/partner /tmp/jointwork
```

| Case | What it establishes |
|---|---|
| X1 | a partner stays on its pinned release **and** its pinned snapshot while a new release and new settings arrive |
| X2 | a failed evidence write degrades the scope without touching the outcome or losing the snapshot |
| X3 | a cross-process owner registered without liveness holds its snapshot, and cleanup now reports it |
| X4 | **negative control** — with a liveness-capable owner the same snapshot IS reclaimed |
| X5a | a refused runtime leaves the committed pair untouched, and admissions still see it |
| X5b | a settings commit failure after the runtime half leaves the previous pair usable |
| X5c | an admission held at the boundary observes one whole pair, never a mixture |
| X5d | **mutation control** — reading the halves separately admits a pair that was never current |
| X6 | after a settings-only activation through the coordinator, admission lands on a live snapshot |
| X7 | **mutation control** — skipping the republication admits under a snapshot cleanup reclaimed |

**10/10 on macOS.** Exact build target: this branch, plus `experiments/SettingsVersioning` taken from
`nikonov/supervisor-quiescence-probe@f70f454d4365`. Both are in this tree, so the branch builds and runs
without mixing incompatible sources.

## Why the pair is published, not ordered

My first proposal was *prepare settings → activate runtime → activate settings*. @kirillkrylov's
correction: that is not sufficient on its own. An admission landing between the last two steps sees the
new runtime with the old settings, and a settings commit that fails after the runtime is already
activated leaves the same split.

So both halves become current in **one publication** — `IOperationLedger.TryPublishSelection` — and an
admission takes both from **one read** — `BeginFromSelection`. Publication and admission share one
critical section, so there is no halfway state to observe. A failure on either half simply means the
publication never happens, so the previous selection stays usable and there is no rollback step that
could itself fail.

X5d is the mutation control that keeps this honest. A caller reading the two halves separately, with a
publication between its reads, admits `(10.1.0.0, cfg-11)` — a pair that was never current together.
Deterministic, through the boundary hook, not raced for.

## X3, narrowed

An earlier version of this file concluded that a cleanup finding `OwnersWithoutLiveness` non-empty should
treat it as a leak. That was too broad, as @kirillkrylov pointed out: an ordinary in-process owner whose
lifetime ends at `Dispose` appears in that list the whole time it runs and is perfectly correct. Absence
of liveness is a **capability**, not evidence that anything is wrong.

What X3 does show is narrower and still worth having: a **cross-process** owner registered without its
wrapper holds its snapshot indefinitely, and before this round nothing said so. `Cleanup()` now reports
it as a diagnostic without changing what it reclaims, and X4 remains the positive process-loss case.

## The rule the removal of `SettingsStore.Admit` exposed

@vladimir-nikonov removed `Admit` at my request and named exactly what left with it: its lock-protected
read-then-register kept a two-step admission from straddling a concurrent `Cleanup`. That guarantee is
now `BeginFromSelection`'s, which holds it.

Checking that surfaced a larger gap. The published pair can go **stale**. A settings-only activation
moves the store's current snapshot; if the pair is not republished, the selection still names the
previous snapshot, which is then neither pinned nor held nor retained — so `Cleanup()` legitimately
reclaims it, and the next admission is registered under a snapshot that no longer exists:

```
X7 {"reclaimed": ["cfg-14"], "admittedUnder": "cfg-14",
    "snapshotStillExists": false, "pinnedInStore": "cfg-15"}
```

**The rule: any activation of either half republishes the pair.** Then selected and pinned are the same
snapshot by construction, and the question of whether cleanup should know about the selection never
arises. `ActivationCoordinator` is where that rule lives — one method, no state of its own, in the one
place that owns both halves.

The alternative was to make the published selection a fourth ownership reason for cleanup. It would work
and it is worse: it makes the settings store depend on the ledger's selection, and it leaves two places
that can disagree about what is current.
