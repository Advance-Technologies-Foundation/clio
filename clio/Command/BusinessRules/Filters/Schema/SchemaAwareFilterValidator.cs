using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.BusinessRules.Filters.Schema;

/// <summary>
/// Validates filter column paths and value datatypes against the actual schema metadata.
/// </summary>
internal sealed class SchemaAwareFilterValidator {

	private const string LookupDataValueTypeName = "Lookup";

	private readonly IFilterSchemaProvider _schemaProvider;

	public SchemaAwareFilterValidator(IFilterSchemaProvider schemaProvider) {
		_schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
	}

	public void Validate(StaticFilterGroup group, string rootSchemaName) =>
		Validate(group, rootSchemaName, "filter");

	private void Validate(StaticFilterGroup group, string rootSchemaName, string path) {
		foreach ((StaticFilterLeaf leaf, int index) in WithIndex(group.Filters)) {
			ValidateLeaf(leaf, rootSchemaName, $"{path}.filters[{index}]");
		}

		foreach ((StaticFilterGroup nested, int index) in WithIndex(group.Groups)) {
			Validate(nested, rootSchemaName, $"{path}.groups[{index}]");
		}

		foreach ((StaticFilterBackwardReference backward, int index) in WithIndex(group.BackwardReferenceFilters)) {
			ValidateBackwardReference(backward, rootSchemaName, $"{path}.backwardReferenceFilters[{index}]");
		}
	}

	private void ValidateLeaf(StaticFilterLeaf leaf, string rootSchemaName, string path) {
		FilterSchemaColumn column = ResolveColumnPath(leaf.ColumnPath, rootSchemaName, $"{path}.columnPath");
		string comparison = leaf.ComparisonType.ToUpperInvariant();

		if (StaticFilterConstants.UnaryComparisons.Contains(comparison)) {
			return;
		}

		if (StaticFilterConstants.RelationalComparisons.Contains(comparison)
			&& !IsRelationalDatatype(column.DataValueTypeName)) {
			throw new ArgumentException(
				$"{path}.comparisonType: '{comparison}' is supported only on numeric and date/time columns. Column '{leaf.ColumnPath}' is {column.DataValueTypeName}.");
		}

		if (StaticFilterConstants.TextComparisons.Contains(comparison)
			&& !IsTextDatatype(column.DataValueTypeName)) {
			throw new ArgumentException(
				$"{path}.comparisonType: '{comparison}' is supported only on text columns. Column '{leaf.ColumnPath}' is {column.DataValueTypeName}.");
		}

		JsonElement value = leaf.Value!.Value;
		if (value.ValueKind == JsonValueKind.Array) {
			if (!string.Equals(column.DataValueTypeName, LookupDataValueTypeName, StringComparison.OrdinalIgnoreCase)) {
				throw new ArgumentException(
					$"{path}.value: array (multi-value IN) is supported only on Lookup columns. Column '{leaf.ColumnPath}' is {column.DataValueTypeName}.");
			}

			return;
		}

		ValidateScalarValueDatatype(value, column, leaf.ColumnPath, path);
	}

	private static void ValidateScalarValueDatatype(JsonElement value, FilterSchemaColumn column, string columnPath, string path) {
		switch (column.DataValueTypeName) {
			case "Boolean":
				if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) {
					throw new ArgumentException(
						$"{path}.value: must be a JSON boolean when column '{columnPath}' is Boolean.");
				}
				return;
			case LookupDataValueTypeName:
			case "Guid":
				if (value.ValueKind != JsonValueKind.String) {
					throw new ArgumentException(
						$"{path}.value: must be a JSON string (GUID or display name) when column '{columnPath}' is {column.DataValueTypeName}.");
				}
				return;
		}

		if (IsTextDatatype(column.DataValueTypeName) && value.ValueKind != JsonValueKind.String) {
			throw new ArgumentException(
				$"{path}.value: must be a JSON string when column '{columnPath}' is a text type.");
		}

		if (IsNumericDatatype(column.DataValueTypeName) && value.ValueKind != JsonValueKind.Number) {
			throw new ArgumentException(
				$"{path}.value: must be a JSON number when column '{columnPath}' is a numeric type.");
		}

