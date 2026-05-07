using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Friendly contract describing a static filter group on the apply-static-filter
/// business-rule action. Maps directly to the wire shape MCP callers send: a logical
/// operation, an array of leaf filters, and optional backward-reference filters.
/// </summary>
internal sealed record StaticFilterGroup(
	[property: JsonPropertyName("logicalOperation")] string LogicalOperation,
	[property: JsonPropertyName("filters")] IReadOnlyList<StaticFilterLeaf>? Filters,
	[property: JsonPropertyName("backwardReferenceFilters")]
		IReadOnlyList<StaticFilterBackwardReference>? BackwardReferenceFilters);

internal sealed record StaticFilterLeaf(
	[property: JsonPropertyName("columnPath")] string ColumnPath,
	[property: JsonPropertyName("comparisonType")] string ComparisonType,
	[property: JsonPropertyName("value")] JsonElement? Value);

internal sealed record StaticFilterBackwardReference(
	[property: JsonPropertyName("referenceColumnPath")] string ReferenceColumnPath,
	[property: JsonPropertyName("filter")] StaticFilterGroup Filter);
