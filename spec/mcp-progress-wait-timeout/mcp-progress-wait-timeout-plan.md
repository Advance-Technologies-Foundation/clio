# MCP progress wait timeout plan

## Decision

Replace timer polling in `McpServerSession` with a versioned task-completion signal broadcast by the raw notification handler. Each wake takes a fresh immutable queue snapshot scoped to the request's typed progress token. If the deadline is reached, take one final snapshot, return it only when the condition is satisfied, and otherwise throw `TimeoutException` with a bounded typed-event summary. Do not complete a terminal wait until every distinct protocol sequence from zero through terminal is present because MCP notification callbacks may complete concurrently.

## Compatibility

The change is confined to `clio.mcp.e2e`. Production MCP notifications and the ClioRing contract are unchanged.

## Safety

Timeout diagnostics include event type, run identifier, sequence, stage status, and terminal outcome. They do not serialize raw notification payloads or configuration values.
