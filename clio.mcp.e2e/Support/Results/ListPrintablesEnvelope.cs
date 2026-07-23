using System.Text.Json.Serialization;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Minimal wire envelope for the <c>list-printables</c> probe: the shared
/// <c>EnvironmentProbeResponse</c> success/error contract. Parsed from the real MCP tool result via
/// <see cref="EntitySchemaStructuredResultParser.Extract{T}"/>. Only the fields a behavioral failure
/// assertion needs are mapped; extend with the printables payload when a success-path test is added.
/// </summary>
internal sealed record ListPrintablesEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string? Error);
