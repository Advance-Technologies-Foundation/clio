using System.Collections.Generic;
using System.Runtime.Serialization;
using Terrasoft.Core.ServiceModelContract;

namespace cliogate.Files.cs.Dto
{

	#region Class: SchemaLayerInfo

	/// <summary>
	/// One <c>SysSchema</c> row that matched an export request by name: the schema as it exists in a single
	/// package layer.
	/// </summary>
	/// <remarks>
	/// A schema name is unique only per (manager, package) pair, so a name-only lookup can legitimately match
	/// several layers. This DTO is what lets the caller disambiguate: an ambiguous export returns the matching
	/// layers instead of silently picking one — the failure mode
	/// <c>delete-schema --remote</c> is known to have.
	/// </remarks>
	[DataContract(Name = nameof(SchemaLayerInfo))]
	public class SchemaLayerInfo
	{

		#region Properties: Public

		/// <summary>Value of <c>SysSchema.Id</c> — the record id the platform exporter takes.</summary>
		[DataMember(Name = "schemaId", Order = 10)]
		public string SchemaId { get; set; }

		/// <summary>Value of <c>SysSchema.UId</c> — the identity that is stable across environments.</summary>
		[DataMember(Name = "schemaUId", Order = 20)]
		public string SchemaUId { get; set; }

		/// <summary>Schema name.</summary>
		[DataMember(Name = "schemaName", Order = 30)]
		public string SchemaName { get; set; }

		/// <summary>Schema caption in the base culture, when the platform stores one.</summary>
		[DataMember(Name = "caption", Order = 40)]
		public string Caption { get; set; }

		/// <summary>Owning schema manager, for example <c>ClientUnitSchemaManager</c> or <c>AddonSchemaManager</c>.</summary>
		[DataMember(Name = "managerName", Order = 50)]
		public string ManagerName { get; set; }

		/// <summary>Name of the package that owns this layer.</summary>
		[DataMember(Name = "packageName", Order = 60)]
		public string PackageName { get; set; }

		/// <summary>UId of the package that owns this layer.</summary>
		[DataMember(Name = "packageUId", Order = 70)]
		public string PackageUId { get; set; }

		#endregion

	}

	#endregion

	#region Class: ExportSchemaResponse

	/// <summary>
	/// Result of <see cref="CreatioApiGateway.ExportSchema"/>: the platform schema-export payload for exactly one
	/// schema layer, plus the identity of the layer it came from.
	/// </summary>
	[DataContract(Name = nameof(ExportSchemaResponse))]
	public class ExportSchemaResponse : BaseResponse
	{

		#region Properties: Public

		/// <summary>Identity of the exported layer. <c>null</c> when the export did not resolve a single layer.</summary>
		[DataMember(Name = "schema", Order = 10)]
		public SchemaLayerInfo Schema { get; set; }

		/// <summary>
		/// Verbatim payload produced by the platform schema exporter. It carries the schema metadata, its
		/// properties and its localizable resources, and is the exact string the import endpoint consumes.
		/// </summary>
		[DataMember(Name = "schemaData", Order = 20)]
		public string SchemaData { get; set; }

		/// <summary>
		/// Layers that matched the request when it was ambiguous or unresolvable, so the caller can retry with an
		/// explicit package or manager. Empty on success.
		/// </summary>
		[DataMember(Name = "candidates", Order = 30)]
		public List<SchemaLayerInfo> Candidates { get; set; } = new List<SchemaLayerInfo>();

		#endregion

	}

	#endregion

	#region Class: ImportSchemaResponse

	/// <summary>
	/// Result of <see cref="CreatioApiGateway.ImportSchema"/>.
	/// </summary>
	[DataContract(Name = nameof(ImportSchemaResponse))]
	public class ImportSchemaResponse : BaseResponse
	{

		#region Properties: Public

		/// <summary>Name of the package the schema was written into.</summary>
		[DataMember(Name = "packageName", Order = 10)]
		public string PackageName { get; set; }

		/// <summary>UId of the package the schema was written into.</summary>
		[DataMember(Name = "packageUId", Order = 20)]
		public string PackageUId { get; set; }

		/// <summary>Diagnostic string returned by the platform schema importer.</summary>
		[DataMember(Name = "importResult", Order = 30)]
		public string ImportResult { get; set; }

		#endregion

	}

	#endregion

	#region Class: FindSchemaLayersResponse

	/// <summary>
	/// Result of <see cref="CreatioApiGateway.FindSchemaLayers"/>: every package layer that carries a schema of the
	/// requested name. Read-only; used to decide create-versus-replace before an import writes anything.
	/// </summary>
	[DataContract(Name = nameof(FindSchemaLayersResponse))]
	public class FindSchemaLayersResponse : BaseResponse
	{

		#region Properties: Public

		/// <summary>Matching layers, ordered by package name. Empty when the schema does not exist.</summary>
		[DataMember(Name = "layers", Order = 10)]
		public List<SchemaLayerInfo> Layers { get; set; } = new List<SchemaLayerInfo>();

		#endregion

	}

	#endregion

}
