# mcp-server

## Command Type

    AI integration commands

## Name

mcp-server, mcp - start MCP server in stdio mode

## Description

Starts a Model Context Protocol (MCP) server that communicates over
standard input/output using JSON-RPC 2.0. This server exposes clio
tools to AI agents and code assistants (Copilot, Claude, Cursor, etc.)
that support the MCP protocol.

The server runs until the stdin stream is closed or the process is
terminated.

Available MCP tool categories:
- application     Create, list, and inspect Creatio applications
- entity          Create and update entity schemas (DB-first)
- schema-sync     Batch entity operations in a single call
- page            Get and update Freedom UI page schemas
- component-info  Inspect curated Freedom UI component contracts
- page-sync       Batch page operations in a single call
- data-binding    Manage data bindings and seed data

Available MCP guidance resources:
- docs://mcp/guides/app-modeling
- docs://mcp/guides/existing-app-maintenance

## Synopsis

```bash
clio mcp-server
clio mcp-server
```

## Examples

```bash
clio mcp-server
Start MCP server and wait for JSON-RPC requests on stdin.

clio mcp-server
Start the same MCP server using the short alias.

CLIO_CMD="clio" python3 scripts/mcp_client.py application-get-list '{"environment-name":"local"}'
Call an MCP tool via the Python stdio client.

CLIO_CMD="clio" python3 scripts/mcp_client.py component-info '{"component-type":"crt.TabContainer"}'
Inspect a Freedom UI component contract from the shipped local registry.
```

## Prerequisites

- clio version 8.0.2.35 or higher
- At least one registered clio environment (clio reg-web-app)
- Target Creatio instance must be running and accessible

## Notes

- The server uses stdio transport (stdin/stdout), not HTTP
- Environment-sensitive tools require either an "environment-name" or explicit connection args such as "uri", "login", and "password"
- "component-info" is local and read-only, so it does not require environment or connection args
- Use "tool-contract-get" when the MCP client needs the authoritative clio MCP contract and preferred discovery or mutation flow
- Preferred existing-app flow is application-get-list -> application-get-info, then page or schema inspection, then page-update / modify-entity-schema-column / schema-sync as needed
- Boolean parameters must be JSON booleans (true/false), not strings
- Entity tools work DB-first: schemas are created directly in PostgreSQL

## Return Values

    0       Server shut down normally
    1       Server failed to start

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#mcp-server)
