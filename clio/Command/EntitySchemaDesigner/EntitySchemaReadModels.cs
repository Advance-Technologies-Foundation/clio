using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Represents a compact column entry returned as part of a schema properties snapshot.
/// </summary>
public sealed record EntitySchemaPropertyColumnInfo(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("u-id")] Guid UId,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("title")] string? Title,
	[property: JsonPropertyName("description")] string? Description,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("required")] bool Required,
	[property: JsonPropertyName("indexed")] bool Indexed,
	[property: JsonPropertyName("reference-schema-name")] string? ReferenceSchemaName);

/// <summary>
/// Represents a structured snapshot of remote entity schema properties with nested column metadata.
/// </summary>
public sealed record EntitySchemaPropertiesInfo(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("title")] string? Title,
	[property: JsonPropertyName("description")] string? Description,
	[property: JsonPropertyName("package-name")] string PackageName,
	[property: JsonPropertyName("parent-schema-name")] string? ParentSchemaName,
	[property: JsonPropertyName("extend-parent")] bool ExtendParent,
	[property: JsonPropertyName("primary-column-name")] string? PrimaryColumnName,
	[property: JsonPropertyName("primary-display-column-name")] string? PrimaryDisplayColumnName,
	[property: JsonPropertyName("own-column-count")] int OwnColumnCount,
	[property: JsonPropertyName("inherited-column-count")] int InheritedColumnCount,
	[property: JsonPropertyName("indexes-count")] int IndexesCount,
	[property: JsonPropertyName("track-changes-in-db")] bool TrackChangesInDb,
	[property: JsonPropertyName("db-view")] bool DbView,
	[property: JsonPropertyName("ssp-available")] bool SspAvailable,
	[property: JsonPropertyName("virtual")] bool Virtual,
	[property: JsonPropertyName("use-record-deactivation")] bool UseRecordDeactivation,
	[property: JsonPropertyName("show-in-advanced-mode")] bool ShowInAdvancedMode,
	[property: JsonPropertyName("administrated-by-operations")] bool AdministratedByOperations,
	[property: JsonPropertyName("administrated-by-columns")] bool AdministratedByColumns,
	[property: JsonPropertyName("administrated-by-records")] bool AdministratedByRecords,
	[property: JsonPropertyName("use-deny-record-rights")] bool UseDenyRecordRights,
	[property: JsonPropertyName("use-live-editing")] bool UseLiveEditing,
	[property: JsonPropertyName("columns")] IReadOnlyList<EntitySchemaPropertyColumnInfo>? Columns = null);

/// <summary>
/// Represents a structured snapshot of remote entity schema column properties.
/// </summary>
public sealed record EntitySchemaColumnPropertiesInfo(
	[property: JsonPropertyName("schema-name")] string SchemaName,
	[property: JsonPropertyName("package-name")] string PackageName,
	[property: JsonPropertyName("column-name")] string ColumnName,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("title")] string? Title,
	[property: JsonPropertyName("description")] string? Description,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("required")] bool Required,
	[property: JsonPropertyName("indexed")] bool Indexed,
	[property: JsonPropertyName("cloneable")] bool Cloneable,
	[property: JsonPropertyName("track-changes")] bool TrackChanges,
	[property: JsonPropertyName("default-value-source")] string? DefaultValueSource,
	[property: JsonPropertyName("default-value")] string? DefaultValue,
	[property: JsonPropertyName("reference-schema-name")] string? ReferenceSchemaName,
	[property: JsonPropertyName("simple-lookup")] bool SimpleLookup,
	[property: JsonPropertyName("cascade")] bool Cascade,
	[property: JsonPropertyName("do-not-control-integrity")] bool DoNotControlIntegrity,
	[property: JsonPropertyName("multiline-text")] bool MultilineText,
	[property: JsonPropertyName("localizable-text")] bool LocalizableText,
	[property: JsonPropertyName("accent-insensitive")] bool AccentInsensitive,
	[property: JsonPropertyName("masked")] bool Masked,
	[property: JsonPropertyName("format-validated")] bool FormatValidated,
	[property: JsonPropertyName("use-seconds")] bool UseSeconds,
	[property: JsonPropertyName("default-value-config")] EntitySchemaDefaultValueConfig? DefaultValueConfig = null);

/// <summary>
/// Represents a domain-specific entity schema designer failure.
/// </summary>
public sealed class EntitySchemaDesignerException : Exception {
	/// <summary>
	/// Initializes a new instance of the <see cref="EntitySchemaDesignerException"/> class.
	/// </summary>
	/// <param name="message">The error message that explains the failure.</param>
	public EntitySchemaDesignerException(string message)
		: base(message) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="EntitySchemaDesignerException"/> class.
	/// </summary>
	/// <param name="message">The error message that explains the failure.</param>
	/// <param name="innerException">The exception that caused the current exception.</param>
	public EntitySchemaDesignerException(string message, Exception innerException)
		: base(message, innerException) {
	}
}
