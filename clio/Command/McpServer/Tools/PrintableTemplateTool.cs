using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that uploads a .docx template into a printable's <c>File</c> stream column via the
/// WordReportingDesignService chunked upload endpoint.
/// </summary>
[McpServerToolType]
public sealed class PrintableTemplateUploadTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "upload-report-template";

	/// <summary>Uploads a .docx template for an existing printable.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description(
		"Upload a .docx template for an existing MS Word printable (SysModuleReport), set with create-printable. " +
		"The file is sent to WordReportingDesignService/UploadReportTemplate. " +
		"This overwrites any existing template, so it requires confirm=true to proceed.")]
	public PrintableTemplateUploadResponse Upload(
		[Description("Parameters: id, file-path, environment-name (required); confirm.")]
		[Required]
		PrintableTemplateUploadArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.Id) || !ODataKeyFormatter.IsGuid(args.Id.Trim())) {
				return PrintableTemplateUploadResponse.Failure("id is required and must be a report GUID.");
			}
			if (string.IsNullOrWhiteSpace(args.FilePath)) {
				return PrintableTemplateUploadResponse.Failure("file-path is required.");
			}
			string filePath = args.FilePath.Trim();
			if (!filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) {
				return PrintableTemplateUploadResponse.Failure("file-path must point to a .docx file.");
			}
			if (!args.Confirm) {
				return PrintableTemplateUploadResponse.Failure(
					$"Refusing to upload a template to {PrintableSupport.EntityName}({args.Id.Trim()}) without confirmation. " +
					$"This overwrites the report's existing template; re-call {ToolName} with \"confirm\": true to authorize this change.");
			}
			if (!File.Exists(filePath)) {
				return PrintableTemplateUploadResponse.Failure($"File not found: {filePath}");
			}

			var (client, urlBuilder) = ODataKeyedWrite.ResolveClients(commandResolver, args.EnvironmentName);

			string reportId = args.Id.Trim();
			string fileName = Path.GetFileName(filePath);
			long totalLength = new FileInfo(filePath).Length;
			Guid fileId = Guid.NewGuid();

			string query = PrintableSupport.BuildUploadQuery(reportId, fileId, totalLength, fileName);
			string url = $"{urlBuilder.Build(PrintableSupport.UploadTemplatePath)}?{query}";

			string responseJson = client.UploadAlmFileByChunk(url, filePath);
			PrintableTemplateUploadResponse result = PrintableSupport.ParseUploadResponse(responseJson, reportId, fileName);

			if (result.Success) {
				// The design service stores the template bytes in SysModuleReport.File but leaves
				// FileName empty; patch it so the report reads as having a template attached
				// (the Printables UI and get-printable surface FileName). Best-effort: a failed
				// patch must not turn a successful upload into a failure.
				try {
					string patchUrl = urlBuilder.Build(ODataKeyFormatter.KeyPath(PrintableSupport.EntityName, reportId));
					string patchBody = JsonSerializer.Serialize(new Dictionary<string, object?> { ["FileName"] = fileName });
					client.ExecutePatchRequest(patchUrl, patchBody, 30_000);
				} catch {
					// FileName is a convenience indicator only; ignore patch failures.
				}
			}
			return result;
		} catch (Exception ex) {
			return PrintableTemplateUploadResponse.Failure(ex.Message);
		}
	}
}
