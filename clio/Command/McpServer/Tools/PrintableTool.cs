using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that lists MS Word printables (reports), optionally scoped to one entity.
/// </summary>
[McpServerToolType]
public sealed class PrintableListTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "list-printables";

	/// <summary>Lists MS Word printables configured in the environment.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"List MS Word printables (reports) from SysModuleReport. " +
		"Optionally narrow to a single entity by entity-schema-id (GUID) or entity-schema-name. " +
		"Returns Id, Caption, FileName, ShowInSection, ShowInCard, ConvertInPDF and the bound entity.")]
	public ODataReadResponse List(
		[Description("Parameters: environment-name (required); entity-schema-name, entity-schema-id, top (optional).")]
		[Required]
		PrintableListArgs args) {
		try {
			if (!string.IsNullOrWhiteSpace(args.EntitySchemaId) && !ODataKeyFormatter.IsGuid(args.EntitySchemaId.Trim())) {
				return ODataReadResponse.Failure("entity-schema-id must be a GUID when provided.");
			}

			var (client, urlBuilder) = ODataKeyedWrite.ResolveClients(commandResolver, args.EnvironmentName);

			string filter = PrintableSupport.BuildMsWordFilter(args.EntitySchemaId, args.EntitySchemaName);
			int top = args.Top is > 0 and <= 100 ? args.Top.Value : 25;
			var parts = new List<string> {
				$"$filter={Uri.EscapeDataString(filter)}",
				$"$select={Uri.EscapeDataString("Id,Caption,FileName,ShowInSection,ShowInCard,ConvertInPDF,SysEntitySchemaId")}",
				$"$expand={Uri.EscapeDataString("Type($select=Name),SysEntitySchema($select=Name)")}",
				$"$top={top}"
			};
			string path = $"odata/{PrintableSupport.EntityName}?{string.Join("&", parts)}";
			string responseJson = client.ExecuteGetRequest(urlBuilder.Build(path), 30_000);
			return PrintableSupport.ParseRead(responseJson);
		} catch (Exception ex) {
			return ODataReadResponse.Failure(ex.Message);
		}
	}
}

/// <summary>
/// MCP tool that returns a single MS Word printable with its bound entity, section and type.
/// </summary>
[McpServerToolType]
public sealed class PrintableGetTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "get-printable";

	/// <summary>Reads a single printable by GUID.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Get a single printable (SysModuleReport) by its GUID, expanded with its Type, bound " +
		"SysEntitySchema and SysModule. A non-empty FileName indicates a template is attached.")]
	public ODataReadResponse Get(
		[Description("Parameters: id, environment-name (both required).")]
		[Required]
		PrintableGetArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.Id) || !ODataKeyFormatter.IsGuid(args.Id.Trim())) {
				return ODataReadResponse.Failure("id is required and must be a record GUID.");
			}

			var (client, urlBuilder) = ODataKeyedWrite.ResolveClients(commandResolver, args.EnvironmentName);

			string expand = Uri.EscapeDataString("Type($select=Name),SysEntitySchema($select=Name),SysModule($select=Caption)");
			string path = $"{ODataKeyFormatter.KeyPath(PrintableSupport.EntityName, args.Id)}?$expand={expand}";
			string responseJson = client.ExecuteGetRequest(urlBuilder.Build(path), 30_000);
			return PrintableSupport.ParseRead(responseJson);
		} catch (Exception ex) {
			return ODataReadResponse.Failure(ex.Message);
		}
	}
}

/// <summary>
/// MCP tool that creates an MS Word printable (SysModuleReport). The .docx template is uploaded
/// separately with <see cref="PrintableTemplateUploadTool"/>.
/// </summary>
[McpServerToolType]
public sealed class PrintableCreateTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "create-printable";

	/// <summary>Creates an MS Word printable record.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description(
		"Create an MS Word printable (SysModuleReport). The report Type is set to MS Word automatically. " +
		"Requires caption and entity-schema-id (the bound object). " +
		"The .docx template is uploaded separately with upload-report-template after creation. " +
		"macros-settings (report columns) is an optional raw passthrough and is not validated.")]
	public ODataWriteResponse Create(
		[Description("Parameters: caption, entity-schema-id, environment-name (required); sys-module-id, show-in-section, show-in-card, convert-in-pdf, macros-settings (optional).")]
		[Required]
		PrintableCreateArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.Caption)) {
				return ODataWriteResponse.Failure("caption is required.");
			}
			if (string.IsNullOrWhiteSpace(args.EntitySchemaId) || !ODataKeyFormatter.IsGuid(args.EntitySchemaId.Trim())) {
				return ODataWriteResponse.Failure("entity-schema-id is required and must be a GUID.");
			}
			if (!string.IsNullOrWhiteSpace(args.SysModuleId) && !ODataKeyFormatter.IsGuid(args.SysModuleId.Trim())) {
				return ODataWriteResponse.Failure("sys-module-id must be a GUID when provided.");
			}

			var (client, urlBuilder) = ODataKeyedWrite.ResolveClients(commandResolver, args.EnvironmentName);

			string typeId = PrintableSupport.ResolveMsWordTypeId(client, urlBuilder);
			var body = new Dictionary<string, object?> {
				["Caption"] = args.Caption.Trim(),
				["TypeId"] = typeId,
				["SysEntitySchemaId"] = args.EntitySchemaId.Trim()
			};
			if (!string.IsNullOrWhiteSpace(args.SysModuleId)) {
				body["SysModuleId"] = args.SysModuleId.Trim();
			}
			if (args.ShowInSection.HasValue) {
				body["ShowInSection"] = args.ShowInSection.Value;
			}
			if (args.ShowInCard.HasValue) {
				body["ShowInCard"] = args.ShowInCard.Value;
			}
			if (args.ConvertInPdf.HasValue) {
				body["ConvertInPDF"] = args.ConvertInPdf.Value;
			}
			// MacrosSettings is stored verbatim — it is Creatio's internal column-mapping format, not validated here.
			if (!string.IsNullOrWhiteSpace(args.MacrosSettings)) {
				body["MacrosSettings"] = args.MacrosSettings;
			}

			string url = urlBuilder.Build(ODataKeyFormatter.CollectionPath(PrintableSupport.EntityName));
			string responseJson = client.ExecutePostRequest(url, JsonSerializer.Serialize(body), 30_000);
			return PrintableSupport.ParseCreated(responseJson);
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(ex.Message);
		}
	}
}

