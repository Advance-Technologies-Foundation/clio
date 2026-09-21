# Lifetime boundary independent reproduction

Upstream reviewed: 61ae2696f18797f79403ef3ad813fb5c5043433e. Windows 26200, .NET10.0.12. Raw observations: codex-lifetime-windows.txt.

Commands from repository root:

```powershell
dotnet build experiments/DetachedOperations/RuntimeV1/DetachedRuntime.V1.csproj -c Release
dotnet build experiments/DetachedOperations/RuntimeV2/DetachedRuntime.V2.csproj -c Release
dotnet run --project experiments/DetachedOperations/Host/Host.csproj -c Release -- run artifacts/detached/10.0.0.0 artifacts/detached/10.1.0.0
```

28/28 passed. The repaired admission arm sampled zero violations, split-admission control sampled 19. N1-N3 attribution counterexamples passed. P1 preserves Succeeded after injected terminal-write failure; P2 reports envP degraded and quiescent while both target/global swap windows are refused. O1 keeps the ALC alive while a returned runtime-defined object is held and collects after it is dropped.

This reproduces the new experimental behavior, not approval of its product policy. The previous stuck-Running revision was source-inspected, not rerun in this review. The write-failure injection occurs before Append; partial writes, fsync failures and crash recovery of those cases are not covered.

Source-grounded qualification: OperationLedger._live holds host-contract OperationRecord values, and Complete removes _owners even on the degraded path. Unloading a runtime DLL does not itself delete that host-owned record; replacing the process/Core owning the ledger does. The contract's claim that retiring the release necessarily destroys the only evidence conflates these boundaries. Portable output remains necessary for reliable unloading, but persistence degradation must be evaluated against the component being replaced.

Read-only local Claude review rev_fb0ad4af33204158 is pending against this exact source and evidence. No approval is claimed. No product code was changed.
