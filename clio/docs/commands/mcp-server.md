# mcp-server

## Purpose
`mcp-server` starts the clio Model Context Protocol server over stdio so AI clients can call
clio tools through MCP.

Alias: `mcp`

## Usage
```bash
clio mcp-server
clio mcp
```

## Behavior

- Uses stdio transport with JSON-RPC 2.0.
- Runs until stdin is closed or the process is terminated.
- Exposes structured clio MCP tools such as application, page, component-info, entity, schema-sync, page-sync, and data-binding tools.
- `create-lookup` and `schema-sync` `create-lookup` operations register new lookup schemas in the standard `Lookups` section as part of successful completion.
- `component-info` is a local read-only helper and does not require a target environment.

## Connection Notes

Environment-sensitive tools usually accept one of these targeting modes:

- `environment-name` for a registered clio environment
- explicit connection arguments such as `uri`, `login`, and `password`

Check the individual tool contract before calling it because required fields differ per tool.

## Examples

Start the MCP server with the canonical command:
```bash
clio mcp-server
```

Start the same server with the alias:
```bash
clio mcp
```

Call an MCP tool through the Python stdio client:
```bash
CLIO_CMD="clio" python3 scripts/mcp_client.py application-get-list '{"environment-name":"local"}'
```

Inspect a Freedom UI component contract without connecting to Creatio:
```bash
CLIO_CMD="clio" python3 scripts/mcp_client.py component-info '{"component-type":"crt.TabContainer"}'
```

## Prerequisites

- clio `8.0.2.35` or higher
- at least one registered clio environment for tools that use `environment-name`
- reachable target Creatio instance for the called tool
