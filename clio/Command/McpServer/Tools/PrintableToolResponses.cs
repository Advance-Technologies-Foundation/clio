using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Response for <see cref="PrintableTemplateUploadTool"/>. The CRUD printable tools reuse the shared
/// OData responses (<see cref="ODataReadResponse"/> for list/get, <see cref="ODataWriteResponse"/>
/// for create/update/delete); only the template upload needs its own shape.
/// </summary>
public sealed record PrintableTemplateUploadResponse(
	[property: JsonPropertyName("success")]
	[property: Description("Whether the template upload succeeded.")]
	bool Success,

	[property: JsonPropertyName("error")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Error message when success is false.")]
	string? Error,

	[property: JsonPropertyName("id")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("GUID of the printable (SysModuleReport) whose template was uploaded.")]
	string? Id = null,

	[property: JsonPropertyName("file-name")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Name of the uploaded .docx template file.")]
	string? FileName = null) {

	/// <summary>Creates a failure response.</summary>
	public static PrintableTemplateUploadResponse Failure(string message) => new(false, message);
}
