using System.Text.Json.Serialization;

namespace Clio.Mcp.E2E.Support.Results;

internal sealed record RunProcessEnvelope(
	[property: JsonPropertyName("status")] string? Status,
	[property: JsonPropertyName("processId")] string? ProcessId,
	[property: JsonPropertyName("resultParameterValues")] Dictionary<string, object>? ResultParameterValues,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings,
	[property: JsonPropertyName("error")] string? Error);
