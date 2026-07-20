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

## Application settings and clio runtime

`ClioRing.Desktop/app-settings.json` references the colocated `app-settings.schema.json`, so JSON-aware editors provide validation and hover descriptions. The `Channel` value is only the label shown in Ring's build badge; values such as `release`, `dev`, or `preview` do **not** select which clio executable Ring starts.

Ring has a separate `ClioRuntimeMode` setting:

- `release` starts a verified dotnet-tool shim from the standard user directory or `DOTNET_CLI_HOME`; custom paths remain explicit Development targets.
- `development` starts the saved local development target.

The main Ring surface always shows the active mode. Development mode uses a prominent warning with a
Release/Development switch. The switch updates `app-settings.json`, preserves the development target,
and takes effect after Ring restarts.
The same selected runtime drives deployment workflows, environment discovery, and ordinary radial actions.

### Release clio updates

Ring checks NuGet at startup and every eight hours for the latest listed stable `clio` package. A newer
version adds a durable notice to the main surface and changes the tray icon, tooltip, and first menu action.
The check timestamp and one-notification-per-version acknowledgement are persisted without process data, so
restarting Ring does not repeatedly query or notify. Ring never installs an update automatically, and
Development mode remains untouched.

Choosing **Update** gracefully stops only Ring's own Release MCP child, then runs the trusted dotnet host
with the exact version shown in the notice and an isolated NuGet configuration containing only NuGet.org.
If Windows Restart Manager confirms that another application still has the Release shim open, Ring
shows each trusted `clio.exe` process with its PID, executable path, secret-free command classification, and
immediate parent application. **Cancel** leaves every process running. **Kill clio processes and retry** is
a separate explicit action that revalidates each PID, start time, and executable path before terminating
only those clio processes. Claude, Codex, and other parent applications are never terminated.

To expose the experimental MCP-over-stdio UI and point it at a development build, use:

```json
{
  "$schema": "./app-settings.schema.json",
  "WorkspaceFolder": "C:\\Projects\\Workspaces",
  "Channel": "release",
  "ClioRuntimeMode": "development",
  "Experiments": {
    "ClioIpc": true
  },
  "DevClioPath": "C:\\Projects\\clio\\clio\\bin\\Debug\\net10.0\\clio.dll"
}
```

In Development mode, target selection uses this precedence on the next Ring launch:

1. A valid `DevClioPath` (`clio.dll` or `clio.exe`).
2. An explicit `ClioIpc` block with `Command`, `Args`, and optional `WorkingDirectory`.

With legacy settings that do not contain `ClioRuntimeMode`, Ring keeps using Development when either
target exists. Clean settings select Release. This migration prevents an existing development setup from
changing silently while making Release the default for ordinary installations.

`DevClioPath` and `ClioIpc.Command` are code-execution trust boundaries: Ring starts the selected program with your user privileges. Use only trusted local builds and commands, prefer absolute paths, and do not point them at downloaded binaries or locations writable by other users.

For example, the equivalent explicit launch block is:

```json
"ClioIpc": {
  "Command": "dotnet",
  "Args": [
    "C:\\Projects\\clio\\clio\\bin\\Debug\\net10.0\\clio.dll",
    "mcp-server"
  ]
}
```

## Deletion boundary

Removing the experiment requires deleting this subtree, `.github/workflows/clio-ring-release.yml`, the `RingCommand` implementation and tests, its DI/dispatch/solution entries, and Ring command docs. No clio environment or settings migration is needed.
