using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Validates the structural shape of a deserialized <see cref="StaticFilterGroup"/> without consulting any schema.
/// </summary>
internal static class StaticFilterStructuralValidator {

	private static readonly Regex BackwardReferenceShape = new(@"^\[[A-Za-z0-9_]+:[A-Za-z0-9_]+\]$",
		RegexOptions.Compiled);

	internal static void Validate(StaticFilterGroup group) => Validate(group, "filter");

	private static void Validate(StaticFilterGroup group, string path) {
		ValidateLogicalOperation(group.LogicalOperation, path);
		ValidateFilters(group, path);
		ValidateGroups(group, path);
		ValidateBackwardReferences(group, path);
	}

	private static void ValidateLogicalOperation(string logicalOperation, string path) {
		if (!string.Equals(logicalOperation, StaticFilterConstants.LogicalAnd, StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(logicalOperation, StaticFilterConstants.LogicalOr, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"{path}.logicalOperation: must be 'AND' or 'OR' (got '{logicalOperation}').");
		}
	}

	private static void ValidateFilters(StaticFilterGroup group, string path) {
		for (int i = 0; i < group.Filters.Count; i++) {
			ValidateLeaf(group.Filters[i], $"{path}.filters[{i}]");
		}
	}

	private static void ValidateGroups(StaticFilterGroup group, string path) {
		for (int i = 0; i < group.Groups.Count; i++) {
			Validate(group.Groups[i], $"{path}.groups[{i}]");
		}
	}

	private static void ValidateBackwardReferences(StaticFilterGroup group, string path) {
		for (int i = 0; i < group.BackwardReferenceFilters.Count; i++) {
			ValidateBackwardReference(group.BackwardReferenceFilters[i], $"{path}.backwardReferenceFilters[{i}]");
		}
	}

	private static void ValidateLeaf(StaticFilterLeaf leaf, string path) {
		if (string.IsNullOrWhiteSpace(leaf.ColumnPath)) {
			throw new ArgumentException($"{path}.columnPath: required.");
		}

		string comparison = leaf.ComparisonType.ToUpperInvariant();
		if (!StaticFilterConstants.AllLeafComparisons.Contains(comparison)) {
			throw new ArgumentException(
				$"{path}.comparisonType: unsupported value '{leaf.ComparisonType}'. Supported: EQUAL, NOT_EQUAL, IS_NULL, IS_NOT_NULL, GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL, CONTAIN, NOT_CONTAIN, START_WITH, NOT_START_WITH, END_WITH, NOT_END_WITH.");
		}

		bool isUnary = StaticFilterConstants.UnaryComparisons.Contains(comparison);
		if (isUnary) {
			if (leaf.Value.HasValue && leaf.Value.Value.ValueKind != JsonValueKind.Null) {
				throw new ArgumentException($"{path}.value: must be omitted when comparisonType is '{comparison}'.");
			}

			return;
		}

		if (!leaf.Value.HasValue || leaf.Value.Value.ValueKind == JsonValueKind.Null) {
			throw new ArgumentException($"{path}.value: required when comparisonType is '{comparison}'.");
		}

		if (leaf.Value.Value.ValueKind == JsonValueKind.Array) {
			ValidateArrayValue(leaf.Value.Value, comparison, path);
		}
	}

	private static void ValidateArrayValue(JsonElement array, string comparison, string path) {
		if (!StaticFilterConstants.EqualityComparisons.Contains(comparison)) {
			throw new ArgumentException(
				$"{path}.value: array values are only supported when comparisonType is EQUAL or NOT_EQUAL (multi-value IN on Lookup).");
		}

		int index = 0;
		foreach (JsonElement item in array.EnumerateArray()) {
			if (item.ValueKind != JsonValueKind.String) {
				throw new ArgumentException($"{path}.value[{index}]: must be a JSON string (GUID or display name).");
			}

			index++;
		}

		if (index == 0) {
			throw new ArgumentException($"{path}.value: array must contain at least one element.");
		}
	}

	private static void ValidateBackwardReference(StaticFilterBackwardReference reference, string path) {
		if (string.IsNullOrWhiteSpace(reference.ReferenceColumnPath)) {
			throw new ArgumentException($"{path}.referenceColumnPath: required.");
		}

		if (!BackwardReferenceShape.IsMatch(reference.ReferenceColumnPath)) {
			throw new ArgumentException(
				$"{path}.referenceColumnPath: must use shape '[Schema:Column]' (got '{reference.ReferenceColumnPath}').");
		}

		string comparison = reference.ComparisonType.ToUpperInvariant();
		if (!StaticFilterConstants.BackwardReferenceComparisons.Contains(comparison)) {
			throw new ArgumentException(
				$"{path}.comparisonType: backward references support only EXISTS or NOT_EXISTS in this iteration (got '{reference.ComparisonType}').");
		}

		if (reference.Filter is not null) {
			Validate(reference.Filter, $"{path}.filter");
		}
	}
}
