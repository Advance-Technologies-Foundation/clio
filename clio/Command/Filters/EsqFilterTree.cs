using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.Filters;

// Encoding-neutral model of a stored ESQ filter tree: the devkit classToPlain form (pages) and the backend
// serializable form with className (business rules) both read into it. Unmodelled JSON is ignored, not rejected.
internal abstract record EsqFilterNode(string Path);

internal sealed record EsqFilterGroupNode(
	string Path,
	string? RootSchemaName,
	IReadOnlyList<EsqFilterNode> Items) : EsqFilterNode(Path);

internal sealed record EsqFilterLeafNode(
	string Path,
	int FilterType,
	int? ComparisonType,
	EsqExpressionNode? Left,
	IReadOnlyList<EsqExpressionNode> Right,
	EsqFilterGroupNode? SubFilters) : EsqFilterNode(Path);

internal sealed record EsqExpressionNode(
	string Path,
	int ExpressionType,
	string? ColumnPath,
	int? FunctionType,
	int? MacrosType,
	EsqExpressionNode? FunctionArgument,
	EsqParameterNode? Parameter,
	EsqFilterGroupNode? SubFilters);

internal sealed record EsqParameterNode(JsonElement? Value);

/// <summary>Wire codes shared by both stored encodings (devkit <c>ɵFilterType</c>/<c>ɵExpressionType</c>/<c>ɵFunctionType</c>).</summary>
internal static class EsqFilterCodes {
	internal const int CompareFilter = 1;
	internal const int IsNullFilter = 2;
	internal const int BetweenFilter = 3;
	internal const int InFilter = 4;
	internal const int ExistsFilter = 5;
	internal const int FilterGroup = 6;

	internal const int SchemaColumnExpression = 0;
	internal const int FunctionExpression = 1;
	internal const int ParameterExpression = 2;
	internal const int SubQueryExpression = 3;

	internal const int MacrosFunction = 1;
	internal const int DatePartFunction = 3;
}
