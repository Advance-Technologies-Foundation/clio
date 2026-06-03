using System.Collections.Generic;

namespace Clio.Command.BusinessRules.Filters.Schema;

/// <summary>
/// Describes a single column on a Creatio entity schema, scoped to what the static-filter validator + converter need.
/// </summary>
internal sealed class FilterSchemaColumn {
	public string Name { get; init; } = string.Empty;
	/// <summary>Terrasoft data value type name (e.g. Text, Lookup, Boolean, DateTime, Date, Time, Integer, Float, Money).</summary>
	public string DataValueTypeName { get; init; } = string.Empty;
	/// <summary>Numeric Creatio DataValueType code matching <see cref="DataValueTypeName"/>; emitted on CompareFilter for macros.</summary>
	public int DataValueTypeCode { get; init; }
	/// <summary>Lookup reference schema name; null for non-lookup columns.</summary>
	public string? ReferenceSchemaName { get; init; }
}

/// <summary>
/// Fetches column metadata for an entity schema. Implementations cache per instance.
/// </summary>
internal interface IFilterSchemaProvider {
	IReadOnlyDictionary<string, FilterSchemaColumn> GetColumns(string schemaName);
	string? GetPrimaryDisplayColumnName(string schemaName);
}
