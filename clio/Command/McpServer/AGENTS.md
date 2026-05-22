# MCP tool pattern

This directory contains the MCP surface for `clio`: tools, prompts, and related resources.

## Skill to use

For MCP implementation work in this directory, explicitly use the `create-mcp-tool` skill.

## Base rule

Prefer deriving MCP tools from `clio\Command\McpServer\Tools\BaseTool.cs`.

Use one of these two execution paths:

- `InternalExecute(options)`
  Use this when the tool should execute the injected command instance directly.
  This is correct for commands that are not bound to per-call environment settings.

- `InternalExecute<TCommand>(options)`
  Use this when `options` inherit from `EnvironmentOptions` and the command depends on environment-bound services such as `IApplicationClient`, `EnvironmentSettings`, or `IServiceUrlBuilder`.
  This resolves a fresh command instance for the environment carried by the current MCP call and avoids reusing the stale startup-time command.

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

## MCP tool budget policy

clio's MCP tool registry shares the same `tools/list` slot with every other MCP server an agent host has open, and host platforms enforce a fixed cap on the total tool count an agent can see. To keep clio inside that envelope:

- **128 hard limit.** Anthropic and the MCP protocol cap a single host's tool count at 128. clio must never approach this number; every tool we ship competes with other servers the user has installed.
- **75 budget ratchet.** [`clio.tests/Command/McpServer/McpToolBudgetTests.cs`](../../../clio.tests/Command/McpServer/McpToolBudgetTests.cs) asserts the live count against `ToolBudget`. After every consolidation block, the ratchet must move down — never up — without explicit ticket approval.
- **60 soft target.** Plan new tools with the understanding that the long-term goal is to land below 60 registered MCP tools. Treat anything higher as technical debt to be amortized.
- **Extend before add.** Before introducing a new `[McpServerTool]`, look for an existing tool that already covers the resource. Add a `mode` / `action` / `schema-type` / `kind` / `target` / `source` discriminator argument to that tool instead of registering a new MCP entry point. See ENG-90312 (Block B/C/D) for the established consolidation patterns (`restart-creatio`, `restore-db`, `link-from-repository`, `create-schema`, `app-section`, `data-binding-row`, etc.).
- **Deprecation = remove, no aliases.** Do not preserve historical MCP tool names as aliases on new tool methods. When a tool moves to a new contract, the old MCP `[McpServerTool]` registration goes away in the same commit. CLI verbs are unaffected because they live on `[Verb]`-decorated Options classes, not on the MCP wrapper.
- **Per-type tool classes can keep their `[McpServerToolType]`-inherited methods** as internal callables for the dispatcher to delegate into. Strip only the `[McpServerTool]` attribute and the class-level `[McpServerToolType]` (if explicitly applied — the inherited one on `BaseTool<T>` is irrelevant for the dispatcher pattern). Leave a brief `internal const string ...ToolName = "..."` block as documentation so `ToolContractGetTool` and prompt helpers still resolve.

When you cannot avoid raising the budget, ask in the ticket whether the new entry point should instead become a new mode on an existing tool.

## Workspace-scoped tools

For tools that operate on a local workspace:

- require `workspace-path` when the tool may be called outside the current shell working directory
- validate ownership against the local workspace before mutating the remote environment
- mark destructive tools as destructive
