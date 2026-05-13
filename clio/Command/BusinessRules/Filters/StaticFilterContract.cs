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
		IReadOnlyList<StaticFilterBackwardReference>? BackwardReferenceFilters,
	[property: JsonPropertyName("groups")]
		IReadOnlyList<StaticFilterGroup>? Groups = null);

internal sealed record StaticFilterLeaf(
	[property: JsonPropertyName("columnPath")] string ColumnPath,
	[property: JsonPropertyName("comparisonType")] string ComparisonType,
	[property: JsonPropertyName("value")] JsonElement? Value);

/// <summary>
/// Backward-reference filter on a 1:N relationship to a child schema. The
/// <c>referenceColumnPath</c> uses the bracketed <c>[ChildSchema:ColumnOnChild]</c>
/// syntax where <c>ColumnOnChild</c> is the Lookup column on the child that points
/// back to the parent (root) schema.
///
/// Aggregation behaviour:
/// - <c>aggregationType</c> = <c>EXISTS</c> (default when omitted) or <c>NOT_EXISTS</c>:
///   the filter requires at least one (or no) matching child record. <c>comparisonType</c>,
///   <c>aggregationColumnPath</c>, and <c>aggregationValue</c> must not be provided.
/// - <c>aggregationType</c> = <c>COUNT</c> | <c>SUM</c> | <c>AVG</c> | <c>MIN</c> |
///   <c>MAX</c>: aggregate the matching child rows and compare the result to a value.
///   <c>comparisonType</c> + <c>aggregationValue</c> are required;
///   <c>aggregationColumnPath</c> is required for SUM/AVG/MIN/MAX (the column being
///   aggregated) and ignored for COUNT.
/// </summary>
internal sealed record StaticFilterBackwardReference(
	[property: JsonPropertyName("referenceColumnPath")] string ReferenceColumnPath,
	[property: JsonPropertyName("filter")] StaticFilterGroup Filter,
	[property: JsonPropertyName("aggregationType")] string? AggregationType = null,
	[property: JsonPropertyName("aggregationColumnPath")] string? AggregationColumnPath = null,
	[property: JsonPropertyName("comparisonType")] string? ComparisonType = null,
	[property: JsonPropertyName("aggregationValue")] JsonElement? AggregationValue = null);
