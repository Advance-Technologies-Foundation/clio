using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record CommandExecutionEnvelope(
	[property: JsonPropertyName("exit-code")] int ExitCode,
	[property: JsonPropertyName("execution-log-messages")] IReadOnlyList<CommandLogMessageEnvelope>? Output = null);

internal sealed record CommandLogMessageEnvelope(
	[property: JsonPropertyName("message-type")] LogDecoratorType MessageType,
	[property: JsonPropertyName("value")] string? Value);
