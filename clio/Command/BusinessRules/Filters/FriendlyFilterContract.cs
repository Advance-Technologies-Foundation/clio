using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules.Filters;

internal sealed record FriendlyFilterGroup(
	[property: JsonPropertyName("logicalOperation")] string LogicalOperation,
	[property: JsonPropertyName("filters")] IReadOnlyList<FriendlyFilterLeaf>? Filters,
	[property: JsonPropertyName("backwardReferenceFilters")]
		IReadOnlyList<BackwardReferenceFilter>? BackwardReferenceFilters);

internal sealed record FriendlyFilterLeaf(
	[property: JsonPropertyName("columnPath")] string ColumnPath,
	[property: JsonPropertyName("comparisonType")] string ComparisonType,
	[property: JsonPropertyName("value")] JsonElement? Value);

internal sealed record BackwardReferenceFilter(
	[property: JsonPropertyName("referenceColumnPath")] string ReferenceColumnPath,
	[property: JsonPropertyName("filter")] FriendlyFilterGroup Filter);