/// <summary>
/// MCP tool that updates an MS Word printable. Only supplied fields change; Type is never altered.
/// </summary>
[McpServerToolType]
public sealed class PrintableUpdateTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "update-printable";

	/// <summary>Updates a single printable record.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description(
		"Update a single printable (SysModuleReport) by GUID; only supplied fields change and the report Type is never altered. " +
		"This is a destructive operation: it requires confirm=true to proceed. " +
		"The .docx template is managed separately with upload-report-template.")]
	public ODataWriteResponse Update(
		[Description("Parameters: id, environment-name (required); caption, entity-schema-id, sys-module-id, show-in-section, show-in-card, convert-in-pdf, macros-settings, confirm (optional).")]
		[Required]
		PrintableUpdateArgs args) {
		try {
			ODataWriteResponse invalidTarget = ODataKeyedWrite.ValidateTarget(PrintableSupport.EntityName, args.Id, "update");
			if (invalidTarget is not null) {
				return invalidTarget;
			}

			var body = new Dictionary<string, object?>();
			if (!string.IsNullOrWhiteSpace(args.Caption)) {
				body["Caption"] = args.Caption.Trim();
			}
			if (!string.IsNullOrWhiteSpace(args.EntitySchemaId)) {
				if (!ODataKeyFormatter.IsGuid(args.EntitySchemaId.Trim())) {
					return ODataWriteResponse.Failure("entity-schema-id must be a GUID when provided.");
				}
				body["SysEntitySchemaId"] = args.EntitySchemaId.Trim();
			}
			if (!string.IsNullOrWhiteSpace(args.SysModuleId)) {
				if (!ODataKeyFormatter.IsGuid(args.SysModuleId.Trim())) {
					return ODataWriteResponse.Failure("sys-module-id must be a GUID when provided.");
				}
				body["SysModuleId"] = args.SysModuleId.Trim();
			}
			if (args.ShowInSection.HasValue) {
				body["ShowInSection"] = args.ShowInSection.Value;
			}
			if (args.ShowInCard.HasValue) {
				body["ShowInCard"] = args.ShowInCard.Value;
			}
			if (args.ConvertInPdf.HasValue) {
				body["ConvertInPDF"] = args.ConvertInPdf.Value;
			}
			if (args.MacrosSettings is not null) {
				body["MacrosSettings"] = args.MacrosSettings;
			}

			if (body.Count == 0) {
				return ODataWriteResponse.Failure(
					"No fields to update. Provide at least one of: caption, entity-schema-id, sys-module-id, show-in-section, show-in-card, convert-in-pdf, macros-settings.");
			}

			ODataWriteResponse notConfirmed = PrintableSupport.RequireConfirmation(args.Confirm, ToolName, args.Id, "change");
			if (notConfirmed is not null) {
				return notConfirmed;
			}

			(IApplicationClient client, string url) = ODataKeyedWrite.ResolveTarget(
				commandResolver, args.EnvironmentName, PrintableSupport.EntityName, args.Id);
			client.ExecutePatchRequest(url, JsonSerializer.Serialize(body), 30_000);
			return new ODataWriteResponse(true, null, args.Id.Trim());
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(ex.Message);
		}
	}
}

/// <summary>
/// MCP tool that deletes a single MS Word printable by GUID.
/// </summary>
[McpServerToolType]
public sealed class PrintableDeleteTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "delete-printable";

	/// <summary>Deletes a single printable record.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description(
		"Delete a single printable (SysModuleReport) by GUID; this tool never performs a keyless mass delete. " +
		"This is a destructive operation: it requires confirm=true to proceed. " +
		"Use list-printables or get-printable to obtain the Id first.")]
	public ODataWriteResponse Delete(
		[Description("Parameters: id, environment-name (required); confirm.")]
		[Required]
		PrintableDeleteArgs args) {
		try {
			ODataWriteResponse invalidTarget = ODataKeyedWrite.ValidateTarget(PrintableSupport.EntityName, args.Id, "delete");
			if (invalidTarget is not null) {
				return invalidTarget;
			}

			ODataWriteResponse notConfirmed = PrintableSupport.RequireConfirmation(args.Confirm, ToolName, args.Id, "deletion");
			if (notConfirmed is not null) {
				return notConfirmed;
			}

			(IApplicationClient client, string url) = ODataKeyedWrite.ResolveTarget(
				commandResolver, args.EnvironmentName, PrintableSupport.EntityName, args.Id);
			client.ExecuteDeleteRequest(url, string.Empty, 30_000);
			return new ODataWriteResponse(true, null, args.Id.Trim());
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(ex.Message);
		}
	}
}
