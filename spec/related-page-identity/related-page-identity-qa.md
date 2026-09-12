# Related-page identity validation

The fix reports the target Creatio already resolved. It does not change entity resolution,
add-on requests, save metadata, or cache refresh behavior.

Unit tests cover varying designer IDs, stable target identity, malformed/missing identity,
case normalization, web/mobile, clearing, and extension-data serialization preservation.

`RelatedPageIdentityToolE2ETests` is an explicit local fixture: it requires an exclusive
Marketing sandbox containing BulkEmail and Custom. It clears and restores bindings and
rebuilds static configuration, so it cannot run on a shared CI stand. Its independent
identity oracle is SysSchema filtered by Name, EntitySchemaManager and ExtendParent=false.
It verifies repeated reads, own-package reads, clear/readback, restore/readback, and a
missing-object failure. The original semantic page set is restored in a finally block.

Run against a disposable environment with:

```powershell
$env:McpE2E__AllowDestructiveMcpTests = 'true'
$env:McpE2E__Sandbox__EnvironmentName = '<exclusive-environment>'
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Debug -f net10.0 --filter 'FullyQualifiedName~RelatedPageIdentityToolE2ETests'
```

These tools return structured response DTOs rather than CommandExecutionResult logs, so
the fixture checks success/IsError, diagnostics, identity and actual persisted side effects.
There is no Info/Error execution-log collection in these two response contracts.
