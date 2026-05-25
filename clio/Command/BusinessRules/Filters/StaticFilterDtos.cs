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
}

internal sealed class StaticFilterBackwardReference {
	/// <summary>Backward-reference selector of shape [Schema:Column].</summary>
	public string ReferenceColumnPath { get; init; } = string.Empty;
	/// <summary>Comparison: EXISTS or NOT_EXISTS for this iteration.</summary>
	public string ComparisonType { get; init; } = "EXISTS";
	public StaticFilterGroup? Filter { get; init; }
}
