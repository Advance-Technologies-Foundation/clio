# Runtime retirement: managed loading experiment

Discussion #1643; baseline `7225ebcaa`. This isolated probe changes no product/Core contracts and does not implement automatic retirement.

## Hypothesis and cases

Loading mode and safe retirement are different questions. Stream loading may remove managed image file locks, but an active release can still need a dependency that has not loaded. Path loading need not prevent reclamation once its collectible context is actually collected.

Three tiny projects, no added packages: an entry DLL, a private dependency and a console measurement harness. Both entry and private dependency use the selected load mode. The runner exercises all eight combinations of path/stream, early deletion/no early deletion, and dependency preloaded/lazy. Entry activation first verifies the dependency has not already loaded. A live reflection call owner retains the release until Finish; it then releases references, requests unloading, and observes collection through a weak reference before retrying deletion. Explicit GC is test apparatus, not a proposed production cleanup strategy.

Run from repository root:

```powershell
dotnet build experiments/RuntimeRetirement/Payload/Payload.csproj -c Release
dotnet run --project experiments/RuntimeRetirement/Runner/Runner.csproj -c Release -- experiments/RuntimeRetirement/Payload/bin/Release/net10.0
```

The runner uses a unique temporary directory and never deletes the supplied build directory. Exit code 1 means a required observation failed; JSON contains raw observations. Failed deletion may partially remove files: this is deliberately measured only inside the disposable directory. `EarlyDeleted=false` means either no attempt or an unsuccessful attempt; consult `prematureDelete` to distinguish these.

## Windows observations, 2026-09-21

See windows-results.json (.NET 10.0.12, Windows build 26200). Eight cases met the current probe checks:

- Retaining the release directory until completion allowed both modes to invoke the private dependency and reclaim files after context collection.
- With dependency already loaded, path-mode early deletion failed; stream-mode early deletion succeeded and the loaded dependency remained callable.
- With a lazy dependency, stream-mode early deletion succeeded but Finish failed with FileNotFoundException.
- Path-mode premature deletion failed overall but had already removed the lazy dependency, so Finish also failed. A failed recursive delete is not an atomic no-op.
- All eight contexts were collected and release directories reclaimed within the bounded measurement loop.

## Interpretation and limits

Do not recursively delete a release while an execution owner might still require it, regardless of loading mode. Managed stream loading alone is not a retirement protocol. Path-based reclamation is possible after collection in this controlled fixture; this contradicts an unconditional assertion that path loading makes all in-session cleanup impossible.

This fixture does not implement a concurrent lease manager, switch an MCP runtime, retain detached operations, test native DLLs/resources, prove unloadability of actual Creatio/DI dependencies, guarantee GC timing, or coordinate multiple processes. It models one live owner, not production races. Mac/Linux results remain for Alex to independently reproduce. Production changes require a separate proof integrating real release ownership; do not infer that the present Core unloads old releases mid-session.

Smallest next design question: can we retire routing to an old generation, wait until all its owned work ends, dispose its scope/container, release host references, and only reclaim after unload is actually observed? If not yet, retaining complete immutable release directories is safer than deleting them while executable work survives. Alex owns the detached-operation experiment; this branch deliberately does not implement his scope.
