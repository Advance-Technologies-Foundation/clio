# MCP tool pattern

This directory contains the MCP surface for `clio`: tools, prompts, and related resources.

## Skill to use

For MCP implementation work in this directory, explicitly use the `create-mcp-tool` skill.

## Base rule

Prefer deriving MCP tools from `clio\Command\McpServer\Tools\BaseTool.cs`.

For tools that call backend MCP server, use `BaseMcpBackendTool<T>`.

Use one of these two execution paths:

- `InternalExecute(options)`
  Use this when the tool should execute the injected command instance directly.
  This is correct for commands that are not bound to per-call environment settings.

- `InternalExecute<TCommand>(options)`
  Use this when `options` inherit from `EnvironmentOptions` and the command depends on environment-bound services such as `IApplicationClient`, `EnvironmentSettings`, or `IServiceUrlBuilder`.
  This resolves a fresh command instance for the environment carried by the current MCP call and avoids reusing the stale startup-time command.

- `ExecuteMcpTool(options, toolName, arguments)` (from `BaseMcpBackendTool`)
  Use this when the tool calls backend Creatio MCP server (DB-first approach).
  This handles MCP HTTP communication, session management, and error handling.

## Backend MCP Tools (DB-first)

Tools with `-db` suffix call the Creatio backend MCP server for DB-first operations:

**Application tools:**
- `application-create-db` - Create applications
- `application-get-info-db` - Get application info
- `application-get-list-db` - List applications

**Entity tools:**
- `entity-create-db` - Create entity schemas
- `entity-create-lookup-db` - Create lookup schemas
- `entity-update-db` - Update entity schemas
- `entity-check-name-db` - Check if name is taken
- `entity-list-packages-db` - List packages
- `entity-get-schema-db` - Get schema details

**Binding tools:**
- `binding-create-db` - Create data bindings
- `binding-get-columns-db` - Get binding columns

**Page tools:**
- `page-get-db` - Get Freedom UI page
- `page-update-db` - Update Freedom UI page
- `page-list-db` - List Freedom UI pages

These tools use `BaseMcpBackendTool<T>` and call backend via `McpHttpClient`.

## Environment-sensitive commands

If a tool accepts any of these:

- environment name
- URI/login/password
- OAuth client credentials

assume the tool is environment-sensitive unless proven otherwise.

In that case, do not execute the injected command directly. Use `InternalExecute<TCommand>(options)` so the command is resolved for the current request.

## Commands with custom setup

Some commands require command-specific setup before execution, for example attaching progress handlers.

In those cases use:

- `InternalExecute<TCommand>(options, configureCommand: ...)`

Example use cases:

- subscribe to `StatusChanged`
- attach temporary callbacks
- tweak command instance state before `Execute`

## Uniformity rules

- New MCP tools should inherit from `BaseTool<TOptions>` unless there is a strong reason not to.
- Do not duplicate local `InternalExecute` implementations in tools when `BaseTool` can handle the flow.
- Keep tool methods focused on:
  - MCP argument mapping
  - selecting the correct execution path
  - optional command-specific setup

## Related artifacts

When adding or updating an MCP tool, also review:

- prompts in `clio\Command\McpServer\Prompts`
- any related MCP resources in `clio\Command\McpServer\Resources`
- unit tests in `clio.tests\Command\McpServer`
- end-to-end tests in `clio.mcp.e2e`
- tool descriptions and safety flags (`ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`)

## Test requirement

- Every new or changed MCP tool must ship with updated `clio.mcp.e2e` coverage.
- Do not stop at unit mapping tests; MCP implementation work is incomplete until the real `clio mcp-server` path is exercised end to end.
- If an existing E2E harness cannot support the tool yet, extend the harness as part of the same task instead of deferring E2E coverage.

## Workspace-scoped tools

For tools that operate on a local workspace:

- require `workspace-path` when the tool may be called outside the current shell working directory
- validate ownership against the local workspace before mutating the remote environment
- mark destructive tools as destructive
