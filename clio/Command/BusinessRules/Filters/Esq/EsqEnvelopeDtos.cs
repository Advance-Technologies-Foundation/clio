using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Top-level envelope serialized as JSON string into BusinessRuleValueExpression.value.
/// Property names match Terrasoft serializer wire format and MUST stay camelCase.
/// </summary>
internal sealed class EsqRootEnvelopeDto {
	[JsonPropertyName("rootSchemaName")]
	public string RootSchemaName { get; set; } = string.Empty;

	[JsonPropertyName("filterType")]
	public int FilterType { get; set; } = (int)EsqFilterType.FilterGroup;

	[JsonPropertyName("logicalOperation")]
	public int LogicalOperation { get; set; }

	[JsonPropertyName("isEnabled")]
	public bool IsEnabled { get; set; } = true;

	[JsonPropertyName("items")]
	public Dictionary<string, object> Items { get; set; } = [];

	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.FilterGroup;
}

/// <summary>
/// Nested filter-group inside backward-reference subFilters (no rootSchemaName at this level).
/// </summary>
internal sealed class EsqNestedGroupDto {
	[JsonPropertyName("rootSchemaName")]
	public string? RootSchemaName { get; set; }

	[JsonPropertyName("filterType")]
	public int FilterType { get; set; } = (int)EsqFilterType.FilterGroup;

	[JsonPropertyName("logicalOperation")]
	public int LogicalOperation { get; set; }

	[JsonPropertyName("isEnabled")]
	public bool IsEnabled { get; set; } = true;

	[JsonPropertyName("items")]
	public Dictionary<string, object> Items { get; set; } = [];

	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.FilterGroup;
}

internal sealed class EsqCompareFilterDto {
	[JsonPropertyName("filterType")]
	public int FilterType { get; set; } = (int)EsqFilterType.CompareFilter;

	[JsonPropertyName("comparisonType")]
	public int ComparisonType { get; set; }

	[JsonPropertyName("isEnabled")]
	public bool IsEnabled { get; set; } = true;

