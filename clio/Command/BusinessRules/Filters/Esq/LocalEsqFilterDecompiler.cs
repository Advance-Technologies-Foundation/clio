using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Clio.Command.BusinessRules.Filters.Esq;

internal static class LocalEsqFilterDecompiler {

	internal static JsonNode Decompile(string esqEnvelopeJson) {
		if (string.IsNullOrWhiteSpace(esqEnvelopeJson)) {
			throw new InvalidOperationException("apply-static-filter has no persisted ESQ envelope.");
		}

		JsonNode? parsed;
		try {
			parsed = JsonNode.Parse(esqEnvelopeJson);
		} catch (System.Text.Json.JsonException exception) {
			throw new InvalidOperationException(
				$"apply-static-filter ESQ envelope is not valid JSON: {exception.Message}", exception);
		}

		if (parsed is not JsonObject envelope) {
			throw new InvalidOperationException("apply-static-filter ESQ envelope root must be a JSON object.");
		}

		return DecompileGroup(envelope);
	}

	private static JsonObject DecompileGroup(JsonObject group) {
		JsonObject result = new() {
			["logicalOperation"] = MapLogicalOperation(GetInt(group, "logicalOperation", (int)EsqLogicalOperation.And))
		};

		JsonArray filters = [];
		JsonArray groups = [];
		JsonArray backwardReferences = [];

		if (group["items"] is JsonObject items) {
			foreach (KeyValuePair<string, JsonNode?> entry in items) {
				if (entry.Value is not JsonObject item) {
					throw new InvalidOperationException($"apply-static-filter item '{entry.Key}' must be a JSON object.");
				}

				RouteItem(item, filters, groups, backwardReferences);
			}
		}

		if (filters.Count > 0) {
			result["filters"] = filters;
		}

		if (groups.Count > 0) {
			result["groups"] = groups;
		}

		if (backwardReferences.Count > 0) {
			result["backwardReferenceFilters"] = backwardReferences;
		}

		return result;
	}

	private static void RouteItem(JsonObject item, JsonArray filters, JsonArray groups, JsonArray backwardReferences) {
		int filterType = GetInt(item, "filterType", -1);
		switch ((EsqFilterType)filterType) {
			case EsqFilterType.FilterGroup:
				groups.Add(DecompileGroup(item));
				return;
			case EsqFilterType.IsNullFilter:
				filters.Add(DecompileIsNull(item));
				return;
			case EsqFilterType.InFilter:
				filters.Add(DecompileLookupIn(item));
				return;
			case EsqFilterType.Exists:
				backwardReferences.Add(DecompileExists(item));
				return;
			case EsqFilterType.CompareFilter:
				RouteCompareFilter(item, filters, backwardReferences);
				return;
			default:
				throw new InvalidOperationException($"apply-static-filter carries an unsupported filterType '{filterType}'.");
		}
	}

	private static void RouteCompareFilter(JsonObject item, JsonArray filters, JsonArray backwardReferences) {
		JsonObject leftExpression = RequireObject(item, "leftExpression", "compare filter");
		int leftExpressionType = GetInt(leftExpression, "expressionType", (int)EsqExpressionType.SchemaColumn);

		if (GetBool(item, "isAggregative", defaultValue: false)
			|| leftExpressionType == (int)EsqExpressionType.SubQuery) {
			backwardReferences.Add(DecompileAggregation(item, leftExpression));
			return;
		}

		if (leftExpressionType == (int)EsqExpressionType.Function) {
			filters.Add(DecompileDatePart(item, leftExpression));
			return;
		}

		if (item["rightExpression"] is JsonObject rightFunction
			&& GetInt(rightFunction, "expressionType", (int)EsqExpressionType.Parameter) == (int)EsqExpressionType.Function) {
			filters.Add(DecompileMacros(item, leftExpression, rightFunction));
			return;
		}

		filters.Add(DecompileScalarCompare(item, leftExpression));
	}

	private static JsonObject DecompileIsNull(JsonObject item) {
		bool isNull = GetInt(item, "comparisonType", (int)EsqComparisonType.IsNull) == (int)EsqComparisonType.IsNull;
		return new JsonObject {
			["columnPath"] = RequireColumnPath(item, "leftExpression"),
			["comparisonType"] = isNull ? StaticFilterConstants.IsNull : StaticFilterConstants.IsNotNull
		};
	}

