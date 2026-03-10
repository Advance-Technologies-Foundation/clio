---
name: create-mcp-tool
description: Create or update a clio MCP tool when command behavior, MCP exposure, prompts, or resources must be added or kept in sync. Use for work in `clio/Command/McpServer/Tools`, `Prompts`, or `Resources`, especially when adding a new MCP tool, updating arguments, or aligning the MCP contract with an existing command.
---

# Create MCP Tool

Create MCP tools as thin, explicit adapters over the command behavior that already exists in `clio`.

## Workflow

1. Identify the command source of truth.
Review the command options, execution path, validation, auth requirements, and output behavior before touching MCP code.

2. Review the MCP surface together.
Check all relevant files under:
- `clio/Command/McpServer/Tools`
- `clio/Command/McpServer/Prompts`
- `clio/Command/McpServer/Resources`

3. Keep the MCP contract aligned with the command.
Match argument names, required flags, defaults, destructive behavior, and environment-handling behavior to the command contract. If the command is environment-sensitive, use the existing environment-aware tool execution pattern instead of bypassing it.

4. Define stable tool names as constants.
Expose each MCP tool name as a `const` on the tool class so tests can reference the same identifier.

Example:
```csharp
public class ExampleTool : BaseTool<ExampleOptions> {
	internal const string ExampleByEnvironmentToolName = "example-by-environment";
	internal const string ExampleByCredentialsToolName = "example-by-credentials";
}
```

The constants must be visible to the E2E test project. Do not duplicate tool-name strings in tests.

5. Keep descriptions useful.
Use `[McpServerTool(Name = ...)]` with concise descriptions and argument descriptions that explain what a human needs to provide.

6. Review prompt and resource updates.
If the command already has an MCP prompt or resource, keep them aligned with the tool contract. If there is no prompt/resource, explicitly decide whether one is needed.

7. Plan the test work immediately.
Every new or updated MCP tool must also get end-to-end coverage in `clio.mcp.e2e`, not only unit mapping tests in `clio.tests`.

## Required Checks

- Keep the MCP tool aligned with current command behavior.
- Keep tool names centralized as constants on the tool class.
- Prefer one MCP method per invocation mode when the command supports distinct modes such as environment-name and credentials.
- Preserve destructive semantics clearly in names and descriptions.
- Add or update XML docs on public C# API where required by repo policy.
- Treat E2E coverage as mandatory for every MCP tool change; extend the harness if needed instead of deferring that work.

## Change Summary Requirement

When finishing MCP tool work, explicitly state one of:
- `MCP reviewed, no update required`
- what MCP files were updated and why

If command behavior changed, also review command docs per repo policy.
