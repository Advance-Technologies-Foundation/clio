# get-info target classification - QA plan

## Unit - Command module

| ID | Case | Expected |
|---|---|---|
| U1 | HTML response | exit 1, non-Creatio message, no body/parser detail |
| U2 | plain non-JSON response | exit 1, non-Creatio message |
| U3 | malformed JSON beginning as JSON | exit 1, stable unexpected-response message |
| U4 | valid JSON with missing `applicationInfo.sysValues` | exit 1, stable unexpected-response message |
| U5 | `HttpRequestException` / connect failure | exit 1, connection message |
| U6 | `TaskCanceledException` / timeout | exit 1, connection message |
| U7 | `UnauthorizedAccessException`, HTTP 401/403, or auth envelope | exit 1, authentication message |
| U8 | malformed or unsupported URI | exit 1 before any request |
| U9 | debug failure path with secret-like exception/body text | only safe type/classification metadata logged |
| U10 | valid base response without compatible ClioGate | exit 0 with base report and warning |
| U11 | valid base response with system/ClioGate enrichment | exit 0 with merged report |
| U12 | ClioGate compatibility check throws | exit 0 with base report |

## Unit - MCP module

| ID | Case | Expected |
|---|---|---|
| M1 | Resolved command receives HTML | exit 1, Error log with non-Creatio message |
| M2 | Existing environment and timeout mapping | unchanged |
| M3 | Tool metadata/description | advertises classified actionable failures |

## MCP E2E

Use a local loopback HTTP stub and the real `clio mcp-server` child. The stub accepts the basic
authentication handshake and returns HTML for the ApplicationInfoService endpoint.

| ID | Case | Expected |
|---|---|---|
| E1 | `describe-environment` against loopback non-Creatio stub | structured exit 1, Error message contains non-Creatio guidance and no parser/body detail |

The E2E is `McpE2E.NoEnvironment`; it does not mutate or require a Creatio instance.

## Compatibility and regression

- `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`
- targeted `GetCreatioInfoToolE2ETests` external-process run
- `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release`
- `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true`

No live Creatio deployment is required because the changed failure classes are deterministic at
the command boundary and the successful/enrichment contracts are covered with existing substituted
clients.
