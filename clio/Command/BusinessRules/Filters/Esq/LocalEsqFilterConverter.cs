using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Builds Creatio ESQ <see cref="SerializableFilters"/> envelopes from clio's friendly
/// <see cref="StaticFilterGroup"/> contract — no runtime dependency on the CrtCopilot
/// <c>LlmEsqConverterService</c> endpoint. Schema metadata required for Lookup detection
/// and reference traversal is pulled through <see cref="IFilterSchemaProvider"/>.
///
/// Ported and simplified from CrtCopilot's <c>LlmEsqFiltersConverter</c>; relative-date
/// macros, aggregations on backward references, and lookup display-value resolution are
/// out of scope because the clio MCP friendly contract exposes only constant comparisons
/// (14 tokens) and a structural backward-reference (implicit EXISTS).
/// </summary>
internal sealed class LocalEsqFilterConverter(IFilterSchemaProvider schemaProvider) {

	private static readonly Regex BackwardReferenceSyntax =
		new(@"^\[(?<schema>[A-Za-z_][A-Za-z0-9_]*):(?<column>[A-Za-z_][A-Za-z0-9_]*)\]$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant,
			TimeSpan.FromMilliseconds(100));

	public SerializableFilters BuildTopLevelGroup(StaticFilterGroup group, string rootSchemaName) {
		ArgumentNullException.ThrowIfNull(group);
		ArgumentException.ThrowIfNullOrWhiteSpace(rootSchemaName);
		SerializableFilters envelope = new() {
			FilterType = EsqFilterType.FilterGroup,
			IsEnabled = true,
			RootSchemaName = rootSchemaName,
			ClassName = EsqFilterClassNames.FilterGroup,
			Key = string.Empty,
			LogicalOperation = ParseLogicalOperation(group.LogicalOperation),
			Items = new Dictionary<string, SerializableFilter>(StringComparer.Ordinal)
		};
		IReadOnlyList<StaticFilterLeaf> leaves = group.Filters ?? [];
		for (int i = 0; i < leaves.Count; i++) {
			envelope.Items.Add($"Filter_{i}", BuildLeaf(leaves[i], rootSchemaName));
		}
		IReadOnlyList<StaticFilterBackwardReference> backwardRefs = group.BackwardReferenceFilters ?? [];
		for (int i = 0; i < backwardRefs.Count; i++) {
			envelope.Items.Add(
				$"BackwardReferenceFilter_{i}",
				BuildBackwardReference(backwardRefs[i], i));
		}
		return envelope;
	}

	private SerializableFilter BuildNestedGroup(
		StaticFilterGroup group,
		string schemaName,
		int parentIndex) {
		Dictionary<string, SerializableFilter> items = new(StringComparer.Ordinal);
		IReadOnlyList<StaticFilterLeaf> leaves = group.Filters ?? [];
		for (int i = 0; i < leaves.Count; i++) {
			items.Add($"Filter_{parentIndex}_{i}", BuildLeaf(leaves[i], schemaName));
		}
		IReadOnlyList<StaticFilterBackwardReference> backwardRefs = group.BackwardReferenceFilters ?? [];
		for (int i = 0; i < backwardRefs.Count; i++) {
			items.Add(
				$"BackwardReferenceFilter_{parentIndex}_{i}",
				BuildBackwardReference(backwardRefs[i], i));
		}
		return new SerializableFilters {
			FilterType = EsqFilterType.FilterGroup,
			IsEnabled = true,
			LogicalOperation = ParseLogicalOperation(group.LogicalOperation),
			Items = items,
			Key = string.Empty,
			RootSchemaName = schemaName,
			ClassName = EsqFilterClassNames.FilterGroup
		};
	}

