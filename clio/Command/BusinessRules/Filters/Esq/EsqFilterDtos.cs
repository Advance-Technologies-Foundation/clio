using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Top-level ESQ filter group serialized into <c>BusinessRuleValueExpression.value</c>
/// of <c>BusinessRuleActionSetFilter</c>. JSON shape mirrors the platform's
/// <c>Terrasoft.Nui.ServiceModel.DataContract.Filters</c> envelope produced by
/// <c>CrtCopilot.LlmEsqConverter</c> — values match Terrasoft enum integers exactly.
/// </summary>
internal sealed class SerializableFilters : SerializableFilter {
	[JsonPropertyName("rootSchemaName")]
	public string? RootSchemaName { get; set; }
}

internal class SerializableFilter {
	[JsonPropertyName("filterType")]
	public EsqFilterType FilterType { get; set; }

	[JsonPropertyName("comparisonType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqFilterComparisonType? ComparisonType { get; set; }

	[JsonPropertyName("logicalOperation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqLogicalOperationStrict? LogicalOperation { get; set; }

	[JsonPropertyName("isNull")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IsNull { get; set; }

	[JsonPropertyName("isEnabled")]
	public bool IsEnabled { get; set; } = true;

	[JsonPropertyName("isNot")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IsNot { get; set; }

	[JsonPropertyName("subFilters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableFilters? SubFilters { get; set; }

	[JsonPropertyName("items")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, SerializableFilter>? Items { get; set; }

	[JsonPropertyName("leftExpression")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableExpression? LeftExpression { get; set; }

	[JsonPropertyName("rightExpression")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableExpression? RightExpression { get; set; }

	[JsonPropertyName("rightExpressions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableExpression[]? RightExpressions { get; set; }

	[JsonPropertyName("rightLessExpression")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableExpression? RightLessExpression { get; set; }

	[JsonPropertyName("rightGreaterExpression")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableExpression? RightGreaterExpression { get; set; }

	[JsonPropertyName("trimDateTimeParameterToDate")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? TrimDateTimeParameterToDate { get; set; }

	[JsonPropertyName("key")]
	public string Key { get; set; } = string.Empty;

	[JsonPropertyName("isAggregative")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IsAggregative { get; set; }

	[JsonPropertyName("leftExpressionCaption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? LeftExpressionCaption { get; set; }

	[JsonPropertyName("referenceSchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ReferenceSchemaName { get; set; }

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = string.Empty;

	[JsonPropertyName("dataValueType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqDataValueType? DataValueType { get; set; }
}

internal sealed class SerializableExpression {
	[JsonPropertyName("expressionType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqEntitySchemaQueryExpressionType? ExpressionType { get; set; }

	[JsonPropertyName("isBlock")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IsBlock { get; set; }

	[JsonPropertyName("columnPath")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ColumnPath { get; set; }

	[JsonPropertyName("parameter")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableParameter? Parameter { get; set; }

	[JsonPropertyName("functionType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqFunctionType? FunctionType { get; set; }

	[JsonPropertyName("macrosType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqEntitySchemaQueryMacrosType? MacrosType { get; set; }

	[JsonPropertyName("functionArgument")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableExpression? FunctionArgument { get; set; }

	[JsonPropertyName("functionArguments")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableExpression[]? FunctionArguments { get; set; }

	[JsonPropertyName("dateDiffInterval")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqDateDiffQueryFunctionInterval? DateDiffInterval { get; set; }

	[JsonPropertyName("datePartType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqDatePart? DatePartType { get; set; }

	[JsonPropertyName("aggregationType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqAggregationType? AggregationType { get; set; }

	[JsonPropertyName("aggregationEvalType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqAggregationEvalType? AggregationEvalType { get; set; }

	[JsonPropertyName("subFilters")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableFilters? SubFilters { get; set; }

	[JsonPropertyName("arithmeticOperation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqArithmeticOperation? ArithmeticOperation { get; set; }

	[JsonPropertyName("leftArithmeticOperand")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableExpression? LeftArithmeticOperand { get; set; }

	[JsonPropertyName("rightArithmeticOperand")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SerializableExpression? RightArithmeticOperand { get; set; }

	[JsonPropertyName("subOrderDirection")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqOrderDirection? SubOrderDirection { get; set; }

	[JsonPropertyName("subOrderColumn")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SubOrderColumn { get; set; }

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = string.Empty;
}

// A single non-derived parameter type covers both scalar and date cases. STJ does not
// serialize derived-class members through a base-typed reference unless polymorphism is
// declared with a discriminator, but the platform envelope shape carries no discriminator —
// so DateValue is just an optional field on the unified parameter type.
internal sealed class SerializableParameter {
	[JsonPropertyName("dataValueType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public EsqDataValueType? DataValueType { get; set; }

	[JsonPropertyName("value")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public object? Value { get; set; }

	[JsonPropertyName("dateValue")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DateValue { get; set; }

	[JsonPropertyName("className")]
	public string ClassName { get; set; } = string.Empty;
}

/// <summary>
/// Lookup parameter value shape required by the Creatio platform's Freedom UI business-rule
/// editor: <c>{Name, Id, value, displayValue}</c>. <c>Id</c> and <c>value</c> are both the
/// record GUID and are always populated; <c>Name</c> and <c>displayValue</c> carry the
/// primary-display-column value so the rule editor renders the lookup record by name.
/// When the display value is unknown (caller passed a raw GUID and no resolver round-trip was
/// possible), only the GUID-bearing fields are written, keeping the filter functional at the
/// SQL level even though the UI shows "&lt;?&gt;" in place of the name.
/// Field declaration order matches the platform's expected JSON shape.
/// </summary>
internal sealed class LookupValueDto {
	[JsonPropertyName("Name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Name { get; set; }

	[JsonPropertyName("Id")]
	public Guid Id { get; set; }

	[JsonPropertyName("value")]
	public Guid Value { get; set; }

	[JsonPropertyName("displayValue")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DisplayValue { get; set; }
}
