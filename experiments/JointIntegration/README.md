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
| X3 | **the integration finding** — a snapshot held by an owner with no liveness is never reclaimed, and cleanup gives no signal |
| X4 | **negative control** — with a liveness-capable owner the same snapshot IS reclaimed |
| X5 | a failed runtime activation leaves the committed runtime/configuration pair unchanged — **currently RED, by design** |

## The finding

`SettingsStore.Cleanup()` consults three ownership reasons, one of which is the ledger's
`OperationHeldSnapshots`. That property resolves orphans, so a dead owner normally releases its snapshot
and cleanup reclaims it — X4.

But an owner with **no liveness support** is never resolved. Its operation stays `Running` forever, so it
holds its snapshot forever, and `Cleanup()` returns an empty list and reports nothing unusual:

```
X3 {"reclaimed": [], "cfg3SurvivedCleanup": true, "listedByLedger": true,
    "stateOfDeadOwnersOperation": "Running"}
X4 {"reclaimed": ["cfg-5"], "cfg5Gone": true,
    "stateOfDeadOwnersOperation": "Unknown"}
```

The only signal is `OwnersWithoutLiveness`, which the settings owner does not consult. Neither lane's own
tests can see this: the ledger correctly reports what it cannot resolve, the settings owner correctly
consumes the held set, and the leak lives in the gap between the two.

The fix belongs to the settings owner and is one line of policy, not a mechanism: a cleanup that finds
`OwnersWithoutLiveness` non-empty for a scope should say so rather than silently keep everything.

## X5 is red on purpose

@kirillkrylov's acceptance criterion: *successful settings preparation followed by a failed runtime
activation*, after which admissions must observe **one committed pair**. Reproduced rather than
predicted:

```
X5 {"activationFailed": true, "committedSnapshot": "cfg-7",
    "preparedButNeverActivated": "cfg-8",
    "runtimeSeen": "10.0.0.0", "snapshotSeen": "cfg-8"}
```

The committed pair is (`10.0.0.0`, `cfg-7`). `cfg-8` was prepared for a release that was then refused
for declaring an unsupported contract generation. The admission afterwards observes
(`10.0.0.0`, `cfg-8`) — **a pair that was never committed**. Settings moved ahead of a runtime
activation that never happened.

This is not a defect in the settings store's own behaviour: `Prepare` activates on success, which is
correct for a settings-only change and wrong for one half of a paired activation. The seam needed is the
smallest possible separation, and it belongs to the settings lane:

- `Prepare` returns a **candidate** and does not make it current.
- `Activate` makes a candidate current.
- A candidate is retained against cleanup for the activation window only — not a general staged-settings
  framework, just the gap between the two calls.

The caller then orders a paired activation as: prepare the candidate, activate the runtime, and activate
the candidate only if the runtime activation succeeded. A refused runtime never reaches the second step,
so the pair stays as it was. X5 flips to green when that lands, and is the acceptance test for it.
