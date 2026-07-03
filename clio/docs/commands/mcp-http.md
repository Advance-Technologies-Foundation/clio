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
