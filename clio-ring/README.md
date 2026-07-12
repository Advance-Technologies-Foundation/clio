# clio-ring

clio-ring is an experimental Windows desktop companion for clio. It is an internal preview, is versioned independently, and may be removed if the POC does not prove useful.

## Architecture boundary

- Ring talks to clio through public CLI/MCP contracts.
- clio does not reference Ring or Avalonia assemblies.
- Ring sources, tests, packaging, and release assets live under this subtree.
- No telemetry is collected.

The source was imported from `C:\Projects\clio-ring-spike-claude` commit `f2bf1eb`, the NativeAOT uninstall-pipeline happy-path build proven on 2026-07-12.

## Developer commands

```powershell
dotnet test .\clio-ring\ClioLauncher.Tests\ClioLauncher.Tests.csproj
dotnet run --project .\clio-ring\ClioLauncher.Desktop\ClioLauncher.Desktop.csproj
dotnet publish .\clio-ring\ClioLauncher.Desktop\ClioLauncher.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true
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
