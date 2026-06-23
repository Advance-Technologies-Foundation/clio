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
}
