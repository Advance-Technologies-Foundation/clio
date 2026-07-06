# mcp-http

## Command Type

    AI integration commands

## Name

mcp-http - start MCP server in HTTP transport mode

## Description

Starts a Model Context Protocol (MCP) server that communicates over
HTTP using the Streamable HTTP transport (JSON-RPC 2.0 over POST).
This server exposes the same clio tools available in stdio mode
(`clio mcp-server`) to AI agents and platforms that connect over HTTP.

The server runs until the process is terminated (Ctrl+C or SIGTERM).

## Prerequisites

`clio mcp-http` hosts the MCP endpoint on ASP.NET Core, so clio requires the
**ASP.NET Core shared runtime** (`Microsoft.AspNetCore.App`) in addition to the base
.NET runtime. The .NET SDK bundles it — SDK installs need nothing extra. Runtime-only
installs that ship only `Microsoft.NETCore.App` are not supported (see the Installation
section of the main README).

## Security

The HTTP transport exposes the same tools as stdio mode — each acting with the operator's
stored credentials for every registered environment — so the server validates incoming
requests to prevent DNS-rebinding and cross-origin abuse (the MCP spec makes this the
host's responsibility):

- **Host header** is restricted to the bound host (`--host`, plus loopback aliases when
  bound to loopback); unexpected `Host` values are rejected (HTTP 400).
- **Origin header**, when present, is restricted to the bound host / loopback; requests
  from any other origin are rejected (HTTP 403). Native MCP clients send no `Origin`
  header and are unaffected.

There is **no built-in authentication**. `--host 0.0.0.0` still exposes the endpoint to
every host that can reach the machine on the LAN — use it only on trusted networks.

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `--port` | `8005` | Port to listen on |
| `--host` | `127.0.0.1` | Host address to bind to |
| `--path` | `/mcp` | MCP endpoint path |

## Examples

Start with default settings (port 8005, localhost):

```shell
clio mcp-http
```

Start on a custom port:

```shell
clio mcp-http --port 9000
```

Listen on all network interfaces (no authentication — use only on trusted networks):

```shell
clio mcp-http --host 0.0.0.0
```

Custom endpoint path:

```shell
clio mcp-http --path /api/mcp
```

## Testing with curl

Send an `initialize` request:

```shell
curl -X POST http://localhost:8005/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}'
```

List available tools:

```shell
curl -X POST http://localhost:8005/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: <session-id-from-initialize>" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `CLIO_MCP_RESPECT_AMBIENT_PROXY` | When `true` or `1`, do NOT neutralize inherited proxy env vars. Default: proxy is bypassed. |

## See Also

- [`mcp-server`](mcp-server.md) - Start MCP server in stdio mode
