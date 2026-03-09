---
name: test-mcp-tool
description: Add or update unit and end-to-end coverage for a clio MCP tool. Use when changing MCP tool arguments, tool names, command wiring, prompts, resources, destructive behavior, or when creating a new MCP tool that needs contract tests and E2E coverage.
---

# Test MCP Tool

Test MCP tools at two levels: in-process contract tests and external-process end-to-end tests.

## Coverage Plan

Cover all meaningful argument combinations.

- Test the happy path for each exposed tool method.
- Test every invocation mode separately when the tool exposes more than one method.
- Test negative outcomes with invalid inputs or missing dependencies.
- Cover all possible combinations of optional arguments, especially arguments that are not marked `[Required]`.
- When optional flags change execution mode, add dedicated tests for each mode instead of assuming one representative case is enough.

## Unit Tests

Use `clio.tests` to verify:
- tool discovery names
- argument-to-options mapping
- command resolution path
- destructive flags or prompt/resource alignment when applicable

At minimum, assert that each MCP method:
- uses the tool-name constant from the production tool class
- maps every argument into the expected command options
- preserves defaults for omitted optional arguments
- rejects or reports invalid inputs as expected

## End-to-End Tests

Use `clio.mcp.e2e` for every new or updated MCP tool.

- Start the real `clio mcp-server` process over stdio.
- Discover the tool by the production constant, not a duplicated string literal.
- Verify side effects in the target system, not only MCP success shape.
- For destructive tests, require explicit opt-in and a sandbox target.
- If the current harness cannot cover the tool yet, extend the harness in the same task instead of omitting E2E coverage.

## Assertion Rules

Always use explicit AAA structure and separate important assertions into distinct Allure steps.

Required output assertions:
- On success, assert at least one `Info` message type in the returned execution log.
- On failure, assert an `Error` message type when execution output is available.
- If the MCP server surfaces a top-level invocation error instead of raw command output, still assert human-readable diagnostics and verify no unintended side effects.

Also assert:
- `IsError` state matches the scenario
- exit code matches the scenario
- side effects happened on success
- side effects did not happen on failure

## Tool Name Rule

Tests must reference tool names from constants defined on the production tool class.

Example:
```csharp
private const string ToolUnderTestTag = ClearRedisTool.ClearRedisByEnvironmentName;
```

Do not hardcode MCP tool names in test files.

## Finish Checklist

- Unit coverage updated in `clio.tests`
- E2E coverage updated in `clio.mcp.e2e`
- Optional-argument combinations reviewed explicitly
- `Info` and `Error` message-type assertions added where relevant
- Allure metadata added for MCP E2E tests
- AGENTS or local guidance updated when a new MCP testing convention is introduced
