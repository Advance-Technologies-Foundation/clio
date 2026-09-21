# Independent cleanup/admission recheck

Upstream 878a83f46cfac7076442817aae28e51db3231348; Windows 26200 / .NET10.0.12. Both runtime fixture projects built Release, then:

```powershell
dotnet run --project experiments/DetachedOperations/Host/Host.csproj -c Release -- run artifacts/detached/10.0.0.0 artifacts/detached/10.1.0.0
```

31/31 passed; raw codex-cleanup-windows.txt. P3 confirms host evidence survives runtime collection. P4 injected admission failure registers zero Running operations and writes no external effect. P5 observes published Succeeded with retirement refused during cleanup, then quiescence and window availability after release.

Source writes admission evidence before inserting live records/owners. Complete publishes outcome; Dispose releases ownership; quiescence uses ownership. This closes the demonstrated source paths. Cleanup fixture uses a timed delay rather than an externally controlled latch; partial persistence and arbitrary consumer misuse remain outside these cases. No production guarantee or policy approval follows. Narrow local Claude recheck requested; verdict pending.
