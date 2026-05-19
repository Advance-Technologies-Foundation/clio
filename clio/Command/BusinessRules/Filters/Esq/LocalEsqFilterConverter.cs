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
internal sealed class LocalEsqFilterConverter(
	IFilterSchemaProvider schemaProvider,
	ILookupValueResolver? lookupValueResolver = null) {

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
		IReadOnlyList<StaticFilterGroup> nestedGroups = group.Groups ?? [];
		for (int i = 0; i < nestedGroups.Count; i++) {
			envelope.Items.Add(
				$"Group_{i}",
				BuildNestedGroup(nestedGroups[i], rootSchemaName, (i + 1) * 1000));
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
		IReadOnlyList<StaticFilterGroup> nestedGroups = group.Groups ?? [];
		for (int i = 0; i < nestedGroups.Count; i++) {
			items.Add(
				$"Group_{parentIndex}_{i}",
				BuildNestedGroup(nestedGroups[i], schemaName, parentIndex + (i + 1) * 1000));
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
		// Resolve column AND rewrite each path segment from UId (GUID-shape) to Name.
		// The platform BVE1 runtime reads `columnPath` strings by Name, not by UId; callers may
		// legitimately pass GUIDs so the friendly contract stays explicit about which column is
		// referenced even when names clash.
		(EntitySchemaColumnDto leafColumn, string nameOnlyPath) =
			ResolveLeafColumnWithNameOnlyPath(leaf.ColumnPath, schemaName);
		StaticFilterLeaf normalizedLeaf = leaf with { ColumnPath = nameOnlyPath };
		string comparison = leaf.ComparisonType?.ToUpperInvariant() ?? string.Empty;
		if (string.Equals(comparison, "IS_NULL", StringComparison.Ordinal)) {
			return BuildNullFilter(nameOnlyPath, isNull: true);
		}
		if (string.Equals(comparison, "IS_NOT_NULL", StringComparison.Ordinal)) {
			return BuildNullFilter(nameOnlyPath, isNull: false);
		}
		string leafTypeName = BusinessRuleHelpers.MapDataValueTypeName(leafColumn.DataValueType);
		bool isLookupLeaf = string.Equals(leafTypeName, "Lookup", StringComparison.Ordinal)
			&& leafColumn.ReferenceSchema is not null;
		if (isLookupLeaf) {
			return BuildLookupFilter(normalizedLeaf, leafColumn);
		}
		// Relative-date macro values (e.g. "PREVIOUS_WEEK", "WITHIN_PREV_DAYS(7)", "DAY_OF_WEEK(2)")
		// route to the macro builder when the column is Date / DateTime / Time. The resulting
		// filter is still serialized as a static envelope; the platform runtime evaluates the
		// macro against time-of-fire.
		bool isTemporalLeaf = string.Equals(leafTypeName, "DateTime", StringComparison.Ordinal)
			|| string.Equals(leafTypeName, "Date", StringComparison.Ordinal)
			|| string.Equals(leafTypeName, "Time", StringComparison.Ordinal);
		if (isTemporalLeaf && EsqMacroBuilder.IsMacroValue(normalizedLeaf.Value)) {
			return EsqMacroBuilder.Build(normalizedLeaf, leafColumn);
		}
		return BuildCompareFilter(normalizedLeaf, leafColumn);
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

	private SerializableFilter BuildLookupFilter(StaticFilterLeaf leaf, EntitySchemaColumnDto lookupColumn) {
		string referenceSchemaName = lookupColumn.ReferenceSchema!.Name!;
		Guid[] ids = ParseLookupGuids(leaf.Value, leaf.ColumnPath, referenceSchemaName);
		string caption = ExtractCaption(lookupColumn);
		SerializableExpression[] parameterExpressions = new SerializableExpression[ids.Length];
		for (int i = 0; i < ids.Length; i++) {
			parameterExpressions[i] = new SerializableExpression {
				ExpressionType = EsqEntitySchemaQueryExpressionType.Parameter,
				ClassName = EsqFilterClassNames.ParameterExpression,
				Parameter = new SerializableParameter {
					DataValueType = EsqDataValueType.Lookup,
					Value = new LookupValueDto { Value = ids[i], DisplayValue = null },
					ClassName = EsqFilterClassNames.Parameter
				}
			};
		}
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
			RightExpressions = parameterExpressions,
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

	private Guid[] ParseLookupGuids(JsonElement? value, string columnPath, string referenceSchemaName) {
		string fieldPath = "rule.actions[*].filter.filters[*].value";
		if (value is null) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupValueNotGuid,
				fieldPath,
				$"Lookup filter on '{columnPath}' requires a JSON string GUID value (or a JSON array of GUID strings).");
		}
		JsonElement raw = value.Value;
		if (raw.ValueKind == JsonValueKind.Array) {
			int length = raw.GetArrayLength();
			if (length == 0) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.LookupValueNotGuid,
					fieldPath,
					$"Lookup filter on '{columnPath}' requires a non-empty array of GUID strings.");
			}
			Guid[] result = new Guid[length];
			int i = 0;
			foreach (JsonElement element in raw.EnumerateArray()) {
				result[i] = ResolveSingleLookupValue(element, columnPath, referenceSchemaName,
					$"{fieldPath}[{i}]");
				i++;
			}
			return result;
		}
		return [ResolveSingleLookupValue(raw, columnPath, referenceSchemaName, fieldPath)];
	}

	private Guid ResolveSingleLookupValue(
		JsonElement element,
		string columnPath,
		string referenceSchemaName,
		string fieldPath) {
		if (element.ValueKind != JsonValueKind.String) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupValueNotGuid,
				fieldPath,
				$"Lookup filter on '{columnPath}' requires a JSON string GUID value (or a display name when display-name resolution is enabled); got JSON {element.ValueKind}.");
		}
		string text = element.GetString() ?? string.Empty;
		if (Guid.TryParse(text, out Guid parsed)) {
			return parsed;
		}
		// Non-GUID string -> try display-name resolution when a resolver is wired; otherwise reject.
		if (lookupValueResolver is null) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupValueNotGuid,
				fieldPath,
				$"Lookup filter on '{columnPath}' requires a JSON string GUID value. " +
				$"To pass the display name '{text}', wire ILookupValueResolver on the converter so the value is resolved against the lookup's primary display column.");
		}
		return lookupValueResolver.Resolve(referenceSchemaName, text, fieldPath);
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

	private (EntitySchemaColumnDto Column, string NameOnlyPath) ResolveLeafColumnWithNameOnlyPath(
		string columnPath,
		string rootSchemaName) {
		string[] segments = NormalizeColumnPath(columnPath).Split('.');
		IReadOnlyDictionary<string, EntitySchemaColumnDto> currentColumns =
			schemaProvider.GetSchemaColumns(rootSchemaName);
		string currentSchema = rootSchemaName;
		EntitySchemaColumnDto? lastResolved = null;
		for (int i = 0; i < segments.Length; i++) {
			string segment = segments[i];
			if (!BusinessRuleHelpers.TryResolveColumnByNameOrUId(currentColumns, segment, out EntitySchemaColumnDto? column)) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.PathUnknown,
					$"rule.actions[*].filter.filters[*].columnPath",
					$"Column '{segment}' not found on schema '{currentSchema}' while resolving '{columnPath}' (looked up by Name and UId).");
			}
			// Rewrite UId-form segments to their canonical Name so the emitted BVE1 envelope
			// carries platform-native path strings the runtime can resolve without UId mapping.
			segments[i] = !string.IsNullOrWhiteSpace(column!.Name) ? column.Name : segment;
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
		return (lastResolved!, string.Join('.', segments));
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
