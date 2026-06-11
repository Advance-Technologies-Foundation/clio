using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Structured envelope returned by the <c>find-app</c> MCP tool.
/// </summary>
internal sealed record FindAppResponseEnvelope(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("applications")] IReadOnlyList<FindAppItemEnvelope>? Applications = null,
	[property: JsonPropertyName("error")] string? Error = null);

/// <summary>
/// Application item with its sections, as returned by the <c>find-app</c> MCP tool.
/// </summary>
internal sealed record FindAppItemEnvelope(
	[property: JsonPropertyName("id")] string? Id,
	[property: JsonPropertyName("code")] string? Code,
	[property: JsonPropertyName("name")] string? Name,
	[property: JsonPropertyName("version")] string? Version,
	[property: JsonPropertyName("description")] string? Description,
	[property: JsonPropertyName("sections")] IReadOnlyList<FindAppSectionEnvelope>? Sections = null);

/// <summary>
/// Section item nested inside a <see cref="FindAppItemEnvelope"/>.
/// </summary>
internal sealed record FindAppSectionEnvelope(
	[property: JsonPropertyName("code")] string? Code,
	[property: JsonPropertyName("caption")] string? Caption,
	[property: JsonPropertyName("entity-schema-name")] string? EntitySchemaName,
	[property: JsonPropertyName("description")] string? Description);
