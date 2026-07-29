using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared constants, type resolution, OData response parsing and the confirmation gate used by the
/// printable-* MCP tools, which configure MS Word printables (the <c>SysModuleReport</c> entity plus
/// the .docx template stored in its <c>File</c> stream column).
/// </summary>
internal static class PrintableSupport {

	/// <summary>OData entity set holding the printable (report) records.</summary>
	internal const string EntityName = "SysModuleReport";

	/// <summary>Lookup entity that classifies a report (MS Word vs FastReport).</summary>
	internal const string TypeEntityName = "SysModuleReportType";

	/// <summary>
	/// Well-known Id of the "MS Word" <see cref="TypeEntityName"/> record. Used as a fast-path
	/// fallback when the runtime lookup by name fails; the lookup remains primary so custom or
	/// localized installations still resolve correctly.
	/// </summary>
	internal const string MsWordTypeId = "8bc259ef-4276-4906-b7a6-23dc59be7fe2";

	/// <summary>Display name of the MS Word report type.</summary>
	internal const string MsWordTypeName = "MS Word";

	/// <summary>MIME type Creatio expects for a Word .docx template.</summary>
	internal const string WordMimeType =
		"application/vnd.openxmlformats-officedocument.wordprocessingml.document";

	/// <summary>
	/// Relative path of the design service endpoint that ingests a .docx template into a report's
	/// <c>File</c> stream column. The chunked POST contract is mirrored from Creatio's own
	/// MSWordReportDesigner service client.
	/// </summary>
	internal const string UploadTemplatePath = "rest/WordReportingDesignService/UploadReportTemplate";

	/// <summary>Stream column on <see cref="EntityName"/> that stores the template bytes.</summary>
	internal const string TemplateColumnName = "File";

	/// <summary>
	/// Metadata columns a printable read projects. Explicit so the <see cref="TemplateColumnName"/>
	/// stream column is never pulled into an MCP response: the default projection would ship the whole
	/// .docx payload for every report that has a template attached. <c>FileName</c> is the metadata that
	/// tells a caller whether a template exists.
	/// </summary>
	internal const string MetadataSelect =
		"Id,Caption,FileName,ShowInSection,ShowInCard,ConvertInPDF,MacrosSettings,SysEntitySchemaId,SysModuleId,TypeId";

	/// <summary>Entity schema the chunked upload writes through (the report template store).</summary>
	internal const string TemplateUploadEntitySchema = "SysReportTemplate";

	/// <summary>SysSetting name the upload service uses to enforce the maximum file size.</summary>
	internal const string MaxFileSizeSettingName = "FileImportMaxFileSize";

