using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Structural validator for the static filter contract on apply-static-filter actions.
/// Checks invariants that do not require access to the entity schema or DataService —
/// logical-operation tokens, supported comparison tokens, presence of leaf value for
/// binary comparisons, and well-formed backward-reference shape. Schema-aware validation
/// (path resolution, datatype compatibility, lookup-record existence, backward-reference
/// cardinality) is delegated to the server-side ESQ converter and surfaces here as
/// <see cref="BusinessRuleFilterErrorCodes.ServerRejected"/>.
/// </summary>
internal static class StaticFilterStructuralValidator {

	internal const string DefaultFieldPathPrefix = "rule.actions[*].filter";

	internal static readonly IReadOnlySet<string> SupportedComparisonTokens =
		new HashSet<string>(StringComparer.Ordinal) {
			"EQUAL", "NOT_EQUAL",
			"GREATER", "GREATER_OR_EQUAL", "LESS", "LESS_OR_EQUAL",
			"IS_NULL", "IS_NOT_NULL",
			"START_WITH", "NOT_START_WITH",
			"CONTAIN", "NOT_CONTAIN",
			"END_WITH", "NOT_END_WITH"
		};

	private static readonly IReadOnlySet<string> UnaryComparisons =
		new HashSet<string>(StringComparer.Ordinal) { "IS_NULL", "IS_NOT_NULL" };

