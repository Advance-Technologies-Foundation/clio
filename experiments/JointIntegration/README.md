# Joint integration proof

> **Read [`reference-flow.md`](reference-flow.md) first.** It is the integrated narrative — the flow, who
> owns which mutable state, the guarantees stated together, and the one failure story. This file is the
> case index and the reproduction commands.

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
| X7 | a stale selection no longer admits under a reclaimed snapshot |
| X8 | a cleanup inside the commit window no longer reclaims the snapshot the selection names |
| X10 | **mutation control** — ignoring the selected-snapshot reason reopens the window |
| X9 | the ledger already knows the answer cleanup needs: the snapshot was still selected |

**13/13 on macOS.** Exact build target: this branch, plus `experiments/SettingsVersioning` taken from
`nikonov/supervisor-quiescence-probe@62fb98e05736`. Both are in this tree, so the branch builds and runs
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

## The handoff window, and the reason I was wrong to refuse a fourth ownership reason

@kirillkrylov: *the committed selection must retain its snapshot until admission has acquired operation
ownership.* I had argued that the republish rule made that unnecessary, because selected and pinned end
up equal. The rule is right and it is **not sufficient**.

Committing a pair takes the settings store's lock and then the ledger's. In the window between them the
store is already pinned to the new snapshot while the selection still names the old one. A cleanup
landing there sees the old snapshot as neither pinned, nor held, nor retained:

```
X8 {"reclaimed": ["cfg-16"], "admittedUnder": "cfg-16",
    "stillExists": false, "pinnedInStore": "cfg-17"}
X9 {"selectedAtCleanupTime": true}
```

The republish rule narrows the window; it does not close it. So the fourth ownership reason is needed
after all, and X9 shows the information already exists on the ledger side:
`IOperationLedger.SelectedSnapshots` — the snapshots named by a committed selection, whether or not
anything is running.

This is not a second source of truth about what is *current*. It answers one question the settings store
cannot answer for itself and the ledger cannot interpret: **is this snapshot named by a committed
selection.** The change on the store side is one more reason in `Cleanup`, next to pinned, held and
retained.

## Running it on Windows

Run the built executable directly. `dotnet run` inside a PowerShell pipeline does not return here — the
process completes, the pipeline does not, which looks exactly like a hung test:

```powershell
$exe = "experiments\JointIntegration\bin\Release\net10.0\Clio10.JointIntegration.exe"
$p = Start-Process -FilePath $exe -ArgumentList @(
      "artifacts/detached/10.0.0.0","artifacts/detached/10.1.0.0",
      "artifacts/detached/partner","artifacts/detached/10.2.0.0","C:\Temp\jointwork") `
     -NoNewWindow -PassThru -RedirectStandardOutput joint.json
$p.WaitForExit(180000)
```
