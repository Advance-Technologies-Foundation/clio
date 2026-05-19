using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Validates a static filter against live schema metadata pulled through
/// <see cref="IFilterSchemaProvider"/>. Runs AFTER <see cref="StaticFilterStructuralValidator"/>
/// has accepted the structural shape (logical operator, comparison token, unary/binary value
/// rule, backward-reference shape) — this class adds path resolution, traversal-through-lookup
/// invariants, backward-reference 1:N cardinality, and datatype compatibility for leaf values.
/// </summary>
internal sealed class SchemaAwareFilterValidator(IFilterSchemaProvider schemaProvider) {

	private static readonly Regex BackwardReferenceSyntax =
		new(@"^\[(?<schema>[A-Za-z_][A-Za-z0-9_]*):(?<column>[A-Za-z_][A-Za-z0-9_]*)\]$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant,
			TimeSpan.FromMilliseconds(100));

	private static readonly IReadOnlySet<string> UnaryFilterComparisons =
		new HashSet<string>(StringComparer.Ordinal) { "IS_NULL", "IS_NOT_NULL" };

	private static readonly IReadOnlySet<string> RelationalFilterComparisons =
		new HashSet<string>(StringComparer.Ordinal) {
			"GREATER", "GREATER_OR_EQUAL", "LESS", "LESS_OR_EQUAL"
		};

	private static readonly IReadOnlySet<string> StringMatchFilterComparisons =
		new HashSet<string>(StringComparer.Ordinal) {
			"START_WITH", "NOT_START_WITH",
			"CONTAIN", "NOT_CONTAIN",
			"END_WITH", "NOT_END_WITH"
		};

	public void Validate(StaticFilterGroup filter, string rootSchemaName, string fieldPathPrefix) {
		ArgumentNullException.ThrowIfNull(filter);
		ArgumentException.ThrowIfNullOrWhiteSpace(rootSchemaName);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> rootColumns =
			schemaProvider.GetSchemaColumns(rootSchemaName);
		ValidateGroup(filter, rootSchemaName, rootColumns, fieldPathPrefix);
	}

	private void ValidateGroup(
		StaticFilterGroup group,
		string schemaName,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columns,
		string fieldPath) {
		if (group.Filters is not null) {
			for (int i = 0; i < group.Filters.Count; i++) {
				ValidateLeaf(group.Filters[i], schemaName, columns, $"{fieldPath}.filters[{i}]");
			}
		}
		if (group.BackwardReferenceFilters is not null) {
			for (int i = 0; i < group.BackwardReferenceFilters.Count; i++) {
				ValidateBackwardReference(
					group.BackwardReferenceFilters[i],
					schemaName,
					$"{fieldPath}.backwardReferenceFilters[{i}]");
			}
		}
		if (group.Groups is not null) {
			for (int i = 0; i < group.Groups.Count; i++) {
				// Nested groups operate on the same schema as their parent.
				ValidateGroup(group.Groups[i], schemaName, columns, $"{fieldPath}.groups[{i}]");
			}
		}
	}

