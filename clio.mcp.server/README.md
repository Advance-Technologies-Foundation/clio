# clio-mcp-server

MCP server for `clio`, implemented with the Microsoft C# MCP SDK (`ModelContextProtocol`).

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

## Notes

- Uses `stdio` transport via SDK (`WithStdioServerTransport`).
- Optional env var: `CLIO_MCP_HOME` to override where clio settings are stored.
