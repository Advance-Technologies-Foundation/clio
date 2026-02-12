# clio-mcp-server (PoC)

Minimal MCP server over stdio that exposes AI-friendly tools on top of `clio`.

## Run

```bash
dotnet run --project /Users/v.nikonov/Documents/GitHub/clio/clio.mcp.server/clio.mcp.server.csproj
```

## Tools

- `env.list`
- `env.get`
- `env.upsert`
- `env.set_active`
- `creatio.ping`
- `creatio.get_info`
- `creatio.call_service`
