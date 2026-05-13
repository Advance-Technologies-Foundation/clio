using System;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Maps ESQ filter / expression types to the dotted Terrasoft class names that the platform
/// uses for polymorphic deserialization (e.g. <c>Terrasoft.CompareFilter</c>,
/// <c>Terrasoft.FilterGroup</c>). Values mirror <c>LlmEsqFiltersConverter.GetFilterClassName</c>
/// and <c>GetExpressionClassName</c> from CrtCopilot.
/// </summary>
internal static class EsqFilterClassNames {

	public const string Parameter = "Terrasoft.Parameter";
	public const string BaseExpression = "Terrasoft.BaseExpression";
	public const string ColumnExpression = "Terrasoft.ColumnExpression";
	public const string ParameterExpression = "Terrasoft.ParameterExpression";
	public const string FunctionExpression = "Terrasoft.FunctionExpression";
	public const string SubQueryExpression = "Terrasoft.SubQueryExpression";
	public const string AggregationQueryExpression = "Terrasoft.AggregationQueryExpression";

	public const string CompareFilter = "Terrasoft.CompareFilter";
	public const string IsNullFilter = "Terrasoft.IsNullFilter";
	public const string InFilter = "Terrasoft.InFilter";
	public const string ExistsFilter = "Terrasoft.ExistsFilter";
	public const string FilterGroup = "Terrasoft.FilterGroup";

	public static string ForFilter(EsqFilterType filterType) =>
		filterType switch {
			EsqFilterType.CompareFilter => CompareFilter,
			EsqFilterType.IsNullFilter => IsNullFilter,
			EsqFilterType.InFilter => InFilter,
			EsqFilterType.Exists => ExistsFilter,
			EsqFilterType.FilterGroup => FilterGroup,
			_ => CompareFilter
		};

	public static string ForExpression(
		EsqEntitySchemaQueryExpressionType expressionType,
		bool isAggregation = false) =>
		expressionType switch {
			EsqEntitySchemaQueryExpressionType.SchemaColumn => ColumnExpression,
			EsqEntitySchemaQueryExpressionType.Parameter => ParameterExpression,
			EsqEntitySchemaQueryExpressionType.Function => FunctionExpression,
			EsqEntitySchemaQueryExpressionType.SubQuery =>
				isAggregation ? AggregationQueryExpression : SubQueryExpression,
			_ => BaseExpression
		};
}