	internal static void Validate(StaticFilterGroup filter, string fieldPathPrefix = DefaultFieldPathPrefix) {
		if (filter is null) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.FilterRequired,
				fieldPathPrefix,
				"filter group is required.");
		}
		ValidateGroup(filter, fieldPathPrefix);
	}

	private static void ValidateGroup(StaticFilterGroup group, string fieldPath) {
		if (group is null) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.FilterRequired,
				fieldPath,
				"filter group is required.");
		}
		ValidateLogicalOperation(group.LogicalOperation, fieldPath + ".logicalOperation");

		if (group.Filters is not null) {
			for (int i = 0; i < group.Filters.Count; i++) {
				ValidateLeaf(group.Filters[i], $"{fieldPath}.filters[{i}]");
			}
		}

		if (group.BackwardReferenceFilters is not null) {
			for (int i = 0; i < group.BackwardReferenceFilters.Count; i++) {
				ValidateBackwardReference(
					group.BackwardReferenceFilters[i],
					$"{fieldPath}.backwardReferenceFilters[{i}]");
			}
		}
		if (group.Groups is not null) {
			for (int i = 0; i < group.Groups.Count; i++) {
				if (group.Groups[i] is null) {
					throw new BusinessRuleFilterException(
						BusinessRuleFilterErrorCodes.FilterRequired,
						$"{fieldPath}.groups[{i}]",
						"nested filter group is required.");
				}
				ValidateGroup(group.Groups[i], $"{fieldPath}.groups[{i}]");
			}
		}
	}

	private static void ValidateLeaf(StaticFilterLeaf leaf, string fieldPath) {
		if (leaf is null) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.FilterRequired,
				fieldPath,
				"leaf filter is required.");
		}
		if (string.IsNullOrWhiteSpace(leaf.ColumnPath)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.PathUnknown,
				fieldPath + ".columnPath",
				"columnPath is required.");
		}
		if (string.IsNullOrWhiteSpace(leaf.ComparisonType)
			|| !SupportedComparisonTokens.Contains(leaf.ComparisonType)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonUnknown,
				fieldPath + ".comparisonType",
				$"Unsupported comparisonType '{leaf.ComparisonType}'. See guidance for the supported list.");
		}
		if (UnaryComparisons.Contains(leaf.ComparisonType)) {
			return;
		}
		if (!leaf.Value.HasValue
			|| leaf.Value.Value.ValueKind == JsonValueKind.Null
			|| leaf.Value.Value.ValueKind == JsonValueKind.Undefined) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ValueRequired,
				fieldPath + ".value",
				"leaf value is required for binary comparison.");
		}
	}

	internal static readonly IReadOnlySet<string> SupportedAggregationTypes =
		new HashSet<string>(StringComparer.Ordinal) {
			"EXISTS", "NOT_EXISTS", "COUNT", "SUM", "AVG", "MIN", "MAX"
		};

	private static readonly IReadOnlySet<string> ExistsAggregationTypes =
		new HashSet<string>(StringComparer.Ordinal) { "EXISTS", "NOT_EXISTS" };

	private static readonly IReadOnlySet<string> ValueAggregationTypes =
		new HashSet<string>(StringComparer.Ordinal) { "COUNT", "SUM", "AVG", "MIN", "MAX" };

	private static readonly IReadOnlySet<string> ColumnPathRequiredAggregationTypes =
		new HashSet<string>(StringComparer.Ordinal) { "SUM", "AVG", "MIN", "MAX" };

	private static void ValidateBackwardReference(StaticFilterBackwardReference brf, string fieldPath) {
		if (brf is null) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				fieldPath,
				"backward reference filter is required.");
		}
		if (string.IsNullOrWhiteSpace(brf.ReferenceColumnPath)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				fieldPath + ".referenceColumnPath",
				"referenceColumnPath is required for a backward-reference filter.");
		}
		ValidateGroup(brf.Filter, fieldPath + ".filter");
		ValidateAggregationFields(brf, fieldPath);
	}

	private static void ValidateAggregationFields(StaticFilterBackwardReference brf, string fieldPath) {
		// EXISTS (implicit when aggregationType is omitted): all aggregation fields must be absent.
		if (string.IsNullOrWhiteSpace(brf.AggregationType)) {
			RejectFieldWhenAggregationTypeIsExists(brf.ComparisonType, fieldPath + ".comparisonType", "comparisonType");
			RejectFieldWhenAggregationTypeIsExists(brf.AggregationColumnPath, fieldPath + ".aggregationColumnPath", "aggregationColumnPath");
			RejectValueWhenAggregationTypeIsExists(brf.AggregationValue, fieldPath + ".aggregationValue");
			return;
		}
		string aggregationType = brf.AggregationType;
		if (!SupportedAggregationTypes.Contains(aggregationType)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonUnknown,
				fieldPath + ".aggregationType",
				$"Unsupported aggregationType '{aggregationType}'. Supported: EXISTS, NOT_EXISTS, COUNT, SUM, AVG, MIN, MAX.");
		}
		if (ExistsAggregationTypes.Contains(aggregationType)) {
			RejectFieldWhenAggregationTypeIsExists(brf.ComparisonType, fieldPath + ".comparisonType", "comparisonType");
			RejectFieldWhenAggregationTypeIsExists(brf.AggregationColumnPath, fieldPath + ".aggregationColumnPath", "aggregationColumnPath");
			RejectValueWhenAggregationTypeIsExists(brf.AggregationValue, fieldPath + ".aggregationValue");
			return;
		}
		// COUNT/SUM/AVG/MIN/MAX: comparisonType + aggregationValue are mandatory; aggregationColumnPath
		// is mandatory for SUM/AVG/MIN/MAX (the column being aggregated) and ignored for COUNT.
		if (string.IsNullOrWhiteSpace(brf.ComparisonType)
			|| !SupportedComparisonTokens.Contains(brf.ComparisonType)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonUnknown,
				fieldPath + ".comparisonType",
				$"comparisonType is required for aggregation '{aggregationType}' and must be one of the 14 supported tokens.");
		}
		if (UnaryComparisons.Contains(brf.ComparisonType)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonUnknown,
				fieldPath + ".comparisonType",
				$"Unary comparison '{brf.ComparisonType}' is not allowed on aggregation '{aggregationType}'; use a binary token (EQUAL, GREATER, ...).");
		}
		if (!brf.AggregationValue.HasValue
			|| brf.AggregationValue.Value.ValueKind == JsonValueKind.Null
			|| brf.AggregationValue.Value.ValueKind == JsonValueKind.Undefined) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ValueRequired,
				fieldPath + ".aggregationValue",
				$"aggregationValue is required for aggregation '{aggregationType}'.");
		}
		if (ColumnPathRequiredAggregationTypes.Contains(aggregationType)
			&& string.IsNullOrWhiteSpace(brf.AggregationColumnPath)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.PathUnknown,
				fieldPath + ".aggregationColumnPath",
				$"aggregationColumnPath is required for aggregation '{aggregationType}' (column being aggregated on the child schema).");
		}
	}

	private static void RejectFieldWhenAggregationTypeIsExists(string? value, string fieldPath, string fieldName) {
		if (!string.IsNullOrWhiteSpace(value)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonUnknown,
				fieldPath,
				$"{fieldName} must not be provided when aggregationType is EXISTS/NOT_EXISTS (or omitted).");
		}
	}

	private static void RejectValueWhenAggregationTypeIsExists(JsonElement? value, string fieldPath) {
		if (value.HasValue
			&& value.Value.ValueKind != JsonValueKind.Null
			&& value.Value.ValueKind != JsonValueKind.Undefined) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ValueShape,
				fieldPath,
				"aggregationValue must not be provided when aggregationType is EXISTS/NOT_EXISTS (or omitted).");
		}
	}

	private static void ValidateLogicalOperation(string logicalOperation, string fieldPath) {
		if (string.Equals(logicalOperation, "AND", StringComparison.Ordinal)
			|| string.Equals(logicalOperation, "OR", StringComparison.Ordinal)) {
			return;
		}
		throw new BusinessRuleFilterException(
			BusinessRuleFilterErrorCodes.LogicalOperationUnknown,
			fieldPath,
			$"Unsupported logicalOperation '{logicalOperation}'. Use 'AND' or 'OR'.");
	}
}
