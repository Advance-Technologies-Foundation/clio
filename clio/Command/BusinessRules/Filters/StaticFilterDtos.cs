using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Friendly typed representation of the apply-static-filter payload after structural validation.
/// </summary>
internal sealed class StaticFilterGroup {
	public string LogicalOperation { get; init; } = "AND";
	public List<StaticFilterLeaf> Filters { get; init; } = [];
	public List<StaticFilterGroup> Groups { get; init; } = [];
	public List<StaticFilterBackwardReference> BackwardReferenceFilters { get; init; } = [];
}

internal sealed class StaticFilterLeaf {
	/// <summary>Column path on the current schema, may include forward references (e.g. Country.Name).</summary>
	public string ColumnPath { get; init; } = string.Empty;
	public string ComparisonType { get; init; } = string.Empty;
	public JsonElement? Value { get; init; }
	/// <summary>Dynamic macros name (e.g. Today, CurrentUser); mutually exclusive with <see cref="Value"/>.</summary>
	public string? ValueMacros { get; init; }
	/// <summary>Integer argument for N-style macros (e.g. NextNDays); required only for those.</summary>
	public int? ValueMacrosArgument { get; init; }
}

internal sealed class StaticFilterBackwardReference {
	/// <summary>Backward-reference selector of shape [Schema:Column].</summary>
	public string ReferenceColumnPath { get; init; } = string.Empty;
	/// <summary>
	/// Comparison token. In EXISTS mode (no <see cref="AggregationType"/>): EXISTS or NOT_EXISTS.
	/// In aggregation mode: a relational/equality token (e.g. GREATER) compared to <see cref="AggregationValue"/>.
	/// </summary>
	public string ComparisonType { get; init; } = "EXISTS";
	/// <summary>Optional aggregation function (COUNT/SUM/AVG/MIN/MAX). When null the filter is an EXISTS/NOT_EXISTS check.</summary>
	public string? AggregationType { get; init; }
	/// <summary>Numeric child-schema column to aggregate; required for SUM/AVG/MIN/MAX, forbidden for COUNT.</summary>
	public string? AggregationColumnPath { get; init; }
	/// <summary>Threshold the aggregation result is compared against (e.g. COUNT GREATER 10).</summary>
	public double? AggregationValue { get; init; }
	public StaticFilterGroup? Filter { get; init; }
}
