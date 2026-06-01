using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>Common base for all printable MCP tool args — carries the registered environment name.</summary>
public abstract record PrintableBaseArgs {
	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description("Registered clio environment name, e.g. 'dev_5001'.")]
	[Required]
	public required string EnvironmentName { get; init; }
}

/// <summary>
/// Shared optional fields written by both create-printable and update-printable. On create an omitted
/// field is left at its default; on update an omitted field is left unchanged. The tools only send a
/// field to Creatio when it is supplied, so the same definitions serve both flows.
/// </summary>
public abstract record PrintableWritableArgs : PrintableBaseArgs {
	/// <summary>SysModule (section) GUID the report belongs to.</summary>
	[JsonPropertyName("sys-module-id")]
	[Description("GUID of the SysModule (section) the report belongs to. Omit to leave unset/unchanged.")]
	public string? SysModuleId { get; init; }

	/// <summary>Whether the report is offered from the section list.</summary>
	[JsonPropertyName("show-in-section")]
	[Description("Whether the report is offered from the section (list) print menu. Omit to leave unchanged.")]
	public bool? ShowInSection { get; init; }

	/// <summary>Whether the report is offered from the record page.</summary>
	[JsonPropertyName("show-in-card")]
	[Description("Whether the report is offered from the record (card) print menu. Omit to leave unchanged.")]
	public bool? ShowInCard { get; init; }

	/// <summary>Whether the generated document is converted to PDF.</summary>
	[JsonPropertyName("convert-in-pdf")]
	[Description("Whether the generated document is converted to PDF. Omit to leave unchanged.")]
	public bool? ConvertInPdf { get; init; }

	/// <summary>Raw MacrosSettings JSON describing the report columns (passthrough).</summary>
	[JsonPropertyName("macros-settings")]
	[Description("Raw MacrosSettings value describing the report's columns/macros, stored verbatim. This is Creatio's internal column-mapping format (column UIds, value types, filters) and is NOT validated or parsed — supply a value copied from an existing report. Omit to leave unset/unchanged.")]
	public string? MacrosSettings { get; init; }
}

/// <summary>Arguments for <see cref="PrintableListTool"/>.</summary>
public sealed record PrintableListArgs : PrintableBaseArgs {
	/// <summary>Optional filter: SysEntitySchema name the report is bound to (e.g. Contact, Account).</summary>
	[JsonPropertyName("entity-schema-name")]
	[Description("Optional. Narrow the list to printables bound to this entity by SysEntitySchema name (e.g. Contact, Account). Ignored when entity-schema-id is supplied.")]
	public string? EntitySchemaName { get; init; }

	/// <summary>Optional filter: SysEntitySchema GUID the report is bound to.</summary>
	[JsonPropertyName("entity-schema-id")]
	[Description("Optional. Narrow the list to printables bound to this entity by SysEntitySchema GUID. Takes precedence over entity-schema-name.")]
	public string? EntitySchemaId { get; init; }

	/// <summary>Maximum number of records to return (1-100, default 25).</summary>
	[JsonPropertyName("top")]
	[Description("Maximum number of printables to return. Range: 1-100. Default: 25.")]
	public int? Top { get; init; }
}

/// <summary>Arguments for <see cref="PrintableGetTool"/>.</summary>
public sealed record PrintableGetArgs : PrintableBaseArgs {
	/// <summary>GUID of the printable (SysModuleReport) record.</summary>
	[JsonPropertyName("id")]
	[Description("GUID of the printable (SysModuleReport) to inspect.")]
	[Required]
	public required string Id { get; init; }
}

/// <summary>Arguments for <see cref="PrintableCreateTool"/>.</summary>
public sealed record PrintableCreateArgs : PrintableWritableArgs {
	/// <summary>Display caption of the report.</summary>
	[JsonPropertyName("caption")]
	[Description("Display caption of the MS Word report.")]
	[Required]
	public required string Caption { get; init; }

	/// <summary>SysEntitySchema GUID the report is bound to.</summary>
	[JsonPropertyName("entity-schema-id")]
	[Description("GUID of the SysEntitySchema (object) the report is built for. Required — this is the primary link that makes the report appear for that entity.")]
	[Required]
	public required string EntitySchemaId { get; init; }
}

/// <summary>Arguments for <see cref="PrintableUpdateTool"/>.</summary>
public sealed record PrintableUpdateArgs : PrintableWritableArgs {
	/// <summary>GUID of the printable to update.</summary>
	[JsonPropertyName("id")]
	[Description("GUID of the printable (SysModuleReport) to update. Required — a keyless mass update is rejected.")]
	[Required]
	public required string Id { get; init; }

	/// <summary>New caption (optional).</summary>
	[JsonPropertyName("caption")]
	[Description("New display caption. Omit to leave unchanged.")]
	public string? Caption { get; init; }

	/// <summary>New SysEntitySchema GUID (optional).</summary>
	[JsonPropertyName("entity-schema-id")]
	[Description("New SysEntitySchema GUID the report is bound to. Omit to leave unchanged.")]
	public string? EntitySchemaId { get; init; }

	/// <summary>Explicit confirmation gate for this destructive operation.</summary>
	[JsonPropertyName("confirm")]
	[Description("Must be true to authorize this destructive update. When false or omitted, the tool refuses without making any remote call.")]
	public bool Confirm { get; init; }
}

/// <summary>Arguments for <see cref="PrintableDeleteTool"/>.</summary>
public sealed record PrintableDeleteArgs : PrintableBaseArgs {
	/// <summary>GUID of the printable to delete.</summary>
	[JsonPropertyName("id")]
	[Description("GUID of the printable (SysModuleReport) to delete. Required — a keyless mass delete is rejected.")]
	[Required]
	public required string Id { get; init; }

	/// <summary>Explicit confirmation gate for this destructive operation.</summary>
	[JsonPropertyName("confirm")]
	[Description("Must be true to authorize this destructive delete. When false or omitted, the tool refuses without making any remote call.")]
	public bool Confirm { get; init; }
}

/// <summary>Arguments for <see cref="PrintableTemplateUploadTool"/>.</summary>
public sealed record PrintableTemplateUploadArgs : PrintableBaseArgs {
	/// <summary>GUID of the printable whose template is being set.</summary>
	[JsonPropertyName("id")]
	[Description("GUID of the printable (SysModuleReport) whose .docx template is being uploaded. Create the printable first with create-printable.")]
	[Required]
	public required string Id { get; init; }

	/// <summary>Absolute path to the .docx template file on the local machine.</summary>
	[JsonPropertyName("file-path")]
	[Description("Absolute path to the .docx template file on the local machine.")]
	[Required]
	public required string FilePath { get; init; }

	/// <summary>Explicit confirmation gate for this destructive operation.</summary>
	[JsonPropertyName("confirm")]
	[Description("Must be true to authorize the upload. It overwrites the report's existing template. When false or omitted, the tool refuses without making any remote call.")]
	public bool Confirm { get; init; }
}
