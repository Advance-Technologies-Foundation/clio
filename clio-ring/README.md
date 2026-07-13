# clio-ring

clio-ring is an experimental Windows desktop companion for clio. It is an internal preview, is versioned independently, and may be removed if the POC does not prove useful.

## Architecture boundary

- Ring talks to clio through public CLI/MCP contracts.
- clio does not reference Ring or Avalonia assemblies.
- Ring sources, tests, packaging, and release assets live under this subtree.
- No telemetry is collected.

## NativeAOT compatibility is mandatory

The shipped ClioRing application is the Windows x64 NativeAOT artifact. NativeAOT compatibility
is an architectural and release requirement, not a later optimization. Every production change
must satisfy all of these gates:

- `dotnet publish` with `-r win-x64 --self-contained true -p:PublishAot=true` succeeds.
- The publish emits zero IL2026/IL3050 trim/AOT warnings.
- `TreatWarningsAsErrors` remains enabled in production projects and
  `IlcTreatWarningsAsErrors` remains enabled in `ClioRing.Desktop`; do not override either gate.
- JSON crossing production boundaries uses source-generated `System.Text.Json` metadata.
- Production code does not add reflection-based serialization, runtime assembly scanning, dynamic
  code generation, or warning suppressions that conceal AOT incompatibility.
- The reflection-heavy MCP SDK remains contained behind the `ClioRing.Ipc` contract boundary; SDK
  types and implementation details do not leak into UI/application APIs.

A passing JIT build, IDE launch, or unit-test run is not proof of compatibility. Run the NativeAOT
publish command below before considering a Ring change complete.

The source was imported from `C:\Projects\clio\clio-ring` commit `f2bf1eb`, the NativeAOT uninstall-pipeline happy-path build proven on 2026-07-12.

## Developer commands

```powershell
dotnet test .\clio-ring\ClioRing.Tests\ClioRing.Tests.csproj
dotnet run --project .\clio-ring\ClioRing.Desktop\ClioRing.Desktop.csproj
dotnet publish .\clio-ring\ClioRing.Desktop\ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true
```

## Internal installation

```powershell
clio experimental --name ring --enable
clio ring install
clio ring
```

The bootstrap installs versioned releases under `%LOCALAPPDATA%\Creatio\clio-ring` and verifies every ZIP against the SHA-256 in the GitHub release manifest.

## Deletion boundary

Removing the experiment requires deleting this subtree, `.github/workflows/clio-ring-release.yml`, the `RingCommand` implementation and tests, its DI/dispatch/solution entries, and Ring command docs. No clio environment or settings migration is needed.
