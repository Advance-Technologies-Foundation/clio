# Story mcp-progress-heartbeat-2 — Wire heartbeat into the application tool family + contract docs

- **Feature:** `mcp-progress-heartbeat` · **Jira:** ENG-91274 · **ADR:** `spec/adr/adr-mcp-progress-heartbeat.md`
- **Status:** ready-for-dev
- **Depends on:** story-mcp-progress-heartbeat-1

## Story
As an AI assistant calling clio over MCP, I want `create-app`, `create-app-section`,
`update-app-section`, `delete-app-section`, `list-app-sections`, and `get-app-info` to stream
progress while they work, so I do not perceive a stall and fall back to raw SQL / manual UI.

## Acceptance criteria
1. Each of the six tools accepts `RequestContext<CallToolRequestParams>` and wraps its
   backend service call with `McpProgressHeartbeat`.
2. The final structured response is unchanged (same shape, returned synchronously). No
   operation handle / polling contract is introduced.
3. Tool `[Description]`s state the await/progress contract: the call may run long, streams
   progress, and must be awaited — not retried on a perceived stall.
4. `McpServerInstructions`, app-modeling / existing-app-maintenance resources, and
   `ApplicationPrompt` describe the same await/progress contract.
5. Tools keep their existing `ReadOnly`/`Destructive`/`Idempotent`/`OpenWorld` flags.

## Definition of Done
- [ ] `ApplicationTool.cs` wired for all six tools.
- [ ] Contract text updated in tool descriptions + instructions + resources + prompt.
- [ ] Wiring unit tests (TC-U-6..TC-U-7) green.
- [ ] MCP reviewed; no regression in existing `ApplicationToolTests`.