	private static JsonObject DecompileLookupIn(JsonObject item) {
		if (item["rightExpressions"] is not JsonArray rightExpressions || rightExpressions.Count == 0) {
			throw new InvalidOperationException("apply-static-filter lookup filter has no rightExpressions.");
		}

		List<string> values = [];
		foreach (JsonNode? rightExpression in rightExpressions) {
			if (rightExpression is not JsonObject parameterExpression) {
				throw new InvalidOperationException("apply-static-filter lookup rightExpression must be a JSON object.");
			}

			values.Add(ReadLookupValue(parameterExpression));
		}

		JsonObject leaf = new() {
			["columnPath"] = RequireColumnPath(item, "leftExpression"),
			["comparisonType"] = MapEqualityComparison(GetInt(item, "comparisonType", (int)EsqComparisonType.Equal))
		};

		if (values.Count == 1) {
			leaf["value"] = values[0];
		} else {
			JsonArray valueArray = [];
			foreach (string value in values) {
				valueArray.Add(value);
			}

			leaf["value"] = valueArray;
		}

		return leaf;
	}

	private static JsonObject DecompileScalarCompare(JsonObject item, JsonObject leftExpression) {
		JsonObject rightExpression = RequireObject(item, "rightExpression", "compare filter");
		JsonObject parameter = RequireObject(rightExpression, "parameter", "compare filter parameter");
		return new JsonObject {
			["columnPath"] = ReadColumnPath(leftExpression),
			["comparisonType"] = MapLeafComparison(GetInt(item, "comparisonType", -1)),
			["value"] = ClonePropertyValue(parameter, "value")
		};
	}

	private static JsonObject DecompileDatePart(JsonObject item, JsonObject leftExpression) {
		int datePartType = GetInt(leftExpression, "datePartType", -1);
		if (!DatePartCatalog.TryResolveName(datePartType, out string datePartName,
			out DatePartCatalog.DatePartValueKind valueKind)) {
			throw new InvalidOperationException($"apply-static-filter carries an unsupported datePartType '{datePartType}'.");
		}

		JsonObject functionArgument = RequireObject(leftExpression, "functionArgument", "datePart");
		JsonObject rightExpression = RequireObject(item, "rightExpression", "datePart");
		JsonObject parameter = RequireObject(rightExpression, "parameter", "datePart parameter");
		return new JsonObject {
			["columnPath"] = ReadColumnPath(functionArgument),
			["comparisonType"] = MapLeafComparison(GetInt(item, "comparisonType", -1)),
			["datePart"] = datePartName,
			["value"] = valueKind == DatePartCatalog.DatePartValueKind.Time
				? ReadTimeOfDay(parameter)
				: ClonePropertyValue(parameter, "value")
		};
	}

	private static JsonObject DecompileMacros(JsonObject item, JsonObject leftExpression, JsonObject rightExpression) {
		int macrosType = GetInt(rightExpression, "macrosType", -1);
		if (!MacrosCatalog.TryResolveName(macrosType, out string macrosName, out bool requiresArgument)) {
			throw new InvalidOperationException($"apply-static-filter carries an unsupported macrosType '{macrosType}'.");
		}

		JsonObject leaf = new() {
			["columnPath"] = ReadColumnPath(leftExpression),
			["comparisonType"] = MapLeafComparison(GetInt(item, "comparisonType", -1)),
			["valueMacros"] = macrosName
		};

		if (requiresArgument) {
			JsonObject argument = RequireObject(rightExpression, "functionArgument", "macros argument");
			JsonObject parameter = RequireObject(argument, "parameter", "macros argument parameter");
			leaf["valueMacrosArgument"] = ClonePropertyValue(parameter, "value");
		}

		return leaf;
	}

