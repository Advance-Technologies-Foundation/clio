using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.BusinessRules.Filters.Schema;
using Clio.Common;

namespace Clio.Command.Filters;

/// <summary>Findings of one semantic pass. Errors are definite; warnings mean "could not be proven".</summary>
internal sealed class EsqFilterFindings {
	internal List<string> Errors { get; } = [];
	internal List<string> Warnings { get; } = [];
}

/// <summary>
/// Checks a stored ESQ filter tree against the live entity schemas — the part no registry type can
/// express: does the column exist, does the operator fit its type, does a macro fit the column, does a
/// lookup parameter carry a record Id. These are the mistakes that make a widget render an error or a
/// wrong number while the page saves fine (ENG-95270). Only definite mismatches are errors; anything the
/// environment could not answer is a warning, so a flaky read never blocks a save.
/// </summary>
internal sealed class EsqFilterSemanticValidator {

	private const int EqualComparison = 3;
	private const int NotEqualComparison = 4;
	private const int FirstRelationalComparison = 5;
	private const int LastRelationalComparison = 8;
	private const int FirstTextComparison = 9;
	private const int LastTextComparison = 14;

	private readonly EsqColumnPathResolver _resolver;

	internal EsqFilterSemanticValidator(IFilterSchemaProvider schemaProvider) {
		_resolver = new EsqColumnPathResolver(schemaProvider);
	}

	internal EsqFilterFindings Validate(EsqFilterGroupNode group, string rootSchemaName) {
		EsqFilterFindings findings = new();
		ValidateGroup(group, rootSchemaName, findings);
		return findings;
	}

	private void ValidateGroup(EsqFilterGroupNode group, string rootSchemaName, EsqFilterFindings findings) {
		foreach (EsqFilterNode node in group.Items) {
			switch (node) {
				case EsqFilterGroupNode nested:
					ValidateGroup(nested, rootSchemaName, findings);
					break;
				case EsqFilterLeafNode leaf:
					ValidateLeaf(leaf, rootSchemaName, findings);
					break;
			}
		}
	}

	private void ValidateLeaf(EsqFilterLeafNode leaf, string rootSchemaName, EsqFilterFindings findings) {
		if (leaf.Left is null) {
			return;
		}
		if (leaf.FilterType == EsqFilterCodes.ExistsFilter) {
			ValidateExists(leaf, rootSchemaName, findings);
			return;
		}
		FilterSchemaColumn? column = ResolveLeft(leaf.Left, rootSchemaName, findings);
		if (column is null) {
			return;
		}
		if (leaf.FilterType == EsqFilterCodes.IsNullFilter) {
			return;
		}
		ValidateComparison(leaf, column, findings);
		foreach (EsqExpressionNode right in leaf.Right) {
			ValidateRight(right, column, leaf.Left.ColumnPath ?? string.Empty, findings);
		}
	}

	private FilterSchemaColumn? ResolveLeft(EsqExpressionNode left, string rootSchemaName, EsqFilterFindings findings) {
		switch (left.ExpressionType) {
			case EsqFilterCodes.SchemaColumnExpression when left.ColumnPath is not null:
				return ResolveColumn(left.ColumnPath, rootSchemaName, $"{left.Path}.columnPath", findings)?.Column;
			case EsqFilterCodes.FunctionExpression when left.FunctionType == EsqFilterCodes.DatePartFunction
				&& left.FunctionArgument?.ColumnPath is not null:
				FilterSchemaColumn? argument = ResolveColumn(left.FunctionArgument.ColumnPath, rootSchemaName,
					$"{left.FunctionArgument.Path}.columnPath", findings)?.Column;
				if (argument is not null && !CreatioDataValueType.IsDateTime(argument.DataValueTypeName)) {
					findings.Errors.Add(
						$"{left.Path}: a date-part function applies only to Date/DateTime/Time columns; '{left.FunctionArgument.ColumnPath}' is {argument.DataValueTypeName}.");
				}
				return null;
			case EsqFilterCodes.SubQueryExpression when left.ColumnPath is not null:
				EsqColumnResolution? subQuery = ResolveColumn(left.ColumnPath, rootSchemaName, $"{left.Path}.columnPath", findings);
				if (subQuery?.LastJoinedSchema is not null && left.SubFilters is not null) {
					ValidateGroup(left.SubFilters, subQuery.LastJoinedSchema, findings);
				}
				return null;
			default:
				return null;
		}
	}

	private void ValidateExists(EsqFilterLeafNode leaf, string rootSchemaName, EsqFilterFindings findings) {
		string? columnPath = leaf.Left!.ColumnPath;
		if (columnPath is null) {
			return;
		}
		if (!columnPath.Contains('[')) {
			findings.Errors.Add(
				$"{leaf.Left.Path}.columnPath: an exists filter needs a backward reference such as '[ChildSchema:LinkColumn].Id'; '{columnPath}' has none.");
			return;
		}
		EsqColumnResolution? resolution = ResolveColumn(columnPath, rootSchemaName, $"{leaf.Left.Path}.columnPath", findings);
		if (resolution?.LastJoinedSchema is not null && leaf.SubFilters is not null) {
			ValidateGroup(leaf.SubFilters, resolution.LastJoinedSchema, findings);
		}
	}