	/// <summary>
	/// Resolves the Id of the "MS Word" report type. Tries a runtime OData lookup by name first;
	/// on any failure falls back to the well-known <see cref="MsWordTypeId"/> constant.
	/// </summary>
	internal static string ResolveMsWordTypeId(IApplicationClient client, IServiceUrlBuilder urlBuilder) {
		try {
			string filter = Uri.EscapeDataString($"Name eq '{EscapeLiteral(MsWordTypeName)}'");
			string path = $"odata/{TypeEntityName}?$select=Id&$filter={filter}&$top=1";
			string json = client.ExecuteGetRequest(urlBuilder.Build(path), 30_000);
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;
			if (root.TryGetProperty("value", out JsonElement value)
				&& value.ValueKind == JsonValueKind.Array
				&& value.GetArrayLength() > 0
				&& value[0].TryGetProperty("Id", out JsonElement idEl)
				&& idEl.ValueKind == JsonValueKind.String) {
				string? id = idEl.GetString();
				if (!string.IsNullOrEmpty(id)) {
					return id;
				}
			}
		} catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException
			or InvalidOperationException or WebException) {
			// The lookup is a best-effort optimisation over the well-known constant, so a transport or
			// payload failure must fall through rather than fail the whole create. Narrowed deliberately:
			// a bare catch would also swallow OutOfMemoryException / cancellation-by-the-host and make
			// this method report success on a genuinely broken process (PR #651 review).
		}
		return MsWordTypeId;
	}

	/// <summary>Escapes a string literal for an OData single-quoted value.</summary>
	internal static string EscapeLiteral(string value) => value.Replace("'", "''");

	/// <summary>
	/// Enforces the confirmation gate for a destructive printable operation, naming the actual tool
	/// to re-call. Returns a failure response when <paramref name="confirm"/> is false, otherwise <c>null</c>.
	/// </summary>
	internal static ODataWriteResponse RequireConfirmation(bool confirm, string toolName, string id, string consequence) {
		if (confirm) {
			return null;
		}
		return ODataWriteResponse.Failure(
			$"Refusing to modify {EntityName}({id.Trim()}) without confirmation. " +
			$"This is a destructive operation; re-call {toolName} with \"confirm\": true to authorize this {consequence}.");
	}

	/// <summary>Parses a Creatio OData read response into an <see cref="ODataReadResponse"/>.</summary>
	internal static ODataReadResponse ParseRead(string json) => ODataResponseParser.ParseODataRead(json);

	/// <summary>Parses a Creatio OData create response into an <see cref="ODataWriteResponse"/>.</summary>
	internal static ODataWriteResponse ParseCreated(string json) => ODataResponseParser.ParseODataCreated(json);

	/// <summary>
	/// Builds the fully-qualified query string for the chunked template upload, mirroring the
	/// contract of Creatio's MSWordReportDesigner service client. <paramref name="fileId"/> is a
	/// fresh GUID identifying this upload; <paramref name="totalLength"/> is the file size in bytes.
	/// </summary>
	internal static string BuildUploadQuery(string reportId, Guid fileId, long totalLength, string fileName) {
		var query = new Dictionary<string, string> {
			["columnName"] = TemplateColumnName,
			["fileId"] = fileId.ToString(),
			["mimeType"] = WordMimeType,
			["parentColumnName"] = "Id",
			["reportId"] = reportId,
			["parentColumnValue"] = fileId.ToString(),
			["totalFileLength"] = totalLength.ToString(CultureInfo.InvariantCulture),
			["entitySchemaName"] = TemplateUploadEntitySchema,
			["fileName"] = fileName,
			["maxFileSizeSysSettingsName"] = MaxFileSizeSettingName,
			["additionalParams"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["ReportId"] = reportId })
		};
		return string.Join("&", query.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
	}

	/// <summary>
	/// Parses the design service upload response (<c>{ "success": bool, "errorInfo": { "message" } }</c>).
	/// Fails <b>closed</b>: only an explicit <c>"success": true</c> counts as a stored template. An
	/// empty body, a plain-text body, or JSON without a boolean <c>success</c> (a reverse-proxy page,
	/// an expired-session HTML login page — all of which can arrive with HTTP 200) is reported as a
	/// failure. Reporting success here is not a cosmetic mistake: the caller then patches
	/// <c>FileName</c>, and a non-empty <c>FileName</c> is exactly how <c>get-printable</c> tells a
	/// caller a template is attached, so the verification path would corroborate the wrong answer.
	/// </summary>
	internal static PrintableTemplateUploadResponse ParseUploadResponse(string json, string reportId, string fileName) {
		if (string.IsNullOrWhiteSpace(json)) {
			return PrintableTemplateUploadResponse.Failure(UnconfirmedUploadError("the service returned an empty body"));
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;
			if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("success", out JsonElement s)) {
				if (s.ValueKind == JsonValueKind.True) {
					return new PrintableTemplateUploadResponse(true, null, reportId, fileName);
				}
				if (s.ValueKind == JsonValueKind.False) {
					string message = "Template upload failed.";
					if (root.TryGetProperty("errorInfo", out JsonElement err) && err.ValueKind == JsonValueKind.Object
						&& err.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String) {
						message = m.GetString() ?? message;
					}
					return PrintableTemplateUploadResponse.Failure(message);
				}
			}
			return PrintableTemplateUploadResponse.Failure(UnconfirmedUploadError($"unexpected response: {Preview(json)}"));
		} catch (JsonException) {
			return PrintableTemplateUploadResponse.Failure(UnconfirmedUploadError($"non-JSON response: {Preview(json)}"));
		}
	}

	/// <summary>
	/// Builds the failure message used when the upload response carries no explicit
	/// <c>"success": true</c>, so the caller knows the template may or may not have landed.
	/// </summary>
	private static string UnconfirmedUploadError(string detail) =>
		SensitiveErrorTextRedactor.Redact(
			$"Template upload was not confirmed by the design service ({detail}). " +
			"The template may not have been stored; verify with get-printable before retrying.");

	/// <summary>Truncates a response body so a long or binary payload never floods the tool result.</summary>
	private static string Preview(string json) {
		string trimmed = json.Trim();
		return trimmed.Length > 200 ? trimmed[..200] + "..." : trimmed;
	}

}
