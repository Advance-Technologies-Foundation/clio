using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared response for the OData write tools (odata-create / odata-update / odata-delete).
/// </summary>
public sealed record ODataWriteResponse(
	[property: JsonPropertyName("success")]
	[property: Description("Whether the OData write succeeded.")]
	bool Success,

	[property: JsonPropertyName("error")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Error message when success is false.")]
	string? Error = null,

	[property: JsonPropertyName("id")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Primary key of the affected record, when known.")]
	string? Id = null,

	[property: JsonPropertyName("record")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("The record returned by Creatio (populated by odata-create).")]
	JsonElement? Record = null) {

	/// <summary>Creates a failure response.</summary>
	public static ODataWriteResponse Failure(string message) => new(false, message);
}
