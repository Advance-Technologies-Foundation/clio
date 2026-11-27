using System.Collections.Generic;
using Clio.Common;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Response for the batch <c>odata-create</c> tool. Carries an aggregate created/failed summary plus
/// a per-row result array so a partial failure never hides the rows that did insert.
/// </summary>
public sealed record ODataCreateBatchResponse {

	/// <summary>Safe context for a request-level refusal before writes.</summary>
	[JsonPropertyName("diagnostic"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public DataWriteDiagnostic? Diagnostic { get; init; }

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

	/// <summary>Gets the number of created rows with at least one field stored differently than sent; null when verify=false.</summary>
	[JsonPropertyName("diverged")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Created rows whose read-back found at least one field stored with a different value than sent (see each row's "
		+ "'diverged'). Absent when verify=false.")]
	public int? Diverged { get; init; }

	/// <summary>Gets the per-row outcomes for every attempted row, in input order.</summary>
	[JsonPropertyName("results")]
	[Description("Per-row outcomes for every attempted row, in input order.")]
	public IReadOnlyList<ODataRowResult> Results { get; init; } = [];

	/// <summary>Gets a request-level error that prevented any row from being attempted.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Request-level error that prevented any row from being attempted.")]
	public string? Error { get; init; }

	/// <summary>Gets the identifier for this batch, present on success and on failure.</summary>
	[JsonPropertyName("correlation-id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Identifier for this batch, present on success and on failure. The same id tags any debug line written for it.")]
	public string? CorrelationId { get; init; }

	/// <summary>Gets notes that did not stop the call, for example that an argument synonym was accepted.</summary>
	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Notes that did not stop the call, for example that a synonym such as payload was accepted as rows. Absent when there is nothing to note.")]
	public IReadOnlyList<string>? Warnings { get; init; }

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

	/// <summary>Safe context for this input row's write boundary.</summary>
	[JsonPropertyName("diagnostic"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public DataWriteDiagnostic? Diagnostic { get; init; }

	/// <summary>What the site itself answered to this failed write; see <see cref="Clio.Common.SiteResponse"/>.</summary>
	[JsonPropertyName("site-response"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("What the Creatio site itself answered to the failed write: status, and its own message and code when the body carries them. Absent on success and when no HTTP response exists.")]
	public SiteResponse? SiteResponse { get; init; }

	[JsonIgnore]
	internal bool ResponseReceived { get; init; }

	/// <summary>Whether the created row was read back and compared; absent for a failed row or when verify=false.</summary>
	[JsonPropertyName("verified")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Created rows only: true when the row was read back after the batch and at least one of its fields compared with what was sent; "
		+ "false when the read-back failed, compared none of them or the batch budget ran out (the row is still created - see verification-note). "
		+ "Absent for a failed row and when verify=false.")]
	public bool? Verified { get; init; }

	/// <summary>The sent fields Creatio stored with a different value for this row.</summary>
	[JsonPropertyName("diverged")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Sent fields Creatio stored with a different value, typically replaced by an entity rule during the save. "
		+ "Re-sending the row does not change them and creates a duplicate. Absent when every compared field matched.")]
	public IReadOnlyList<ODataFieldDivergence>? Diverged { get; init; }

	/// <summary>Why the row's read-back did not verify it, or which sent fields it did not compare.</summary>
	[JsonPropertyName("verification-note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Why the row's read-back did not verify it, or which sent fields it did not compare.")]
	public string? VerificationNote { get; init; }

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
	/// not-inserted unless it knows: <c>false</c> is reserved for rows rejected locally, before any request, and
	/// for rows Creatio rejected while binding the request body, which happens before any write.</para>
	/// </remarks>
	[JsonPropertyName("record-created")]
	[Description("Side effect for this row: true inserted, false definitely not inserted (rejected locally before "
		+ "any request, or rejected by Creatio while reading the request body), null UNKNOWN - the server failed "
		+ "the call but may already have written the record.")]
	public bool? RecordCreated { get; init; }

	/// <summary>
	/// Gets the agent-actionable next step, set when <see cref="RecordCreated"/> is unknown or when Creatio
	/// rejected the row before writing it.
	/// </summary>
	[JsonPropertyName("retry-guidance")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Description("Next step when record-created is null, or when Creatio rejected the row before writing it. "
		+ "Never blind-retry an unknown row.")]
	public string? RetryGuidance { get; init; }
}
