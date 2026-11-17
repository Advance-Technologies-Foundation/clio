using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.Filters;

/// <summary>
/// Encoding-neutral view of a stored ESQ filter tree. Both stored encodings — the devkit
/// <c>classToPlain</c> form Freedom UI pages hold and the backend serializable-filter form (with
/// <c>className</c>) business rules hold — read into this one model, so every filter surface is checked
/// by the same rules (ENG-95270). Only what the semantic checks need is modelled; anything else in the
/// JSON is ignored, not rejected.
/// </summary>
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
	int? DatePartType,
	EsqExpressionNode? FunctionArgument,
	EsqParameterNode? Parameter,
	EsqFilterGroupNode? SubFilters);

internal sealed record EsqParameterNode(int? DataValueType, JsonElement? Value);

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
