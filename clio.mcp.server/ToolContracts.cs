namespace Clio.McpServer;

public readonly record struct ToolExecutionResult(bool IsError, string Message, object Payload);
