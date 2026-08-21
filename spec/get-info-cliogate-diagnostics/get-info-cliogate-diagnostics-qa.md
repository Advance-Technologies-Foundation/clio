# get-info cliogate diagnostics - QA

## Command unit tests

| ID | Case | Expected |
|---|---|---|
| U1 | Working `GetSysInfo`, stale below-floor metadata | merged fields, exit 0, no warning, no metadata lookup |
| U2 | Failed probe, lowest detected alias 2.0.0.31 | base report, exit 0, warning names the lowest alias and 2.0.0.32 floor |
| U3 | Failed probe, detected 2.0.0.45 | base report, exit 0, warning names 2.0.0.45 and read/permission boundary |
| U4 | Failed probe, version lookup throws | base report, exit 0, secret-safe inconclusive warning |

## MCP contract

- Unit metadata asserts the tool describes capability-first probing and diagnostic-only metadata.
- External-process E2E fetches the served full contract through `get-tool-contract` and asserts the
  same rule.

## Compatibility commands

- `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`
- targeted `GetCreatioInfoToolE2ETests` external-process run
- `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release`
- `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true`
