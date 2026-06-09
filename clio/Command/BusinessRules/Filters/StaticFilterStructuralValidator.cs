using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Validates the structural shape of a deserialized <see cref="StaticFilterGroup"/> without consulting any schema.
/// </summary>
internal static class StaticFilterStructuralValidator {

	private static readonly Regex BackwardReferenceShape = new(@"^\[[A-Za-z0-9_]+:[A-Za-z0-9_]+\]$",
		RegexOptions.Compiled,
		TimeSpan.FromMilliseconds(100));

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

		bool hasValue = leaf.Value.HasValue && leaf.Value.Value.ValueKind != JsonValueKind.Null;
		bool hasMacros = !string.IsNullOrWhiteSpace(leaf.ValueMacros);
		bool hasDatePart = !string.IsNullOrWhiteSpace(leaf.DatePart);

		bool isUnary = StaticFilterConstants.UnaryComparisons.Contains(comparison);
		if (isUnary) {
			if (hasValue || hasMacros || hasDatePart) {
				throw new ArgumentException($"{path}: value, valueMacros and datePart must be omitted when comparisonType is '{comparison}'.");
			}

			return;
		}

		if (hasDatePart) {
			ValidateDatePart(leaf, comparison, hasValue, hasMacros, path);
			return;
		}

		if (hasValue && hasMacros) {
			throw new ArgumentException($"{path}: provide either value or valueMacros, not both.");
		}

		if (!hasValue && !hasMacros) {
			throw new ArgumentException($"{path}: value or valueMacros is required when comparisonType is '{comparison}'.");
		}

		if (hasMacros) {
			ValidateMacros(leaf, comparison, path);
			return;
		}

		if (leaf.Value!.Value.ValueKind == JsonValueKind.Array) {
			ValidateArrayValue(leaf.Value.Value, comparison, path);
		}
	}

	private static void ValidateMacros(StaticFilterLeaf leaf, string comparison, string path) {
		if (!MacrosCatalog.TryResolve(leaf.ValueMacros!, out _, out _, out bool requiresArgument)) {
			throw new ArgumentException(
				$"{path}.valueMacros: unknown macros '{leaf.ValueMacros}'. Supported: {string.Join(", ", MacrosCatalog.KnownNames)}.");
		}

		if (StaticFilterConstants.TextComparisons.Contains(comparison)) {
			throw new ArgumentException(
				$"{path}: valueMacros is not supported with text comparison '{comparison}'.");
		}

		if (requiresArgument && !leaf.ValueMacrosArgument.HasValue) {
			throw new ArgumentException(
				$"{path}.valueMacrosArgument: required (positive integer) for macros '{leaf.ValueMacros}'.");
		}

		if (!requiresArgument && leaf.ValueMacrosArgument.HasValue) {
			throw new ArgumentException(
				$"{path}.valueMacrosArgument: must be omitted for macros '{leaf.ValueMacros}'.");
		}

		if (leaf.ValueMacrosArgument is <= 0) {
			throw new ArgumentException(
				$"{path}.valueMacrosArgument: must be a positive integer.");
		}
	}

	private static void ValidateDatePart(StaticFilterLeaf leaf, string comparison, bool hasValue, bool hasMacros, string path) {
		if (hasMacros) {
			throw new ArgumentException($"{path}: datePart and valueMacros are mutually exclusive.");
		}

		if (!hasValue) {
			throw new ArgumentException($"{path}.value: required when datePart is set (the part is compared to a constant).");
		}

		if (StaticFilterConstants.TextComparisons.Contains(comparison)) {
			throw new ArgumentException(
				$"{path}: datePart supports only equality/relational comparisons (EQUAL, NOT_EQUAL, GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL); got '{comparison}'.");
		}

		if (!DatePartCatalog.TryResolve(leaf.DatePart!, out _, out DatePartCatalog.DatePartValueKind valueKind)) {
			throw new ArgumentException(
				$"{path}.datePart: unknown date part '{leaf.DatePart}'. Supported: {string.Join(", ", DatePartCatalog.KnownNames)}.");
		}

		JsonElement value = leaf.Value!.Value;
		if (value.ValueKind == JsonValueKind.Array) {
			throw new ArgumentException($"{path}.value: array values are not supported with datePart.");
		}

		switch (valueKind) {
			case DatePartCatalog.DatePartValueKind.Integer when value.ValueKind != JsonValueKind.Number:
				throw new ArgumentException(
					$"{path}.value: datePart '{leaf.DatePart}' expects a JSON integer (e.g. 2021 for Year, 14 for Day, 11 for Hour).");
			case DatePartCatalog.DatePartValueKind.Time when value.ValueKind != JsonValueKind.String:
				throw new ArgumentException(
					$"{path}.value: datePart '{leaf.DatePart}' expects a JSON time-of-day string (e.g. \"11:06\" or \"11:06:00\").");
			default:
				return;
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

		if (string.IsNullOrWhiteSpace(reference.AggregationType)) {
			ValidateExistsBackwardReference(reference, path);
		} else {
			ValidateAggregationBackwardReference(reference, path);
		}

		if (reference.Filter is not null) {
			Validate(reference.Filter, $"{path}.filter");
		}
	}

	private static void ValidateExistsBackwardReference(StaticFilterBackwardReference reference, string path) {
		string comparison = reference.ComparisonType.ToUpperInvariant();
		if (!StaticFilterConstants.BackwardReferenceComparisons.Contains(comparison)) {
			throw new ArgumentException(
				$"{path}.comparisonType: backward references without aggregationType support only EXISTS or NOT_EXISTS (got '{reference.ComparisonType}').");
		}

		if (reference.AggregationColumnPath is not null || reference.AggregationValue.HasValue) {
			throw new ArgumentException(
				$"{path}: aggregationColumnPath and aggregationValue are only allowed when aggregationType is set.");
		}
	}

	private static void ValidateAggregationBackwardReference(StaticFilterBackwardReference reference, string path) {
		string aggregation = reference.AggregationType!.ToUpperInvariant();
		if (!StaticFilterConstants.AggregationTypes.Contains(aggregation)) {
			throw new ArgumentException(
				$"{path}.aggregationType: unsupported value '{reference.AggregationType}'. Supported: COUNT, SUM, AVG, MIN, MAX.");
		}

		string comparison = reference.ComparisonType.ToUpperInvariant();
		if (!StaticFilterConstants.AggregationComparisons.Contains(comparison)) {
			throw new ArgumentException(
				$"{path}.comparisonType: aggregation backward references require a relational/equality token (EQUAL, NOT_EQUAL, GREATER, GREATER_OR_EQUAL, LESS, LESS_OR_EQUAL); got '{reference.ComparisonType}'.");
		}

		if (!reference.AggregationValue.HasValue) {
			throw new ArgumentException(
				$"{path}.aggregationValue: required number when aggregationType is set (e.g. COUNT GREATER 10).");
		}

		bool isScalar = StaticFilterConstants.ScalarAggregationTypes.Contains(aggregation);
		if (isScalar && string.IsNullOrWhiteSpace(reference.AggregationColumnPath)) {
			throw new ArgumentException(
				$"{path}.aggregationColumnPath: required for {aggregation} (the numeric child column to aggregate).");
		}

		if (!isScalar && !string.IsNullOrWhiteSpace(reference.AggregationColumnPath)) {
			throw new ArgumentException(
				$"{path}.aggregationColumnPath: must be omitted for COUNT.");
		}
	}
}
