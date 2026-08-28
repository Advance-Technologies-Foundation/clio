using System;
using System.Text.Json.Serialization;

namespace Clio.Command.SchemaTransfer;

/// <summary>
/// Provenance and identity of an exported schema, written to the bundle's <c>descriptor.json</c>.
/// </summary>
/// <remarks>
/// This file is for the reader and for auditing a handover. It is NOT what import consumes — import reads
/// <c>schema-data.json</c> — so editing it can never change what actually ships.
/// </remarks>
public sealed class SchemaBundleDescriptor {

	/// <summary>Gets or sets the exported schema name.</summary>
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	/// <summary>Gets or sets the schema <c>UId</c>, which import preserves on the target environment.</summary>
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	/// <summary>Gets or sets the schema caption in the base culture.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>Gets or sets the owning schema manager, for example <c>AddonSchemaManager</c>.</summary>
	[JsonPropertyName("managerName")]
	public string ManagerName { get; set; }

	/// <summary>Gets or sets the package that owned the schema on the source environment.</summary>
	[JsonPropertyName("sourcePackageName")]
	public string SourcePackageName { get; set; }

	/// <summary>Gets or sets the URL of the environment the schema was exported from.</summary>
	[JsonPropertyName("sourceEnvironmentUrl")]
	public string SourceEnvironmentUrl { get; set; }

	/// <summary>Gets or sets the UTC timestamp of the export.</summary>
	[JsonPropertyName("exportedOnUtc")]
	public DateTime ExportedOnUtc { get; set; }

	/// <summary>Gets or sets the clio version that produced the bundle.</summary>
	[JsonPropertyName("clioVersion")]
	public string ClioVersion { get; set; }
}

/// <summary>
/// One exported schema: the authoritative platform payload plus the descriptor written alongside it.
/// </summary>
/// <param name="Descriptor">Provenance and identity of the export.</param>
/// <param name="SchemaData">
/// The verbatim payload produced by the platform schema exporter — the only thing import consumes.
/// </param>
public sealed record SchemaBundle(SchemaBundleDescriptor Descriptor, string SchemaData);
