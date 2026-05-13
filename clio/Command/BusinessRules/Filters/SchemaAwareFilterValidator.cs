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
			if (!currentColumns.TryGetValue(segment, out EntitySchemaColumnDto? column)) {
				string sample = string.Join(", ", currentColumns.Keys.Take(20));
				string ellipsis = currentColumns.Count > 20 ? ", ..." : string.Empty;
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.PathUnknown,
					fieldPath,
					$"Column '{segment}' not found on schema '{currentSchema}'. Available: {sample}{ellipsis}.");
			}
			lastResolved = column;
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
		if (string.Equals(typeName, "Lookup", StringComparison.Ordinal)) {
			if (kind != JsonValueKind.String || !Guid.TryParse(element.GetString(), out _)) {
				throw new BusinessRuleFilterException(
					BusinessRuleFilterErrorCodes.LookupValueNotGuid,
					fieldPath,
					$"Lookup column '{column.Name}' expects a JSON string GUID value; got JSON {kind} '{element}'. " +
					"Resolve the lookup display name to its record Id (GUID) before passing it as a filter value.");
			}
			return;
		}
		if (string.Equals(typeName, "Boolean", StringComparison.Ordinal)) {
			if (kind != JsonValueKind.True && kind != JsonValueKind.False) {
				throw NewValueShape(column, typeName, "boolean", kind, fieldPath);
			}
			return;
		}
		if (SupportedNumericDataValueTypeNames.Contains(typeName)) {
			if (kind != JsonValueKind.Number) {
				throw NewValueShape(column, typeName, "number", kind, fieldPath);
			}
			return;
		}
		if (SupportedDateTimeDataValueTypeNames.Contains(typeName)
			|| SupportedTextDataValueTypeNames.Contains(typeName)) {
			if (kind != JsonValueKind.String) {
				throw NewValueShape(column, typeName, "string", kind, fieldPath);
			}
			return;
		}
		// Other datatypes (Guid, Image, Blob, File, Enum) are not exposed on the friendly filter
		// surface today; let the server reject them if they slip through.
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
