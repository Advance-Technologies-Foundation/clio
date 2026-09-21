# Final bounded lease recheck

Upstream 0aff304a7d14b502953cd301f87baa2f748e1f31, Windows26200/.NET10.0.12. Rebuilt RuntimeV1 and RuntimeV2 Release, then ran:

```powershell
dotnet run --project experiments/DetachedOperations/Host/Host.csproj -c Release -- run artifacts/detached/10.0.0.0 artifacts/detached/10.1.0.0
```

33/33 passed; raw codex-lease-final-windows.txt. L1 sees Succeeded when Dispose returns; L2 rejects invalid Running then permits valid Succeeded. Source validates before consuming the report and serializes Complete/Dispose under one lease gate, closing the two reported paths. No opposite lease/ledger lock acquisition was found in this focused diff review.

Limits: L1 uses a 60ms scheduling delay and a 300ms injected completion delay, not an explicit started handshake. Its observed pass is not a deterministic scheduling proof and has no mutation arm. P5 similarly uses timed cleanup. No claim of exhaustive concurrency, partial-write recovery, production integration or human policy approval. Prior Claude review identified the hazards; this final closure is Codex source inspection plus execution, not a new Claude approval.
