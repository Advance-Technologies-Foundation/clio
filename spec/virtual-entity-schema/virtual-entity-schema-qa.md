# Virtual entity schema — QA plan

## Command and designer unit tests

| ID | Case | Expected |
|---|---|---|
| TC-U-1 | Parse `create-entity-schema` without `--is-virtual` | `IsVirtual == false` |
| TC-U-2 | Parse `--is-virtual` | `IsVirtual == true` |
| TC-U-3 | Create a normal entity | first saved DTO has `IsVirtual == false` |
| TC-U-4 | Create a virtual entity | first saved DTO has `IsVirtual == true` before DB-structure actualization |

## MCP unit and contract tests

| ID | Case | Expected |
|---|---|---|
| TC-U-5 | `create-entity-schema` omits/adds `is-virtual` | options map false/true |
| TC-U-6 | `sync-schemas` create-entity omits/adds `is-virtual` | options map false/true |
| TC-U-7 | curated contracts for both write tools | Boolean field documented, default false |
| TC-U-8 | get-entity-schema-properties contract | output documents `virtual` |
| TC-U-9 | get-app-info mapping and contract | each entity returns/documents `virtual` |

## MCP sandbox E2E

| ID | Case | Expected |
|---|---|---|
| TC-E-1 | Discover both write contracts | `is-virtual` is present with false default |
| TC-E-2 | Create a virtual entity via `sync-schemas` | success and read-back `virtual == true` |
| TC-E-3 | Inspect PostgreSQL metadata after creation | no physical table exists for the schema |
| TC-E-4 | Read owning application | matching entity has `virtual == true` |

The E2E scenario is destructive, requires the dedicated sandbox opt-in, uses a schema name derived
from the sandbox prefix, and cleans up its application/package artifacts through the existing test
harness.

## Regression commands

```powershell
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --filter "FullyQualifiedName~VirtualEntity" --no-build
```
