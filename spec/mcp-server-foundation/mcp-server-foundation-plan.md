# MCP Server Foundation Plan

1. Create `clio.mcp.server` project targeting `net8.0`.
2. Add project reference to `clio`.
3. Implement MCP stdio transport and JSON-RPC request loop.
4. Implement tool registry and JSON schema descriptors.
5. Implement `ClioFacade` with environment management and command execution.
6. Add in-memory logger implementation compatible with `ILogger`.
7. Build and validate project.
8. Document run command and MCP integration snippet.