	private SerializableFilter BuildLeaf(StaticFilterLeaf leaf, string schemaName) {
		string comparison = leaf.ComparisonType?.ToUpperInvariant() ?? string.Empty;
		if (string.Equals(comparison, "IS_NULL", StringComparison.Ordinal)) {
			return BuildNullFilter(leaf.ColumnPath, isNull: true);
		}
		if (string.Equals(comparison, "IS_NOT_NULL", StringComparison.Ordinal)) {
			return BuildNullFilter(leaf.ColumnPath, isNull: false);
		}
		EntitySchemaColumnDto leafColumn = ResolveLeafColumn(leaf.ColumnPath, schemaName);
		string leafTypeName = BusinessRuleHelpers.MapDataValueTypeName(leafColumn.DataValueType);
		bool isLookupLeaf = string.Equals(leafTypeName, "Lookup", StringComparison.Ordinal)
			&& leafColumn.ReferenceSchema is not null;
		if (isLookupLeaf) {
			return BuildLookupFilter(leaf, leafColumn);
		}
		return BuildCompareFilter(leaf, leafColumn);
	}

	private SerializableFilter BuildBackwardReference(
		StaticFilterBackwardReference brf,
		int parentIndex) {
		Match match = BackwardReferenceSyntax.Match(brf.ReferenceColumnPath);
		if (!match.Success) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				"rule.actions[*].filter.backwardReferenceFilters[*].referenceColumnPath",
				$"Expected '[ChildSchema:ColumnOnChild]' syntax for backward reference; got '{brf.ReferenceColumnPath}'.");
		}
		string childSchemaName = match.Groups["schema"].Value;
		SerializableFilters subFilters = (SerializableFilters)BuildNestedGroup(
			brf.Filter,
			childSchemaName,
			parentIndex * 100);
		string? aggregationType = string.IsNullOrWhiteSpace(brf.AggregationType)
			? null
			: brf.AggregationType.ToUpperInvariant();
		// EXISTS / NOT_EXISTS (default when aggregationType is omitted) — emit a Terrasoft.ExistsFilter
		// wrapping the child group. Functionally "at least one (or no) matching child record".
		if (aggregationType is null
			or "EXISTS"
			or "NOT_EXISTS") {
			return new SerializableFilter {
				FilterType = EsqFilterType.Exists,
				ComparisonType = aggregationType == "NOT_EXISTS"
					? EsqFilterComparisonType.NotExists
					: EsqFilterComparisonType.Exists,
				IsEnabled = true,
				IsAggregative = true,
				Key = string.Empty,
				ClassName = EsqFilterClassNames.ExistsFilter,
				SubFilters = subFilters,
				LeftExpression = new SerializableExpression {
					ExpressionType = EsqEntitySchemaQueryExpressionType.SchemaColumn,
					ColumnPath = NormalizeColumnPath(brf.ReferenceColumnPath),
					ClassName = EsqFilterClassNames.ColumnExpression
				}
			};
		}
		// COUNT / SUM / AVG / MIN / MAX — emit a CompareFilter whose leftExpression is an
		// aggregation SubQuery and whose rightExpression is the threshold. Ported from
		// CrtCopilot.LlmEsqFiltersConverter.ConvertBackwardReferenceFitler aggregation branch.
		return BuildAggregationBackwardReference(brf, aggregationType, subFilters);
	}

	private static SerializableFilter BuildAggregationBackwardReference(
		StaticFilterBackwardReference brf,
		string aggregationType,
		SerializableFilters subFilters) {
		EsqAggregationType esqAggregation = MapAggregationType(aggregationType);
		// COUNT aggregates over the relationship column itself; SUM/AVG/MIN/MAX use the explicit
		// aggregationColumnPath provided by the caller.
		string aggregationColumnPath = esqAggregation == EsqAggregationType.Count
			? NormalizeColumnPath(brf.ReferenceColumnPath)
			: brf.AggregationColumnPath!;
		// Result is numeric: use Integer for COUNT (record count) and Float for SUM/AVG/MIN/MAX.
		EsqDataValueType valueType = esqAggregation == EsqAggregationType.Count
			? EsqDataValueType.Integer
			: EsqDataValueType.Float;
		SerializableExpression rightExpression = new() {
			ExpressionType = EsqEntitySchemaQueryExpressionType.Parameter,
			ClassName = EsqFilterClassNames.ParameterExpression,
			Parameter = new SerializableParameter {
				DataValueType = valueType,
				Value = MaterializeAggregationValue(brf.AggregationValue!.Value, valueType),
				ClassName = EsqFilterClassNames.Parameter
			}
		};
		return new SerializableFilter {
			FilterType = EsqFilterType.CompareFilter,
			ComparisonType = MapComparisonType(brf.ComparisonType!),
			IsEnabled = true,
			IsAggregative = true,
			Key = string.Empty,
			ClassName = EsqFilterClassNames.CompareFilter,
			LeftExpression = new SerializableExpression {
				ColumnPath = aggregationColumnPath,
				ExpressionType = EsqEntitySchemaQueryExpressionType.SubQuery,
				FunctionType = EsqFunctionType.Aggregation,
				AggregationType = esqAggregation,
				SubFilters = subFilters,
				ClassName = EsqFilterClassNames.AggregationQueryExpression
			},
			RightExpression = rightExpression
		};
	}

	private static EsqAggregationType MapAggregationType(string token) =>
		token switch {
			"COUNT" => EsqAggregationType.Count,
			"SUM" => EsqAggregationType.Sum,
			"AVG" => EsqAggregationType.Avg,
			"MIN" => EsqAggregationType.Min,
			"MAX" => EsqAggregationType.Max,
			_ => throw new InvalidOperationException($"Unsupported aggregation token '{token}'.")
		};

	private static object MaterializeAggregationValue(JsonElement value, EsqDataValueType dataValueType) =>
		dataValueType == EsqDataValueType.Integer
			? value.GetInt64()
			: value.GetDouble();

	private static SerializableFilter BuildNullFilter(string columnPath, bool isNull) =>
		new() {
			FilterType = EsqFilterType.IsNullFilter,
			ComparisonType = isNull ? EsqFilterComparisonType.IsNull : EsqFilterComparisonType.IsNotNull,
			IsNull = isNull,
			IsEnabled = true,
			ClassName = EsqFilterClassNames.IsNullFilter,
			Key = string.Empty,
			LeftExpression = new SerializableExpression {
				ColumnPath = NormalizeColumnPath(columnPath),
				ExpressionType = EsqEntitySchemaQueryExpressionType.SchemaColumn,
				ClassName = EsqFilterClassNames.ColumnExpression
			}
		};

	private static SerializableFilter BuildCompareFilter(StaticFilterLeaf leaf, EntitySchemaColumnDto column) {
		EsqDataValueType dataValueType = MapDataValueType(column.DataValueType);
		SerializableExpression right = BuildRightExpression(leaf.Value, dataValueType);
		SerializableFilter compare = new() {
			FilterType = EsqFilterType.CompareFilter,
			ComparisonType = MapComparisonType(leaf.ComparisonType),
			IsEnabled = true,
			ClassName = EsqFilterClassNames.CompareFilter,
			Key = string.Empty,
			LeftExpression = new SerializableExpression {
				ColumnPath = NormalizeColumnPath(leaf.ColumnPath),
				ExpressionType = EsqEntitySchemaQueryExpressionType.SchemaColumn,
				ClassName = EsqFilterClassNames.ColumnExpression
			},
			RightExpression = right
		};
		if (dataValueType == EsqDataValueType.DateTime) {
			compare.TrimDateTimeParameterToDate = true;
		}
		return compare;
	}

	private static SerializableFilter BuildLookupFilter(StaticFilterLeaf leaf, EntitySchemaColumnDto lookupColumn) {
		Guid id = ParseLookupGuid(leaf.Value, leaf.ColumnPath);
		string referenceSchemaName = lookupColumn.ReferenceSchema!.Name!;
		string caption = ExtractCaption(lookupColumn);
		SerializableExpression parameterExpression = new() {
			ExpressionType = EsqEntitySchemaQueryExpressionType.Parameter,
			ClassName = EsqFilterClassNames.ParameterExpression,
			Parameter = new SerializableParameter {
				DataValueType = EsqDataValueType.Lookup,
				Value = new LookupValueDto { Value = id, DisplayValue = null },
				ClassName = EsqFilterClassNames.Parameter
			}
		};
		return new SerializableFilter {
			ComparisonType = MapComparisonType(leaf.ComparisonType),
			FilterType = EsqFilterType.InFilter,
			IsEnabled = true,
			LeftExpression = new SerializableExpression {
				ColumnPath = NormalizeColumnPath(leaf.ColumnPath),
				ExpressionType = EsqEntitySchemaQueryExpressionType.SchemaColumn,
				ClassName = EsqFilterClassNames.ColumnExpression
			},
			IsAggregative = false,
			Key = string.Empty,
			DataValueType = EsqDataValueType.Lookup,
			LeftExpressionCaption = caption,
			ReferenceSchemaName = referenceSchemaName,
			RightExpressions = [parameterExpression],
			ClassName = EsqFilterClassNames.InFilter
		};
	}

	private static SerializableExpression BuildRightExpression(JsonElement? value, EsqDataValueType dataValueType) {
		if (!value.HasValue) {
			throw new InvalidOperationException("Filter leaf value is required for non-unary comparison.");
		}
		SerializableParameter parameter = dataValueType == EsqDataValueType.DateTime
			|| dataValueType == EsqDataValueType.Date
			|| dataValueType == EsqDataValueType.Time
				? BuildDateParameter(value.Value, dataValueType)
				: BuildScalarParameter(value.Value, dataValueType);
		return new SerializableExpression {
			ExpressionType = EsqEntitySchemaQueryExpressionType.Parameter,
			ClassName = EsqFilterClassNames.ParameterExpression,
			Parameter = parameter
		};
	}

	private static SerializableParameter BuildDateParameter(JsonElement value, EsqDataValueType dataValueType) {
		string raw = value.GetString() ?? string.Empty;
		return new SerializableParameter {
			DataValueType = dataValueType,
			Value = $"\"{raw}\"",
			DateValue = raw,
			ClassName = EsqFilterClassNames.Parameter
		};
	}

	private static SerializableParameter BuildScalarParameter(JsonElement value, EsqDataValueType dataValueType) {
		object? scalar = value.ValueKind switch {
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => MaterializeNumber(value, dataValueType),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			_ => null
		};
		return new SerializableParameter {
			DataValueType = dataValueType,
			Value = scalar,
			ClassName = EsqFilterClassNames.Parameter
		};
	}

	private static object MaterializeNumber(JsonElement value, EsqDataValueType dataValueType) {
		return dataValueType switch {
			EsqDataValueType.Integer => value.GetInt64(),
			EsqDataValueType.Float or EsqDataValueType.Money
				or EsqDataValueType.Float1 or EsqDataValueType.Float2
				or EsqDataValueType.Float3 or EsqDataValueType.Float4
				or EsqDataValueType.Float8 => value.GetDouble(),
			_ => value.GetDouble()
		};
	}

	private static Guid ParseLookupGuid(JsonElement? value, string columnPath) {
		if (value is null
			|| value.Value.ValueKind != JsonValueKind.String
			|| !Guid.TryParse(value.Value.GetString(), out Guid parsed)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupValueNotGuid,
				$"rule.actions[*].filter.filters[*].value",
				$"Lookup filter on '{columnPath}' requires a JSON string GUID value.");
		}
		return parsed;
	}

	private static EsqLogicalOperationStrict ParseLogicalOperation(string logicalOperation) =>
		string.Equals(logicalOperation, "AND", StringComparison.Ordinal)
			? EsqLogicalOperationStrict.And
			: EsqLogicalOperationStrict.Or;

	private static EsqFilterComparisonType MapComparisonType(string token) =>
		token?.ToUpperInvariant() switch {
			"EQUAL" => EsqFilterComparisonType.Equal,
			"NOT_EQUAL" => EsqFilterComparisonType.NotEqual,
			"LESS" => EsqFilterComparisonType.Less,
			"LESS_OR_EQUAL" => EsqFilterComparisonType.LessOrEqual,
			"GREATER" => EsqFilterComparisonType.Greater,
			"GREATER_OR_EQUAL" => EsqFilterComparisonType.GreaterOrEqual,
			"START_WITH" => EsqFilterComparisonType.StartWith,
			"NOT_START_WITH" => EsqFilterComparisonType.NotStartWith,
			"CONTAIN" => EsqFilterComparisonType.Contain,
			"NOT_CONTAIN" => EsqFilterComparisonType.NotContain,
			"END_WITH" => EsqFilterComparisonType.EndWith,
			"NOT_END_WITH" => EsqFilterComparisonType.NotEndWith,
			"IS_NULL" => EsqFilterComparisonType.IsNull,
			"IS_NOT_NULL" => EsqFilterComparisonType.IsNotNull,
			"EXISTS" => EsqFilterComparisonType.Exists,
			"NOT_EXISTS" => EsqFilterComparisonType.NotExists,
			_ => throw new InvalidOperationException($"Unsupported comparison token '{token}'.")
		};

	private static EsqDataValueType MapDataValueType(int? dataValueTypeId) {
		if (!dataValueTypeId.HasValue) {
			throw new InvalidOperationException("Column DataValueType is required.");
		}
		return (EsqDataValueType)dataValueTypeId.Value;
	}

	private static string ExtractCaption(EntitySchemaColumnDto column) {
		string fallback = column.Name ?? string.Empty;
		return column.Caption?
			.FirstOrDefault(caption => !string.IsNullOrWhiteSpace(caption?.Value))?
			.Value ?? fallback;
	}

	private EntitySchemaColumnDto ResolveLeafColumn(string columnPath, string rootSchemaName) {
		string[] segments = NormalizeColumnPath(columnPath).Split('.');
		IReadOnlyDictionary<string, EntitySchemaColumnDto> currentColumns =
			schemaProvider.GetSchemaColumns(rootSchemaName);
		string currentSchema = rootSchemaName;
		EntitySchemaColumnDto? lastResolved = null;
		for (int i = 0; i < segments.Length; i++) {
			string segment = segments[i];
			if (!currentColumns.TryGetValue(segment, out EntitySchemaColumnDto? column)) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.PathUnknown,
					$"rule.actions[*].filter.filters[*].columnPath",
					$"Column '{segment}' not found on schema '{currentSchema}' while resolving '{columnPath}'.");
			}
			lastResolved = column;
			bool isLastSegment = i == segments.Length - 1;
			if (isLastSegment) {
				break;
			}
			string typeName = BusinessRuleHelpers.MapDataValueTypeName(column.DataValueType);
			if (!string.Equals(typeName, "Lookup", StringComparison.Ordinal) || column.ReferenceSchema is null) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.PathUnknown,
					$"rule.actions[*].filter.filters[*].columnPath",
					$"Cannot traverse '{segment}' on '{currentSchema}': not a Lookup column.");
			}
			currentSchema = column.ReferenceSchema.Name!;
			currentColumns = schemaProvider.GetSchemaColumns(currentSchema);
		}
		return lastResolved!;
	}

	// CrtCopilot's NormalizeColumnPath strips a trailing 'Id' on each path segment because
	// the platform stores reference paths without the 'Id' suffix (e.g. 'Owner', not 'OwnerId').
	internal static string NormalizeColumnPath(string columnPath) {
		if (string.IsNullOrEmpty(columnPath)) {
			return columnPath;
		}
		string[] parts = columnPath.Split('.');
		for (int i = 0; i < parts.Length; i++) {
			string part = parts[i];
			Match backwardMatch = BackwardReferenceSyntax.Match(part);
			if (backwardMatch.Success) {
				string schema = backwardMatch.Groups["schema"].Value;
				string column = backwardMatch.Groups["column"].Value;
				if (column.EndsWith("Id", StringComparison.Ordinal)) {
					column = column[..^2];
				}
				parts[i] = $"[{schema}:{column}]";
			} else if (part.Length > 2 && part.EndsWith("Id", StringComparison.Ordinal)) {
				parts[i] = part[..^2];
			}
		}
		return string.Join('.', parts);
	}
}