	private static JsonObject DecompileExists(JsonObject item) {
		bool isExists = GetInt(item, "comparisonType", (int)EsqComparisonType.Exists) == (int)EsqComparisonType.Exists;
		JsonObject result = new() {
			["referenceColumnPath"] = StripBackwardReferenceSuffix(RequireColumnPath(item, "leftExpression")),
			["comparisonType"] = isExists ? StaticFilterConstants.Exists : StaticFilterConstants.NotExists
		};

		JsonObject? subFilter = ReadSubFilter(item);
		if (subFilter is not null) {
			result["filter"] = subFilter;
		}

		return result;
	}

	private static JsonObject DecompileAggregation(JsonObject item, JsonObject leftExpression) {
		int aggregationTypeValue = GetInt(leftExpression, "aggregationType", -1);
		string aggregationType = MapAggregationType(aggregationTypeValue);
		string aggregatedColumn = ReadColumnPath(leftExpression);
		(string referenceColumnPath, string? aggregationColumnPath) = SplitAggregatedColumn(aggregatedColumn, aggregationType);

		JsonObject rightExpression = RequireObject(item, "rightExpression", "aggregation");
		JsonObject parameter = RequireObject(rightExpression, "parameter", "aggregation parameter");

		JsonObject result = new() {
			["referenceColumnPath"] = referenceColumnPath,
			["aggregationType"] = aggregationType,
			["comparisonType"] = MapLeafComparison(GetInt(item, "comparisonType", -1)),
			["aggregationValue"] = ClonePropertyValue(parameter, "value")
		};

		if (aggregationColumnPath is not null) {
			result["aggregationColumnPath"] = aggregationColumnPath;
		}

		JsonObject? subFilter = ReadSubFilter(leftExpression) ?? ReadSubFilter(item);
		if (subFilter is not null) {
			result["filter"] = subFilter;
		}

		return result;
	}

	private static JsonObject? ReadSubFilter(JsonObject owner) {
		if (owner["subFilters"] is not JsonObject subFilters
			|| subFilters["items"] is not JsonObject items
			|| items.Count == 0) {
			return null;
		}

		return DecompileGroup(subFilters);
	}

	private static string ReadLookupValue(JsonObject parameterExpression) {
		JsonNode? parameterValue = parameterExpression["parameter"]?["value"];
		if (parameterValue is JsonObject lookupValue) {
			string? displayName = GetStringValue(lookupValue, "Name") ?? GetStringValue(lookupValue, "displayValue");
			string? id = GetStringValue(lookupValue, "Id") ?? GetStringValue(lookupValue, "value");
			string? resolved = displayName ?? id;
			if (string.IsNullOrEmpty(resolved)) {
				throw new InvalidOperationException("apply-static-filter lookup value carries neither a display name nor an Id.");
			}

			return resolved;
		}

		if (parameterValue is JsonValue value && value.TryGetValue(out string? scalar) && !string.IsNullOrEmpty(scalar)) {
			return scalar;
		}

		throw new InvalidOperationException("apply-static-filter lookup value has an unsupported shape.");
	}

	private static string ReadTimeOfDay(JsonObject parameter) {
		string? raw = GetStringValue(parameter, "value")?.Trim().Trim('"');
		if (!string.IsNullOrWhiteSpace(raw)
			&& DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)) {
			return parsed.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		}

		string? dateValue = GetStringValue(parameter, "dateValue");
		if (!string.IsNullOrWhiteSpace(dateValue)
			&& DateTimeOffset.TryParse(dateValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsedOffset)) {
			return parsedOffset.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		}

