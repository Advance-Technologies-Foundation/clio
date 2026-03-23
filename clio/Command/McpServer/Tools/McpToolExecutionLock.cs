namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Provides a single global lock object shared by all MCP tools to serialize
/// command execution and protect shared logger state.
/// </summary>
internal static class McpToolExecutionLock {

	internal static readonly object SyncRoot = new();
}
