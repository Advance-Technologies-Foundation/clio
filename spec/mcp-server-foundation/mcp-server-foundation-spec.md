# MCP Server Foundation Spec

## Goal

Create a local MCP server for `clio` so AI agents can execute core Creatio operations via structured tools without reading raw CLI docs.

## Scope

- Add a new executable project: `clio.mcp.server`.
- Implement MCP stdio protocol basics:
  - `initialize`
  - `tools/list`
  - `tools/call`
  - `ping`
- Expose first toolset:
  - `env.list`
  - `env.get`
  - `env.upsert`
  - `env.set_active`
  - `creatio.ping`
  - `creatio.get_info`
  - `creatio.call_service`

## Design

- Reuse existing `clio` logic through project reference to `/clio/clio.csproj`.
- Reuse `SettingsRepository` for environment operations.
- Reuse command classes (`PingAppCommand`, `GetCreatioInfoCommand`, `CallServiceCommand`) for remote execution.
- Inject a custom in-memory logger to prevent stdout corruption in MCP stdio mode.

## Output Contract

Each `tools/call` response returns:
- `content[0].text`: short summary
- `structuredContent`: structured payload (`exitCode`, logs, environment data, etc.)
- `isError`: boolean

## Non-goals

- Full 1:1 exposure of all `clio` commands in this iteration.
- OAuth/device auth UX improvements.
- Streaming logs and long-running operation orchestration.
