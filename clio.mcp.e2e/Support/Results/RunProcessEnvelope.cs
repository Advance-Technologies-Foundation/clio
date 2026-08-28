using System.Text.Json.Serialization;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// The <c>run-process</c> response as it crosses the MCP boundary. Extract it with
/// <see cref="EntitySchemaStructuredResultParser.Extract{T}"/>: the tool lives on the lazy surface, so a
/// call arrives through <c>clio-run</c>, whose payload nests the target tool's own JSON inside the text
/// content, and that parser already walks the nesting.
/// </summary>
internal sealed record RunProcessEnvelope(
	[property: JsonPropertyName("status")] string? Status,
	[property: JsonPropertyName("processId")] string? ProcessId,
	[property: JsonPropertyName("resultParameterValues")] Dictionary<string, object>? ResultParameterValues,
	[property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings,
	[property: JsonPropertyName("error")] string? Error);
