using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Response for the batch <c>odata-create</c> tool. Carries an aggregate created/failed summary plus
/// a per-row result array so a partial failure never hides the rows that did insert.
/// </summary>
public sealed record ODataCreateBatchResponse {

	/// <summary>Gets the number of rows created.</summary>
	[JsonPropertyName("created")]
	[Description("Number of rows created.")]
	public int Created { get; init; }

	/// <summary>Gets the number of rows that failed.</summary>
	[JsonPropertyName("failed")]
	[Description("Number of rows that failed.")]
	public int Failed { get; init; }

	/// <summary>
	/// Gets the number of failed rows whose side effect could NOT be verified (<c>record-created: null</c>).
	/// </summary>
	/// <remarks>
	/// A subset of <see cref="Failed"/>, not an additional bucket. Non-zero means at least one row may have been
	/// inserted despite the reported failure, so the batch is NOT safe to re-send as-is.
	/// </remarks>
	[JsonPropertyName("unverified")]
	[Description("Failed rows whose side effect could not be verified (a subset of 'failed'). Non-zero means the "
		+ "batch must NOT be blindly re-sent: verify with odata-read first, a retry may duplicate.")]
	public int Unverified { get; init; }

	/// <summary>Gets the per-row outcomes for every attempted row, in input order.</summary>
	[JsonPropertyName("results")]
	[Description("Per-row outcomes for every attempted row, in input order.")]
	public IReadOnlyList<ODataRowResult> Results { get; init; } = [];

	/// <summary>Gets a request-level error that prevented any row from being attempted.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented any row from being attempted.")]
	public string? Error { get; init; }

	/// <summary>Builds a response from per-row outcomes.</summary>
	public static ODataCreateBatchResponse From(IReadOnlyList<ODataRowResult> results) =>
		new() {
			Created = results.Count(result => result.Success),
			Failed = results.Count(result => !result.Success),
			Unverified = results.Count(result => !result.Success && result.RecordCreated is null),
			Results = results
		};

	/// <summary>Builds a response for a request-level failure (no row was attempted).</summary>
	public static ODataCreateBatchResponse RequestError(string message) =>
		new() { Error = message };
}

/// <summary>Per-row outcome inside an <see cref="ODataCreateBatchResponse"/>.</summary>
public sealed record ODataRowResult {

	/// <summary>Gets the zero-based index of the row in the input array.</summary>
	[JsonPropertyName("index")]
	[Description("Zero-based index of the row in the input array.")]
	public int Index { get; init; }

	/// <summary>Gets a value indicating whether the row was inserted.</summary>
	[JsonPropertyName("success")]
	[Description("Whether the row was inserted.")]
	public bool Success { get; init; }

	/// <summary>Gets the primary key of the created record when known.</summary>
	[JsonPropertyName("id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Primary key of the created record, when known.")]
	public string? Id { get; init; }

	/// <summary>Gets the failure reason when the row was not inserted.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Failure reason when the row was not inserted.")]
	public string? Error { get; init; }

	/// <summary>
	/// Gets the side-effect outcome for this row: <c>true</c> inserted, <c>false</c> definitely not inserted,
	/// <c>null</c> unknown.
	/// </summary>
	/// <remarks>
	/// <para>Deliberately NOT suppressed when null, unlike the sibling nullable members: <c>null</c> here is the
	/// load-bearing "unknown" state, and omitting the property would make it indistinguishable from a response
	/// shape that never carried it.</para>
	/// <para>The distinction exists because a Creatio OData POST can return an error AFTER the row is written -
	/// an entity event handler that throws post-insert is reported as a failed request while the record persists.
	/// Reporting that as a plain failure invites a retry that silently duplicates the row (observed on
	/// <c>MailboxSyncSettings</c>: three "failed" calls produced three records). So the writer never claims
	/// not-inserted unless it knows: <c>false</c> is reserved for rows rejected locally, before any request.</para>
	/// </remarks>
	[JsonPropertyName("record-created")]
	[Description("Side effect for this row: true inserted, false definitely not inserted (rejected locally before "
		+ "any request), null UNKNOWN - the server failed the call but may already have written the record.")]
	public bool? RecordCreated { get; init; }

	/// <summary>Gets the agent-actionable next step, set only when <see cref="RecordCreated"/> is unknown.</summary>
	[JsonPropertyName("retry-guidance")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Next step when record-created is null. Never blind-retry an unknown row.")]
	public string? RetryGuidance { get; init; }
}
