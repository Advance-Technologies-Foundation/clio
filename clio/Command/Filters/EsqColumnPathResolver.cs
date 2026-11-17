using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.BusinessRules.Filters.Schema;

namespace Clio.Command.Filters;

/// <summary>
/// Outcome of resolving an ESQ column path. <see cref="Unverifiable"/> means a schema on the way could not be
/// read (the environment refused or the schema is not visible from the package): the caller must not turn
/// that into a rejection, since the path itself may be fine.
/// </summary>
internal sealed record EsqColumnResolution(
	FilterSchemaColumn? Column,
	string? OwnerSchema,
	string? LastJoinedSchema,
	string? Error,
	bool Unverifiable) {

	internal bool IsResolved => Column is not null;
}

/// <summary>
/// Resolves ESQ column paths, including backward references (<c>[Child:Link]</c> or
/// <c>[Child:Link:SourceColumn]</c>) and mixed forward/backward chains such as
/// <c>Contact.[Lead:QualifiedContact].Id</c>, against live entity-schema metadata.
/// </summary>
internal sealed class EsqColumnPathResolver {

	private const string LookupTypeName = "Lookup";
	private const string IdSuffix = "Id";

	private readonly IFilterSchemaProvider _schemaProvider;

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
			if (segment.StartsWith('[') && segment.EndsWith(']')) {
				string[] parts = segment[1..^1].Split(':');
				if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace)) {
					return Failed($"segment '{segment}' is not a backward reference of the form [Schema:LinkColumn]");
				}
				if (!TryGetColumns(parts[0], out IReadOnlyDictionary<string, FilterSchemaColumn>? childColumns)) {
					return Unreadable(parts[0]);
				}
				if (FindColumn(childColumns!, parts[1]) is null) {
					return Failed($"column '{parts[1]}' of backward reference '{segment}' was not found on schema '{parts[0]}'");
				}
				currentSchema = parts[0];
				lastJoinedSchema = parts[0];
				if (isLast) {
					return Failed($"backward reference '{segment}' must be followed by a column of '{parts[0]}' (for example '{segment}.Id')");
				}
				continue;
			}
			if (!TryGetColumns(currentSchema, out IReadOnlyDictionary<string, FilterSchemaColumn>? columns)) {
				return Unreadable(currentSchema);
			}
			column = FindColumn(columns!, segment);
			if (column is null) {
				return Failed($"column '{segment}' was not found on schema '{currentSchema}'");
			}
			if (isLast) {
				break;
			}
			if (!string.Equals(column.DataValueTypeName, LookupTypeName, StringComparison.OrdinalIgnoreCase)
				|| string.IsNullOrEmpty(column.ReferenceSchemaName)) {
				return Failed($"column '{segment}' on schema '{currentSchema}' is {column.DataValueTypeName}, not a lookup, so the path cannot continue past it");
			}
			currentSchema = column.ReferenceSchemaName;
		}
		return new EsqColumnResolution(column, currentSchema, lastJoinedSchema, null, false);
	}

	/// <remarks>
	/// ESQ resolves column names case-insensitively, and a lookup is also reachable by its value-column name
	/// (<c>CountryId</c> for <c>Country</c>); rejecting either spelling would flag filters the runtime runs.
	/// </remarks>
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
		try {
			columns = _schemaProvider.GetColumns(schemaName);
			return columns is not null;
		} catch (Exception) {
			columns = null;
			return false;
		}
	}

	private static EsqColumnResolution Failed(string error) => new(null, null, null, error, false);

	private static EsqColumnResolution Unreadable(string schemaName) =>
		new(null, null, null, $"schema '{schemaName}' could not be read from the environment", true);
}