	[JsonPropertyName("trimDateTimeParameterToDate")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? TrimDateTimeParameterToDate { get; set; }

	[JsonPropertyName("leftExpression")]
	public EsqColumnExpressionDto LeftExpression { get; set; } = new();

	[JsonPropertyName("isAggregative")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IsAggregative { get; set; }

	[JsonPropertyName("dataValueType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? DataValueType { get; set; }

	/// <summary>
	/// Either an <see cref="EsqParameterExpressionDto"/> (constant) or an
	/// <see cref="EsqMacrosFunctionExpressionDto"/> (dynamic macros value).
	/// </summary>
	[JsonPropertyName("rightExpression")]
	public object RightExpression { get; set; } = new EsqParameterExpressionDto();

	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.CompareFilter;
}

internal sealed class EsqIsNullFilterDto {
	[JsonPropertyName("filterType")]
	public int FilterType { get; set; } = (int)EsqFilterType.IsNullFilter;

	[JsonPropertyName("comparisonType")]
	public int ComparisonType { get; set; }

	[JsonPropertyName("isNull")]
	public bool IsNull { get; set; }

	[JsonPropertyName("isEnabled")]
	public bool IsEnabled { get; set; } = true;

	[JsonPropertyName("leftExpression")]
	public EsqColumnExpressionDto LeftExpression { get; set; } = new();

	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.IsNullFilter;
}

internal sealed class EsqInFilterDto {
	[JsonPropertyName("filterType")]
	public int FilterType { get; set; } = (int)EsqFilterType.InFilter;

	[JsonPropertyName("comparisonType")]
	public int ComparisonType { get; set; }

	[JsonPropertyName("isEnabled")]
	public bool IsEnabled { get; set; } = true;

	[JsonPropertyName("leftExpression")]
	public EsqColumnExpressionDto LeftExpression { get; set; } = new();

	[JsonPropertyName("isAggregative")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IsAggregative { get; set; }

	[JsonPropertyName("dataValueType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? DataValueType { get; set; }

	[JsonPropertyName("referenceSchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ReferenceSchemaName { get; set; }

	[JsonPropertyName("rightExpressions")]
	public List<EsqParameterExpressionDto> RightExpressions { get; set; } = [];

	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.InFilter;
}

internal sealed class EsqExistsFilterDto {
	[JsonPropertyName("filterType")]
	public int FilterType { get; set; } = (int)EsqFilterType.Exists;

	[JsonPropertyName("comparisonType")]
	public int ComparisonType { get; set; } = (int)EsqComparisonType.Exists;

	[JsonPropertyName("isEnabled")]
	public bool IsEnabled { get; set; } = true;

	[JsonPropertyName("trimDateTimeParameterToDate")]
	public bool TrimDateTimeParameterToDate { get; set; }

	[JsonPropertyName("leftExpression")]
	public EsqColumnExpressionDto LeftExpression { get; set; } = new();

	[JsonPropertyName("isAggregative")]
	public bool IsAggregative { get; set; } = true;

	[JsonPropertyName("dataValueType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? DataValueType { get; set; }

	[JsonPropertyName("subFilters")]
	public EsqNestedGroupDto SubFilters { get; set; } = new();

	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.ExistsFilter;
}

/// <summary>
/// Backward-reference aggregation filter (COUNT/SUM/AVG/MIN/MAX over a child schema), emitted as a
/// CompareFilter whose leftExpression is an aggregation sub-query. Mirrors the CrtCopilot
/// LlmEsqFiltersConverter aggregation branch.
/// </summary>
internal sealed class EsqAggregationFilterDto {
	[JsonPropertyName("filterType")]
	public int FilterType { get; set; } = (int)EsqFilterType.CompareFilter;

	[JsonPropertyName("comparisonType")]
	public int ComparisonType { get; set; }

	[JsonPropertyName("isEnabled")]
	public bool IsEnabled { get; set; } = true;

	[JsonPropertyName("isAggregative")]
	public bool IsAggregative { get; set; } = true;

	[JsonPropertyName("leftExpression")]
	public EsqAggregationExpressionDto LeftExpression { get; set; } = new();

	[JsonPropertyName("rightExpression")]
	public EsqParameterExpressionDto RightExpression { get; set; } = new();

	[JsonPropertyName("subFilters")]
	public EsqNestedGroupDto SubFilters { get; set; } = new();

	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.CompareFilter;
}

/// <summary>
/// Aggregation sub-query expression used as the leftExpression of <see cref="EsqAggregationFilterDto"/>.
/// </summary>
internal sealed class EsqAggregationExpressionDto {
	[JsonPropertyName("expressionType")]
	public int ExpressionType { get; set; } = (int)EsqExpressionType.SubQuery;

	[JsonPropertyName("functionType")]
	public int FunctionType { get; set; } = (int)EsqFunctionType.Aggregation;

	[JsonPropertyName("aggregationType")]
	public int AggregationType { get; set; }

	[JsonPropertyName("columnPath")]
	public string ColumnPath { get; set; } = string.Empty;

	[JsonPropertyName("subFilters")]
	public EsqNestedGroupDto SubFilters { get; set; } = new();

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.AggregationQueryExpression;
}

internal sealed class EsqColumnExpressionDto {
	[JsonPropertyName("expressionType")]
	public int ExpressionType { get; set; } = (int)EsqExpressionType.SchemaColumn;

	[JsonPropertyName("columnPath")]
	public string ColumnPath { get; set; } = string.Empty;

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.ColumnExpression;
}

internal sealed class EsqParameterExpressionDto {
	[JsonPropertyName("expressionType")]
	public int ExpressionType { get; set; } = (int)EsqExpressionType.Parameter;

	[JsonPropertyName("parameter")]
	public EsqParameterDto Parameter { get; set; } = new();

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.ParameterExpression;
}

/// <summary>
/// Macros function expression used as a CompareFilter rightExpression for dynamic values
/// (e.g. Today, CurrentUser). Mirrors devkit ɵMacrosFunctionExpression.toJson wire shape.
/// </summary>
internal sealed class EsqMacrosFunctionExpressionDto {
	[JsonPropertyName("expressionType")]
	public int ExpressionType { get; set; } = (int)EsqExpressionType.Function;

	[JsonPropertyName("functionType")]
	public int FunctionType { get; set; } = (int)EsqFunctionType.Macros;

	[JsonPropertyName("macrosType")]
	public int MacrosType { get; set; }

	[JsonPropertyName("functionArgument")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqParameterExpressionDto? FunctionArgument { get; set; }

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.FunctionExpression;
}

internal sealed class EsqParameterDto {
	[JsonPropertyName("dataValueType")]
	public int DataValueType { get; set; }

	[JsonPropertyName("value")]
	public object? Value { get; set; }

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = EsqFilterClassNames.Parameter;
}

/// <summary>
/// Lookup value envelope used by InFilter parameters. The platform-canonical form carries the GUID twice
/// (Id + value) and the display name twice (Name + displayValue) so both PascalCase and camelCase consumers resolve.
/// </summary>
internal sealed class EsqLookupValueDto {
	[JsonPropertyName("Name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Name { get; set; }

	[JsonPropertyName("Id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("value")]
	public string Value { get; set; } = string.Empty;

	[JsonPropertyName("displayValue")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DisplayValue { get; set; }
}
