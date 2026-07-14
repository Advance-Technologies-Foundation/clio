# MCP progress wait timeout plan

## Decision

Replace timer polling in `McpServerSession` with a `SemaphoreSlim` released by the raw notification handler. Each wake takes a fresh immutable queue snapshot. If the deadline is reached, take one final snapshot, return it only when the condition is satisfied, and otherwise throw `TimeoutException` with typed-event metadata only.

## Compatibility

The change is confined to `clio.mcp.e2e`. Production MCP notifications and the ClioRing contract are unchanged.

## Safety

Timeout diagnostics include event type, run identifier, sequence, stage status, and terminal outcome. They do not serialize raw notification payloads or configuration values.