	private static void ValidateComparison(EsqFilterLeafNode leaf, FilterSchemaColumn column, EsqFilterFindings findings) {
		if (leaf.ComparisonType is not int comparison || leaf.Left!.ExpressionType != EsqFilterCodes.SchemaColumnExpression) {
			return;
		}
		string columnPath = leaf.Left.ColumnPath ?? string.Empty;
		if (comparison is >= FirstRelationalComparison and <= LastRelationalComparison
			&& !CreatioDataValueType.IsNumeric(column.DataValueTypeName)
			&& !CreatioDataValueType.IsDateTime(column.DataValueTypeName)) {
			findings.Errors.Add(
				$"{leaf.Path}.comparisonType: {comparison} (less/greater) needs a numeric or date/time column; '{columnPath}' is {column.DataValueTypeName}.");
		}
		if (comparison is >= FirstTextComparison and <= LastTextComparison
			&& !CreatioDataValueType.IsText(column.DataValueTypeName)) {
			findings.Errors.Add(
				$"{leaf.Path}.comparisonType: {comparison} (starts with/contains/ends with) needs a text column; '{columnPath}' is {column.DataValueTypeName}.");
		}
	}

	private static void ValidateRight(EsqExpressionNode right, FilterSchemaColumn column, string columnPath, EsqFilterFindings findings) {
		if (right.ExpressionType == EsqFilterCodes.FunctionExpression
			&& right.FunctionType == EsqFilterCodes.MacrosFunction
			&& right.MacrosType is int macrosCode) {
			ValidateMacros(right, macrosCode, column, columnPath, findings);
			return;
		}
		if (right.ExpressionType != EsqFilterCodes.ParameterExpression || right.Parameter?.Value is not JsonElement value
			|| value.ValueKind == JsonValueKind.Null) {
			return;
		}
		if (CreatioDataValueType.IsLookupOrGuid(column.DataValueTypeName) && !CarriesRecordId(value)) {
			findings.Errors.Add(
				$"{right.Path}.parameter.value: '{columnPath}' is {column.DataValueTypeName}, so the value must be the record Id or an object with 'value' (the Id) and 'displayValue'.");
		}
		if (string.Equals(column.DataValueTypeName, "Boolean", StringComparison.OrdinalIgnoreCase)
			&& value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
			findings.Errors.Add(
				$"{right.Path}.parameter.value: '{columnPath}' is Boolean, so the value must be true or false.");
		}
	}

	private static void ValidateMacros(EsqExpressionNode right, int macrosCode, FilterSchemaColumn column, string columnPath,
		EsqFilterFindings findings) {
		if (!MacrosCatalog.TryResolveName(macrosCode, out string name, out _)
			|| !MacrosCatalog.TryResolve(name, out _, out MacrosCatalog.MacrosColumnKind kind, out _)) {
			return;
		}
		if (kind == MacrosCatalog.MacrosColumnKind.DateTime && !CreatioDataValueType.IsDateTime(column.DataValueTypeName)) {
			findings.Errors.Add(
				$"{right.Path}.macrosType: {macrosCode} ({name}) applies only to Date/DateTime/Time columns; '{columnPath}' is {column.DataValueTypeName}.");
		}
		if (kind == MacrosCatalog.MacrosColumnKind.Lookup && !CreatioDataValueType.IsLookupOrGuid(column.DataValueTypeName)) {
			findings.Errors.Add(
				$"{right.Path}.macrosType: {macrosCode} ({name}) applies only to lookup columns; '{columnPath}' is {column.DataValueTypeName}.");
		}
	}

	private static bool CarriesRecordId(JsonElement value) {
		if (value.ValueKind == JsonValueKind.String) {
			return Guid.TryParse(value.GetString(), out _);
		}
		if (value.ValueKind != JsonValueKind.Object) {
			return false;
		}
		foreach (string idProperty in new[] { "value", "Id" }) {
			if (value.TryGetProperty(idProperty, out JsonElement id) && id.ValueKind == JsonValueKind.String
				&& Guid.TryParse(id.GetString(), out _)) {
				return true;
			}
		}
		return false;
	}

	private EsqColumnResolution? ResolveColumn(string columnPath, string schemaName, string path, EsqFilterFindings findings) {
		EsqColumnResolution resolution = _resolver.Resolve(columnPath, schemaName);
		if (resolution.IsResolved) {
			return resolution;
		}
		if (resolution.Unverifiable) {
			findings.Warnings.Add($"{path}: '{columnPath}' could not be verified: {resolution.Error}.");
		} else {
			findings.Errors.Add($"{path}: '{columnPath}' does not resolve on '{schemaName}': {resolution.Error}.");
		}
		return null;
	}
}