		throw new InvalidOperationException("apply-static-filter datePart HourMinute value could not be parsed back to a time of day.");
	}

	private static (string ReferenceColumnPath, string? AggregationColumnPath) SplitAggregatedColumn(
		string aggregatedColumn, string aggregationType) {
		int closingBracket = aggregatedColumn.IndexOf(']');
		if (!aggregatedColumn.StartsWith('[') || closingBracket < 0) {
			throw new InvalidOperationException(
				$"apply-static-filter aggregation column '{aggregatedColumn}' is not a backward reference path.");
		}

		string referenceColumnPath = aggregatedColumn[..(closingBracket + 1)];
		string remainder = aggregatedColumn[(closingBracket + 1)..].TrimStart('.');
		if (string.Equals(aggregationType, StaticFilterConstants.Count, StringComparison.Ordinal)) {
			return (referenceColumnPath, null);
		}

		return (referenceColumnPath, string.IsNullOrEmpty(remainder) ? null : remainder);
	}

	private static string StripBackwardReferenceSuffix(string columnPath) =>
		columnPath.EndsWith(".Id", StringComparison.Ordinal) ? columnPath[..^3] : columnPath;

	private static string RequireColumnPath(JsonObject item, string expressionProperty) =>
		ReadColumnPath(RequireObject(item, expressionProperty, "filter"));

	private static string ReadColumnPath(JsonObject expression) {
		string? columnPath = GetStringValue(expression, "columnPath");
		if (string.IsNullOrWhiteSpace(columnPath)) {
			throw new InvalidOperationException("apply-static-filter expression has no columnPath.");
		}

		return columnPath;
	}

	private static JsonNode ClonePropertyValue(JsonObject owner, string propertyName) {
		JsonNode? value = owner[propertyName];
		if (value is null) {
			throw new InvalidOperationException($"apply-static-filter parameter has no '{propertyName}'.");
		}

		return value.DeepClone();
	}

	private static JsonObject RequireObject(JsonObject owner, string propertyName, string context) =>
		owner[propertyName] as JsonObject
		?? throw new InvalidOperationException($"apply-static-filter {context} has no '{propertyName}' object.");

	private static string MapLogicalOperation(int logicalOperation) =>
		logicalOperation == (int)EsqLogicalOperation.Or ? StaticFilterConstants.LogicalOr : StaticFilterConstants.LogicalAnd;

	private static string MapLeafComparison(int comparisonType) => (EsqComparisonType)comparisonType switch {
		EsqComparisonType.Equal => StaticFilterConstants.Equal,
		EsqComparisonType.NotEqual => StaticFilterConstants.NotEqual,
		EsqComparisonType.Less => StaticFilterConstants.Less,
		EsqComparisonType.LessOrEqual => StaticFilterConstants.LessOrEqual,
		EsqComparisonType.Greater => StaticFilterConstants.Greater,
		EsqComparisonType.GreaterOrEqual => StaticFilterConstants.GreaterOrEqual,
		EsqComparisonType.StartWith => StaticFilterConstants.StartWith,
		EsqComparisonType.NotStartWith => StaticFilterConstants.NotStartWith,
		EsqComparisonType.Contain => StaticFilterConstants.Contain,
		EsqComparisonType.NotContain => StaticFilterConstants.NotContain,
		EsqComparisonType.EndWith => StaticFilterConstants.EndWith,
		EsqComparisonType.NotEndWith => StaticFilterConstants.NotEndWith,
		_ => throw new InvalidOperationException($"apply-static-filter carries an unsupported comparisonType '{comparisonType}'.")
	};

	private static string MapEqualityComparison(int comparisonType) => (EsqComparisonType)comparisonType switch {
		EsqComparisonType.Equal => StaticFilterConstants.Equal,
		EsqComparisonType.NotEqual => StaticFilterConstants.NotEqual,
		_ => throw new InvalidOperationException(
			$"apply-static-filter lookup filter carries an unsupported comparisonType '{comparisonType}'.")
	};

	private static string MapAggregationType(int aggregationType) => (EsqAggregationType)aggregationType switch {
		EsqAggregationType.Count => StaticFilterConstants.Count,
		EsqAggregationType.Sum => StaticFilterConstants.Sum,
		EsqAggregationType.Avg => StaticFilterConstants.Avg,
		EsqAggregationType.Min => StaticFilterConstants.Min,
		EsqAggregationType.Max => StaticFilterConstants.Max,
		_ => throw new InvalidOperationException($"apply-static-filter carries an unsupported aggregationType '{aggregationType}'.")
	};

	private static int GetInt(JsonObject source, string propertyName, int defaultValue) =>
		source[propertyName] is JsonValue value && value.TryGetValue(out int result) ? result : defaultValue;

	private static bool GetBool(JsonObject source, string propertyName, bool defaultValue) =>
		source[propertyName] is JsonValue value && value.TryGetValue(out bool result) ? result : defaultValue;

	private static string? GetStringValue(JsonObject source, string propertyName) =>
		source[propertyName] is JsonValue value && value.TryGetValue(out string? result) ? result : null;
}
