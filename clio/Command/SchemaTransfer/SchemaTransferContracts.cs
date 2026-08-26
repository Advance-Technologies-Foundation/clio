using System.Collections.Generic;
using System.Text.Json.Serialization;
using Clio.Common.Responses;

namespace Clio.Command.SchemaTransfer;

/// <summary>
/// One <c>SysSchema</c> row: a schema as it exists in a single package layer.
/// </summary>
/// <remarks>
/// A schema name is unique only per (manager, package) pair, so a name can legitimately match several
/// layers. Every schema-transfer operation reports the layer it acted on so the caller can tell which one
/// it got.
/// </remarks>
public sealed class SchemaLayerDto {

	/// <summary>Gets or sets the <c>SysSchema.Id</c> value — the record id, local to one environment.</summary>
	[JsonPropertyName("schemaId")]
	public string SchemaId { get; set; }

	/// <summary>Gets or sets the <c>SysSchema.UId</c> value — the identity that is stable across environments.</summary>
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	/// <summary>Gets or sets the schema name.</summary>
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	/// <summary>Gets or sets the schema caption in the base culture.</summary>
	[JsonPropertyName("caption")]
	public string Caption { get; set; }

	/// <summary>Gets or sets the owning schema manager, for example <c>AddonSchemaManager</c>.</summary>
	[JsonPropertyName("managerName")]
	public string ManagerName { get; set; }

	/// <summary>Gets or sets the name of the package that owns this layer.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	/// <summary>Gets or sets the UId of the package that owns this layer.</summary>
	[JsonPropertyName("packageUId")]
	public string PackageUId { get; set; }
}

/// <summary>
/// Response of the ClioGate <c>FindSchemaLayers</c> route.
/// </summary>
public sealed class FindSchemaLayersResponse : BaseResponse {

	/// <summary>Gets or sets the matching layers; empty when the schema does not exist.</summary>
	[JsonPropertyName("layers")]
	public List<SchemaLayerDto> Layers { get; set; } = [];
}

/// <summary>
/// Response of the ClioGate <c>ExportSchema</c> route.
/// </summary>
public sealed class ExportSchemaGateResponse : BaseResponse {

	/// <summary>Gets or sets the identity of the exported layer; <c>null</c> when the export did not resolve one.</summary>
	[JsonPropertyName("schema")]
	public SchemaLayerDto Schema { get; set; }

	/// <summary>
	/// Gets or sets the verbatim payload produced by the platform schema exporter. This is the only input the
	/// import route accepts.
	/// </summary>
	[JsonPropertyName("schemaData")]
	public string SchemaData { get; set; }

	/// <summary>Gets or sets the layers that matched an ambiguous request. Empty on success.</summary>
	[JsonPropertyName("candidates")]
	public List<SchemaLayerDto> Candidates { get; set; } = [];
}

/// <summary>
/// Response of the ClioGate <c>ImportSchema</c> route.
/// </summary>
public sealed class ImportSchemaGateResponse : BaseResponse {

	/// <summary>Gets or sets the name of the package the schema was written into.</summary>
	[JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	/// <summary>Gets or sets the UId of the package the schema was written into.</summary>
	[JsonPropertyName("packageUId")]
	public string PackageUId { get; set; }

	/// <summary>Gets or sets the platform importer's own diagnostic string.</summary>
	[JsonPropertyName("importResult")]
	public string ImportResult { get; set; }
}