		if (IsDateTimeDatatype(column.DataValueTypeName) && value.ValueKind != JsonValueKind.String) {
			throw new ArgumentException(
				$"{path}.value: must be a JSON string (ISO 8601) when column '{columnPath}' is a date/time type.");
		}
	}

	private void ValidateBackwardReference(StaticFilterBackwardReference backward, string rootSchemaName, string path) {
		(string childSchema, string childColumn) = ParseBackwardReference(backward.ReferenceColumnPath);
		IReadOnlyDictionary<string, FilterSchemaColumn> childColumns = _schemaProvider.GetColumns(childSchema);
		if (!childColumns.TryGetValue(childColumn, out FilterSchemaColumn? linkColumn)) {
			throw new ArgumentException(
				$"{path}.referenceColumnPath: column '{childColumn}' not found on child schema '{childSchema}'.");
		}

		if (!string.Equals(linkColumn.DataValueTypeName, LookupDataValueTypeName, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(linkColumn.ReferenceSchemaName, rootSchemaName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"{path}.referenceColumnPath: column '{childColumn}' on '{childSchema}' must be a Lookup pointing back to root schema '{rootSchemaName}'.");
		}

		if (backward.Filter is not null) {
			Validate(backward.Filter, childSchema, $"{path}.filter");
		}
	}

	internal FilterSchemaColumn ResolveColumnPath(string columnPath, string rootSchemaName, string path) {
		string[] segments = columnPath.Split('.');
		string currentSchema = rootSchemaName;
		FilterSchemaColumn? column = null;
		for (int i = 0; i < segments.Length; i++) {
			string segment = segments[i];
			IReadOnlyDictionary<string, FilterSchemaColumn> columns = _schemaProvider.GetColumns(currentSchema);
			if (!columns.TryGetValue(segment, out column)) {
				List<string> available = [.. columns.Keys];
				available.Sort(StringComparer.OrdinalIgnoreCase);
				throw new ArgumentException(
					$"filter.path-unknown: Column '{segment}' not found on schema '{currentSchema}' (looked up by Name). Available names: {string.Join(", ", available)}. (path={path})");
			}

			bool isLast = i == segments.Length - 1;
			if (!isLast) {
				if (!string.Equals(column.DataValueTypeName, LookupDataValueTypeName, StringComparison.OrdinalIgnoreCase)
					|| string.IsNullOrEmpty(column.ReferenceSchemaName)) {
					throw new ArgumentException(
						$"{path}: segment '{segment}' on schema '{currentSchema}' is not a Lookup; forward-path traversal requires Lookup columns.");
				}

				currentSchema = column.ReferenceSchemaName!;
			}
		}

		return column!;
	}

	private static (string childSchema, string childColumn) ParseBackwardReference(string referenceColumnPath) {
		string inner = referenceColumnPath.Trim('[', ']');
		string[] parts = inner.Split(':');
		return (parts[0], parts[1]);
	}

	private static bool IsTextDatatype(string dataValueTypeName) =>
		dataValueTypeName is "Text" or "ShortText" or "MediumText" or "MaxSizeText" or "LongText"
			or "PhoneText" or "WebText" or "EmailText" or "SecureText" or "RichText";

	private static bool IsNumericDatatype(string dataValueTypeName) =>
		dataValueTypeName is "Integer" or "Float" or "Money"
			or "Float0" or "Float1" or "Float2" or "Float3" or "Float4" or "Float8";

	private static bool IsDateTimeDatatype(string dataValueTypeName) =>
		dataValueTypeName is "Date" or "DateTime" or "Time";

	private static bool IsRelationalDatatype(string dataValueTypeName) =>
		IsNumericDatatype(dataValueTypeName) || IsDateTimeDatatype(dataValueTypeName);

	private static IEnumerable<(T item, int index)> WithIndex<T>(IEnumerable<T> source) {
		int i = 0;
		foreach (T item in source) {
			yield return (item, i++);
		}
	}
}
