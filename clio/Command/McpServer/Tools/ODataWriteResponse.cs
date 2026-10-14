using System.Collections.Generic;
using System.ComponentModel;
using Clio.Common;
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
	JsonElement? Record = null,

	[property: JsonPropertyName("correlation-id")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Identifier for this write, present on success and on failure. The same id tags any debug line written for it.")]
	string? CorrelationId = null) {

	/// <summary>Observed write boundary and safe retry context.</summary>
	[JsonPropertyName("diagnostic"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public DataWriteDiagnostic? Diagnostic { get; init; }

	/// <summary>odata-delete: why the record was not deleted - never empty on a refusal the server answered.</summary>
	[JsonPropertyName("explanation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Explanation { get; init; }

	/// <summary>odata-delete: the rows that still reference the refused record.</summary>
	[JsonPropertyName("referencing"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<Clio.Command.RuntimeVerification.ReferencingRows>? Referencing { get; init; }

	/// <summary>odata-delete with cascade: the owned rows deleted before the record.</summary>
	[JsonPropertyName("cascade-deleted"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<RecordKey>? CascadeDeleted { get; init; }

	/// <summary>odata-delete with ids: one outcome per id.</summary>
	[JsonPropertyName("results"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<ODataDeleteItemResult>? Results { get; init; }

	/// <summary>odata-delete with dry-run: what would be deleted and what blocks it, per id. Nothing was deleted.</summary>
	[JsonPropertyName("plan"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<ODataDeletePlanItem>? Plan { get; init; }

	/// <summary>odata-delete with cascade: owned rows whose DELETE got no answer, so they may or may not be gone.</summary>
	[JsonPropertyName("cascade-unconfirmed"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<RecordKey>? CascadeUnconfirmed { get; init; }

	/// <summary>odata-delete dry-run with cascade: the digest the destructive call must pass back as cascade-plan-hash.</summary>
	[JsonPropertyName("cascade-plan-hash"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? CascadePlanHash { get; init; }

	/// <summary>odata-delete dry-run: what the check cannot see.</summary>
	[JsonPropertyName("note"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Note { get; init; }

	/// <summary>Notes that did not stop the call, for example that a synonym of data was accepted.</summary>
	[JsonPropertyName("warnings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string>? Warnings { get; init; }

	/// <summary>Creates a failure response.</summary>
	public static ODataWriteResponse Failure(string message) => new(false, message);
}
