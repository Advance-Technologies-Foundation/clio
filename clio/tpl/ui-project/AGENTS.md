## Remote Module Reference Guides

**MANDATORY:** Read and follow these guides before working on any task related to Creatio Freedom UI integrations with remote modules:

Runtime components: node_modules\@creatio-devkit\common\AI_GUIDES_INDEX.md
Design-time components (if relevant): node_modules\@creatio\interface-designer\AI_GUIDES_INDEX.md

## Creating package test projects (required)

Clio is the only approved way to scaffold package unit-test and integration-test projects.
Do not hand-create test `.csproj`, `.sln`, or `.slnx` files, run `dotnet new` test templates,
or invent a separate test harness. Write scenario-specific test cases inside the Clio-generated
projects, using their existing frameworks, base fixtures, and configuration conventions.
This rule concerns package C# tests; retain the existing Angular test tooling for UI tests.

Run these commands from the Clio workspace root (the directory containing `.clio`):

```shell
clio unit-test --package <Package_Name>
clio integration-test --package <Package_Name>
```

For example, for package `UsrOrders`:

```shell
clio unit-test --package UsrOrders
clio integration-test --package UsrOrders --target-framework net8.0
```

Integration tests default to `net10.0`; select `--target-framework` to match the intended test runtime.
Before adding integration scenarios, read `get-guidance name=integration-testing`.
For MCP, discover contracts with `get-tool-contract`, then invoke the existing tools through `clio-run`:

```json
{"command":"new-test-project","args":{"package-name":"UsrOrders","workspace-path":"<absolute-workspace-path>","environment-name":"<registered-environment>"}}
```

```json
{"command":"new-integration-test-project","args":{"package-name":"UsrOrders","workspace-path":"<absolute-workspace-path>","target-framework":"net8.0"}}
```

Use the workspace root even when working inside a nested UI project. Unit-test MCP scaffolding
requires a registered environment name; integration-test scaffolding does not.
Verify the command's exit code and the resulting solution membership before reporting success:

| Scaffold | Generated test project | Test solution | Workspace solution |
|---|---|---|---|
| Unit | `tests/UsrOrders/UsrOrders.Tests.csproj` | `tests/UnitTests.slnx` | `MainSolution.slnx` |
| Integration | `tests/UsrOrders.IntegrationTests/UsrOrders.IntegrationTests.csproj` | `tests/IntegrationTests.slnx` | `MainSolution.slnx` |

Both solutions must contain the generated test project. UnitTests.slnx also contains the package
project under test. Rerunning the unit scaffold repairs missing registrations and preserves existing
project/fixture files. The integration scaffold refuses an existing project directory to protect
customizations. If Clio reports an error, report the diagnostic and fix the Clio workflow; do not
bypass it by inventing another test project. Scaffolding alone does not prove the tests pass.