	private void ValidateLeaf(
		StaticFilterLeaf leaf,
		string schemaName,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columns,
		string fieldPath) {
		EntitySchemaColumnDto column = ResolveColumnPath(
			leaf.ColumnPath,
			schemaName,
			columns,
			$"{fieldPath}.columnPath");
		if (UnaryFilterComparisons.Contains(leaf.ComparisonType)) {
			// Structural validator already enforced no value for unary; nothing left to check.
			return;
		}
		// Multi-value (JSON array) is only meaningful for IN-style equality on Lookup columns.
		// The platform's InFilter treats EQUAL with multiple parameters as IN and NOT_EQUAL as NOT IN;
		// other tokens (GREATER, LESS, CONTAIN, etc.) have no IN semantics.
		if (leaf.Value.HasValue && leaf.Value.Value.ValueKind == JsonValueKind.Array) {
			string typeName = BusinessRuleHelpers.MapDataValueTypeName(column.DataValueType);
			if (!string.Equals(typeName, "Lookup", StringComparison.Ordinal)) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.ValueShape,
					$"{fieldPath}.value",
					$"Array values are only supported for Lookup columns (IN / NOT IN semantics); column '{column.Name}' is {typeName}.");
			}
			if (!string.Equals(leaf.ComparisonType, "EQUAL", StringComparison.Ordinal)
				&& !string.Equals(leaf.ComparisonType, "NOT_EQUAL", StringComparison.Ordinal)) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype,
					$"{fieldPath}.comparisonType",
					$"Array values on Lookup columns are only supported with EQUAL (IN) or NOT_EQUAL (NOT IN); got '{leaf.ComparisonType}'.");
			}
		}
		ValidateComparisonForDatatype(leaf.ComparisonType, column, $"{fieldPath}.comparisonType");
		ValidateValueDatatype(leaf.Value, column, $"{fieldPath}.value");
	}

	private EntitySchemaColumnDto ResolveColumnPath(
		string columnPath,
		string rootSchemaName,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> rootColumns,
		string fieldPath) {
		string[] segments = columnPath.Split('.');
		IReadOnlyDictionary<string, EntitySchemaColumnDto> currentColumns = rootColumns;
		string currentSchema = rootSchemaName;
		EntitySchemaColumnDto? lastResolved = null;
		for (int i = 0; i < segments.Length; i++) {
			string segment = segments[i];
			if (!BusinessRuleHelpers.TryResolveColumnByNameOrUId(currentColumns, segment, out EntitySchemaColumnDto? column)) {
				string sample = string.Join(", ", currentColumns.Keys.Take(20));
				string ellipsis = currentColumns.Count > 20 ? ", ..." : string.Empty;
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.PathUnknown,
					fieldPath,
					$"Column '{segment}' not found on schema '{currentSchema}' (looked up by Name and UId). Available names: {sample}{ellipsis}.");
			}
			lastResolved = column!;
			bool isLastSegment = i == segments.Length - 1;
			if (isLastSegment) {
				break;
			}
			string typeName = BusinessRuleHelpers.MapDataValueTypeName(column.DataValueType);
			if (!string.Equals(typeName, "Lookup", StringComparison.Ordinal)
				|| column.ReferenceSchema is null
				|| string.IsNullOrWhiteSpace(column.ReferenceSchema.Name)) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.PathUnknown,
					fieldPath,
					$"Cannot traverse '{segment}' on schema '{currentSchema}': column is not a Lookup with a reference schema.");
			}
			currentSchema = column.ReferenceSchema.Name;
			currentColumns = schemaProvider.GetSchemaColumns(currentSchema);
		}
		return lastResolved!;
	}

	private void ValidateBackwardReference(
		StaticFilterBackwardReference brf,
		string parentSchemaName,
		string fieldPath) {
		Match match = BackwardReferenceSyntax.Match(brf.ReferenceColumnPath);
		if (!match.Success) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				fieldPath + ".referenceColumnPath",
				$"Expected '[ChildSchema:ColumnOnChild]' syntax for backward reference; got '{brf.ReferenceColumnPath}'.");
		}
		string childSchemaName = match.Groups["schema"].Value;
		string childColumnName = match.Groups["column"].Value;
		IReadOnlyDictionary<string, EntitySchemaColumnDto> childColumns =
			schemaProvider.GetSchemaColumns(childSchemaName);
		if (!childColumns.TryGetValue(childColumnName, out EntitySchemaColumnDto? childColumn)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.PathUnknown,
				fieldPath + ".referenceColumnPath",
				$"Column '{childColumnName}' not found on child schema '{childSchemaName}'.");
		}
		string childColumnTypeName = BusinessRuleHelpers.MapDataValueTypeName(childColumn.DataValueType);
		if (!string.Equals(childColumnTypeName, "Lookup", StringComparison.Ordinal)
			|| childColumn.ReferenceSchema is null
			|| !string.Equals(childColumn.ReferenceSchema.Name, parentSchemaName, StringComparison.Ordinal)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.BackwardReferenceNot1N,
				fieldPath + ".referenceColumnPath",
				$"Column '{childColumnName}' on schema '{childSchemaName}' does not reference '{parentSchemaName}'. " +
				"Backward reference requires a 1:N relationship where the child column points to the parent schema.");
		}
		ValidateGroup(brf.Filter, childSchemaName, childColumns, fieldPath + ".filter");
		ValidateAggregation(brf, childSchemaName, childColumns, fieldPath);
	}

	private static void ValidateAggregation(
		StaticFilterBackwardReference brf,
		string childSchemaName,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> childColumns,
		string fieldPath) {
		// Structural validator already enforced presence/absence cross-rules; here only schema-aware
		// checks: aggregationColumnPath exists on child + numeric for SUM/AVG/MIN/MAX, aggregationValue
		// type matches the aggregated column (or Integer for COUNT).
		if (string.IsNullOrWhiteSpace(brf.AggregationType)
			|| string.Equals(brf.AggregationType, "EXISTS", StringComparison.Ordinal)
			|| string.Equals(brf.AggregationType, "NOT_EXISTS", StringComparison.Ordinal)) {
			return;
		}
		if (string.Equals(brf.AggregationType, "COUNT", StringComparison.Ordinal)) {
			RequireNumericAggregationValue(brf.AggregationValue, "COUNT", fieldPath + ".aggregationValue");
			return;
		}
		// SUM / AVG / MIN / MAX — validate aggregationColumnPath against child schema.
		string aggregationColumnPath = brf.AggregationColumnPath!;
		if (!childColumns.TryGetValue(aggregationColumnPath, out EntitySchemaColumnDto? aggColumn)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.PathUnknown,
				fieldPath + ".aggregationColumnPath",
				$"aggregationColumnPath '{aggregationColumnPath}' not found on child schema '{childSchemaName}'.");
		}
		string aggColumnTypeName = BusinessRuleHelpers.MapDataValueTypeName(aggColumn.DataValueType);
		if (!SupportedNumericDataValueTypeNames.Contains(aggColumnTypeName)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype,
				fieldPath + ".aggregationColumnPath",
				$"aggregation '{brf.AggregationType}' requires a numeric column on '{childSchemaName}'; '{aggregationColumnPath}' is {aggColumnTypeName}.");
		}
		RequireNumericAggregationValue(brf.AggregationValue, brf.AggregationType!, fieldPath + ".aggregationValue");
	}

	private static void RequireNumericAggregationValue(JsonElement? value, string aggregationType, string fieldPath) {
		if (!value.HasValue || value.Value.ValueKind != JsonValueKind.Number) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ValueShape,
				fieldPath,
				$"aggregationValue for '{aggregationType}' must be a JSON number.");
		}
	}

	private static void ValidateComparisonForDatatype(
		string comparisonType,
		EntitySchemaColumnDto column,
		string fieldPath) {
		string typeName = BusinessRuleHelpers.MapDataValueTypeName(column.DataValueType);
		if (RelationalFilterComparisons.Contains(comparisonType)
			&& !SupportedNumericDataValueTypeNames.Contains(typeName)
			&& !SupportedDateTimeDataValueTypeNames.Contains(typeName)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype,
				fieldPath,
				$"Relational comparison '{comparisonType}' is not supported for column '{column.Name}' (type {typeName}). " +
				"Use only on numeric or date/time columns.");
		}
		if (StringMatchFilterComparisons.Contains(comparisonType)
			&& !SupportedTextDataValueTypeNames.Contains(typeName)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype,
				fieldPath,
				$"String-match comparison '{comparisonType}' is not supported for column '{column.Name}' (type {typeName}). " +
				"Use only on text columns.");
		}
	}

	private static void ValidateValueDatatype(
		JsonElement? value,
		EntitySchemaColumnDto column,
		string fieldPath) {
		if (!value.HasValue) {
			// Structural validator already enforced presence for binary comparisons.
			return;
		}
		string typeName = BusinessRuleHelpers.MapDataValueTypeName(column.DataValueType);
		JsonElement element = value.Value;
		JsonValueKind kind = element.ValueKind;
		// Relative-date macros on a Date / DateTime / Time column are accepted as JSON strings
		// without ISO format enforcement; the converter routes them to EsqMacroBuilder.
		if (SupportedDateTimeDataValueTypeNames.Contains(typeName)
			&& Clio.Command.BusinessRules.Filters.Esq.EsqMacroBuilder.IsMacroValue(value)) {
			return;
		}
		if (string.Equals(typeName, "Lookup", StringComparison.Ordinal)) {
			RequireLookupGuid(element, kind, column, fieldPath);
		} else if (string.Equals(typeName, "Boolean", StringComparison.Ordinal)) {
			RequireKind(kind, JsonValueKind.True, JsonValueKind.False, column, typeName, "boolean", fieldPath);
		} else if (SupportedNumericDataValueTypeNames.Contains(typeName)) {
			RequireKind(kind, JsonValueKind.Number, null, column, typeName, "number", fieldPath);
		} else if (SupportedDateTimeDataValueTypeNames.Contains(typeName)
			|| SupportedTextDataValueTypeNames.Contains(typeName)) {
			RequireKind(kind, JsonValueKind.String, null, column, typeName, "string", fieldPath);
		}
		// Other datatypes (Guid, Image, Blob, File, Enum) are not exposed on the friendly filter
		// surface today; let the server reject them if they slip through.
	}

	private static void RequireLookupGuid(
		JsonElement element,
		JsonValueKind kind,
		EntitySchemaColumnDto column,
		string fieldPath) {
		// Schema-aware validator accepts any JSON string on a Lookup column: GUID strings pass
		// through unchanged; non-GUID strings are treated as display names and resolved at
		// conversion time by ILookupValueResolver. JSON arrays of strings are accepted with the
		// same semantics (multi-value IN / NOT_IN). Other JSON kinds (number/boolean/object) are
		// always invalid.
		if (kind == JsonValueKind.String) {
			return;
		}
		if (kind == JsonValueKind.Array) {
			int length = element.GetArrayLength();
			if (length == 0) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.LookupValueNotGuid,
					fieldPath,
					$"Lookup column '{column.Name}' expects a non-empty array of string values.");
			}
			int index = 0;
			foreach (JsonElement entry in element.EnumerateArray()) {
				if (entry.ValueKind != JsonValueKind.String) {
					throw new BusinessRuleFilterException(
						BusinessRuleFilterErrorCodes.LookupValueNotGuid,
						$"{fieldPath}[{index}]",
						$"Lookup column '{column.Name}' array element at index {index} must be a JSON string.");
				}
				index++;
			}
			return;
		}
		throw new BusinessRuleFilterException(
			BusinessRuleFilterErrorCodes.LookupValueNotGuid,
			fieldPath,
			$"Lookup column '{column.Name}' expects a JSON string value (GUID record Id or display name) or an array of strings; got JSON {kind} '{element}'.");
	}

	private static void RequireKind(
		JsonValueKind actual,
		JsonValueKind expectedPrimary,
		JsonValueKind? expectedAlternate,
		EntitySchemaColumnDto column,
		string typeName,
		string expectedLabel,
		string fieldPath) {
		if (actual == expectedPrimary || (expectedAlternate.HasValue && actual == expectedAlternate.Value)) {
			return;
		}
		throw NewValueShape(column, typeName, expectedLabel, actual, fieldPath);
	}

	private static BusinessRuleFilterException NewValueShape(
		EntitySchemaColumnDto column,
		string typeName,
		string expected,
		JsonValueKind actual,
		string fieldPath) =>
		new(
			BusinessRuleFilterErrorCodes.ValueShape,
			fieldPath,
			$"Column '{column.Name}' ({typeName}) expects a JSON {expected} value; got JSON {actual}.");
}
