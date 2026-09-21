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

**8/8 on macOS.** Exact build target: this branch, plus `experiments/SettingsVersioning` taken from
`nikonov/supervisor-quiescence-probe@334d0dcf28b4`. Both are in this tree, so the branch builds and runs
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
