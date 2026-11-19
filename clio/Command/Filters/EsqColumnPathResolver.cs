using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.BusinessRules.Filters.Schema;

namespace Clio.Command.Filters;

// Unverifiable: a schema on the path could not be read (refused, or not visible from the package), so the path
// may still be valid; never turn it into a rejection.
internal sealed record EsqColumnResolution(
	FilterSchemaColumn? Column,
	string? LastJoinedSchema,
	string? Error,
	bool Unverifiable) {

	internal bool IsResolved => Column is not null;
}

// Covers backward references ([Child:Link], [Child:Link:SourceColumn]) and mixed chains
// (Contact.[Lead:QualifiedContact].Id). Kept apart from SchemaAwareFilterValidator.ResolveColumnPath, which serves
// the friendly dialect (throws, exact names); stored filters need ESQ's lenient spellings and a verdict.
internal sealed class EsqColumnPathResolver {

	private const string LookupTypeName = "Lookup";
	private const string IdSuffix = "Id";

	private readonly IFilterSchemaProvider _schemaProvider;
	// A schema the environment refused once is refused again; asking per leaf only multiplies the timeouts.
	private readonly HashSet<string> _unreadableSchemas = new(StringComparer.OrdinalIgnoreCase);

	internal EsqColumnPathResolver(IFilterSchemaProvider schemaProvider) {
		_schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
	}

	internal EsqColumnResolution Resolve(string columnPath, string rootSchemaName) {
		if (string.IsNullOrWhiteSpace(columnPath)) {
			return Failed("the column path is empty");
		}
		string currentSchema = rootSchemaName;
		string? lastJoinedSchema = null;
		FilterSchemaColumn? column = null;
		string[] segments = columnPath.Split('.');
		for (int index = 0; index < segments.Length; index++) {
			string segment = segments[index];
			bool isLast = index == segments.Length - 1;
			EsqColumnResolution? failure = segment.StartsWith('[') && segment.EndsWith(']')
				? StepIntoBackwardReference(segment, isLast, ref currentSchema, ref lastJoinedSchema)
				: StepIntoColumn(segment, isLast, ref currentSchema, ref column);
			if (failure is not null) {
				return failure;
			}
		}
		return new EsqColumnResolution(column, lastJoinedSchema, null, false);
	}

	// [Schema:LinkColumn]: the path continues on Schema. Returns the failure, or null to go on.
	private EsqColumnResolution? StepIntoBackwardReference(
		string segment, bool isLast, ref string currentSchema, ref string? lastJoinedSchema) {
		string[] parts = segment[1..^1].Split(':');
		if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace)) {
			return Failed($"segment '{segment}' is not a backward reference of the form [Schema:LinkColumn]");
		}
		if (!TryGetColumns(parts[0], out IReadOnlyDictionary<string, FilterSchemaColumn>? childColumns)) {
			return Unreadable(parts[0]);
		}
		if (FindColumn(childColumns, parts[1]) is null) {
			return Failed($"column '{parts[1]}' of backward reference '{segment}' was not found on schema '{parts[0]}'");
		}
		currentSchema = parts[0];
		lastJoinedSchema = parts[0];
		return isLast
			? Failed($"backward reference '{segment}' must be followed by a column of '{parts[0]}' (for example '{segment}.Id')")
			: null;
	}

	// A column of the current schema; any but the last must be a lookup, whose schema the path continues on.
	private EsqColumnResolution? StepIntoColumn(
		string segment, bool isLast, ref string currentSchema, ref FilterSchemaColumn? column) {
		if (!TryGetColumns(currentSchema, out IReadOnlyDictionary<string, FilterSchemaColumn>? columns)) {
			return Unreadable(currentSchema);
		}
		column = FindColumn(columns, segment);
		if (column is null) {
			return Failed($"column '{segment}' was not found on schema '{currentSchema}'");
		}
		if (isLast) {
			return null;
		}
		if (!string.Equals(column.DataValueTypeName, LookupTypeName, StringComparison.OrdinalIgnoreCase)
			|| string.IsNullOrEmpty(column.ReferenceSchemaName)) {
			return Failed($"column '{segment}' on schema '{currentSchema}' is {column.DataValueTypeName}, not a lookup, so the path cannot continue past it");
		}
		currentSchema = column.ReferenceSchemaName;
		return null;
	}

	// ESQ matches names case-insensitively and reaches a lookup by its value-column name (CountryId for Country);
	// rejecting either spelling would flag filters the runtime runs.
	private static FilterSchemaColumn? FindColumn(IReadOnlyDictionary<string, FilterSchemaColumn> columns, string name) {
		if (columns.TryGetValue(name, out FilterSchemaColumn? exact)) {
			return exact;
		}
		FilterSchemaColumn? caseInsensitive = columns.Values
			.FirstOrDefault(column => string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase));
		if (caseInsensitive is not null) {
			return caseInsensitive;
		}
		if (name.Length > IdSuffix.Length && name.EndsWith(IdSuffix, StringComparison.OrdinalIgnoreCase)) {
			FilterSchemaColumn? lookup = FindColumn(columns, name[..^IdSuffix.Length]);
			if (lookup is not null && string.Equals(lookup.DataValueTypeName, LookupTypeName, StringComparison.OrdinalIgnoreCase)) {
				return lookup;
			}
		}
		return null;
	}

	private bool TryGetColumns(string schemaName, out IReadOnlyDictionary<string, FilterSchemaColumn>? columns) {
		columns = null;
		if (_unreadableSchemas.Contains(schemaName)) {
			return false;
		}
		try {
			columns = _schemaProvider.GetColumns(schemaName);
		} catch (Exception) {
			columns = null;
		}
		if (columns is null) {
			_unreadableSchemas.Add(schemaName);
			return false;
		}
		return true;
	}

	private static EsqColumnResolution Failed(string error) => new(null, null, error, false);

	private static EsqColumnResolution Unreadable(string schemaName) =>
		new(null, null, $"schema '{schemaName}' could not be read from the environment", true);
}
