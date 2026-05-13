using System;
using System.Collections.Generic;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Friendly-filter structural validator. Only checks invariants that do NOT require
/// access to the entity schema or the DataService — logical-operation tokens, supported
/// comparison tokens, presence of leaf value for binary comparisons, well-formed
/// backward-reference shape. Schema-aware validation (path resolution, datatype
/// compatibility, lookup-record existence, backward-reference cardinality) is delegated
/// to the server-side <c>LlmEsqConverterService.ConvertToEsqFilters</c> endpoint and
/// surfaces here as <see cref="BusinessRuleFilterErrorCodes.ServerRejected"/>.
/// </summary>
internal static class LlmFiltersValidator {

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

	internal static void Validate(FriendlyFilterGroup filter, string fieldPathPrefix = DefaultFieldPathPrefix) {
		if (filter is null) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.FilterRequired,
				fieldPathPrefix,
				"filter group is required.");
		}
		ValidateGroup(filter, fieldPathPrefix);
	}

	private static void ValidateGroup(FriendlyFilterGroup group, string fieldPath) {
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
	}

	private static void ValidateLeaf(FriendlyFilterLeaf leaf, string fieldPath) {
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
			|| leaf.Value.Value.ValueKind == System.Text.Json.JsonValueKind.Null
			|| leaf.Value.Value.ValueKind == System.Text.Json.JsonValueKind.Undefined) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ValueRequired,
				fieldPath + ".value",
				"leaf value is required for binary comparison.");
		}
	}

	private static void ValidateBackwardReference(BackwardReferenceFilter brf, string fieldPath) {
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
