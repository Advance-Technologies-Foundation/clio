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
- Exposes MCP-owned guidance resources such as `docs://mcp/guides/app-modeling` and `docs://mcp/guides/existing-app-maintenance`.
- `create-lookup` and `schema-sync` `create-lookup` operations register new lookup schemas in the standard `Lookups` section as part of successful completion.
- `component-info` is a local read-only helper and does not require a target environment.
- Entity-schema MCP write tools use explicit localization maps. Send schema and column captions through `title-localizations`, column descriptions through `description-localizations`, and include `en-US` in every localization map.
- `application-create` stays scalar-only. Keep `name`, `description`, and `optional-template-data-json.appSectionDescription` as plain strings, and apply localized schema captions later through entity-schema MCP write tools.

## Existing-App Flow

For minimal edits to an existing installed app, prefer this MCP flow:

- `application-get-list` -> `application-get-info` when the target app is not fully known
- `page-list` -> `page-get` -> `component-info` when needed -> `page-update` for single-page edits
- `get-entity-schema-properties` or `get-entity-schema-column-properties` -> `modify-entity-schema-column` for single-column edits
- `schema-sync` when the work spans multiple ordered schema steps or mixed create/update/seed operations
- read before write, then read back with `page-get`, `get-entity-schema-column-properties`, `get-entity-schema-properties`, or `application-get-info` when explicit verification is needed

## Connection Notes

Environment-sensitive tools usually accept one of these targeting modes:

- `environment-name` for a registered clio environment
- explicit connection arguments such as `uri`, `login`, and `password`

Check the individual tool contract before calling it because required fields differ per tool.
Use `tool-contract-get` when the MCP client needs the authoritative contract and preferred discovery or mutation flow for a specific tool.

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
