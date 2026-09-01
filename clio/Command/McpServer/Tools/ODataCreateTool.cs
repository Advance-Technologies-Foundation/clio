using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for creating one or more Creatio records via OData v4 (HTTP POST) in a single call.
/// </summary>
[McpServerToolType]
public sealed class ODataCreateTool(IToolCommandResolver commandResolver, IODataFileContract fileContract) {

	//File I/O is behaviour, so it arrives through DI rather than being reached statically: that is what lets
	//a failure-path test substitute a file-contract fake instead of driving the production write plumbing.
	private readonly IODataFileContract _fileContract =
		fileContract ?? throw new ArgumentNullException(nameof(fileContract));

	internal const string ToolName = "odata-create";

	/// <summary>
	/// Largest number of rows one call may carry. The byte limit on a file-backed payload bounds size, not
	/// cardinality: a 10 MB array of tiny objects is still over a million sequential POSTs from a single MCP
	/// call. A caller with more rows than this is told to chunk them.
	/// </summary>
	internal const int MaxRowCount = 1000;

	/// <summary>
	/// <see cref="MaxRowCount"/> as text. A const interpolated string cannot take an int hole, so the two are
	/// pinned to each other by a test rather than by the compiler.
	/// </summary>
	internal const string MaxRowCountText = "1000";

	/// <summary>
	/// The single wording of the row ceiling. Every agent-facing surface - the tool description, the argument
	/// descriptions and the curated contract - is built from THIS constant, so a caller cannot read the limit
	/// off one surface and be rejected by another.
	/// </summary>
	internal const string RowCountLimitDescription =
		"At most " + MaxRowCountText + " rows per call: a larger array is rejected before the environment is "
		+ "resolved and before any POST, so split the input into batches of at most " + MaxRowCountText + " rows.";

	/// <summary>Per-row request timeout, and the ceiling for the remaining-budget cap.</summary>
	internal const int RowRequestTimeoutMs = 30_000;

	/// <summary>
	/// Wall-clock ceiling for one batch. Rows are POSTed SEQUENTIALLY, so the row limit alone bounds nothing
	/// in time: with the default stop-on-error=false, 1000 rows that each hit the per-row timeout would keep
	/// one call running for more than eight hours - long after the MCP caller has disconnected. Once the
	/// budget is spent the remaining rows are reported as not attempted instead of being sent.
	/// </summary>
	internal const int MaxBatchDurationMs = 5 * 60 * 1000;

	private const string CancelledMessage =
		"row was not attempted: the batch was cancelled.";

	private const string DeadlineMessage =
		"row was not attempted: the batch exceeded its "
		+ "wall-clock budget. Re-send the remaining rows as a smaller batch.";

	private const string ValidArgumentsHint =
		"Valid: entity, environment-name, rows, rows-file, stop-on-error.";

	/// <summary>
	/// The camelCase / snake_case spellings an LLM emits for this tool's kebab-case fields. Without this map
	/// (and the overflow bag it reads) a request carrying inline <c>rows</c> plus <c>rows_file</c> bound only
	/// the inline rows, slipped past the mutual-exclusion check, and POSTed an ambiguous request.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> ArgumentAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["environmentName"] = "environment-name",
			["environment_name"] = "environment-name",
			["rowsFile"] = "rows-file",
			["rows_file"] = "rows-file",
			["stopOnError"] = "stop-on-error",
			["stop_on_error"] = "stop-on-error"
		};

	/// <summary>
	/// Next step offered when a row's side effect cannot be verified. Kept in one place so every unknown path
	/// gives identical advice.
	/// </summary>
	private const string UnknownSideEffectGuidance =
		"Side effect UNKNOWN: Creatio may have written the record before failing (a post-insert entity event "
		+ "handler that throws is reported as a failed request). Do NOT retry blindly - it may duplicate the row. "
		+ "Read the entity back (odata-read, filtering on the values you sent) and re-send only if it is absent.";

	/// <summary>Creates one or more Creatio records using OData v4.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description(
		"Create one or more Creatio records via OData v4 (POST) in a single call. " +
		"Provide the entity set name and a 'rows' array of field/value objects; pass all rows for the same " +
		"entity in one call rather than one call per row. " + RowCountLimitDescription + " Each row is inserted " +
		"sequentially and reported independently — a failed row does not abort the rest unless 'stop-on-error' is set. " +
		"The batch stops when it exceeds its wall-clock budget; the first row that was not attempted is then " +
		"reported with record-created=false and the reason. " +
		"Returns a created/failed summary and a per-row result array with each created record's Id. " +
		"CRITICAL for failed rows — read 'record-created' before reacting: true inserted, false definitely not " +
		"inserted (rejected locally, safe to fix and re-send), null UNKNOWN. Null means Creatio failed the call " +
		"but may already have written the record, which happens when a post-insert entity event handler throws; " +
		"re-sending such a row DUPLICATES it. On null, read the entity back and re-send only if absent — the " +
		"row's 'retry-guidance' says so too, and the batch's 'unverified' count is how many rows are in that " +
		"state. " +
		"Call get-tool-contract for odata-create to see usage examples and discovery workflow hints.")]
	public ODataCreateBatchResponse Create(
		[Description("Parameters: entity, rows or rows-file, environment-name (required); stop-on-error (optional).")]
		[Required]
		ODataCreateArgs args,
		CancellationToken cancellationToken = default) {
		//Runs before the payload is resolved, before the environment is resolved and before any POST: an
		//unbound file-source key such as rows_file would otherwise be dropped silently and the inline rows
		//sent instead, which is the ambiguous request this rejects.
		string? argumentError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData,
			ArgumentAliases,
			".",
			ValidArgumentsHint);
		if (argumentError is not null) {
			return ODataCreateBatchResponse.RequestError(argumentError);
		}
		if (string.IsNullOrWhiteSpace(args.Entity)) {
			return ODataCreateBatchResponse.RequestError("entity is required.");
		}
		if (!ODataKeyFormatter.IsValidEntityName(args.Entity)) {
			return ODataCreateBatchResponse.RequestError(
				"entity must be a valid OData entity set name (letters, digits, underscore).");
		}
		ODataCreateBatchResponse payloadError = ResolveRequestedRows(args, out JsonElement rows);
		if (payloadError is not null) {
			return payloadError;
		}

		IApplicationClient client;
		IServiceUrlBuilder urlBuilder;
		try {
			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			client = commandResolver.Resolve<IApplicationClient>(options);
			urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);
		} catch (Exception ex) {
			return ODataCreateBatchResponse.RequestError(SensitiveErrorTextRedactor.Redact(ex.Message));
		}

		string url = urlBuilder.Build(ODataKeyFormatter.CollectionPath(args.Entity));
		return ODataCreateBatchResponse.From(PostRows(client, url, rows, args.StopOnError, cancellationToken));
	}

	/// <summary>
	/// POSTs each row in turn and returns the per-row outcomes, stopping early on cancellation, on the batch
	/// deadline, or on the first failure when <paramref name="stopOnError"/> is set.
	/// </summary>
	/// <param name="client">Environment-scoped client used for the POSTs.</param>
	/// <param name="url">Collection endpoint every row is posted to.</param>
	/// <param name="rows">Validated non-empty JSON array of row objects.</param>
	/// <param name="stopOnError">Whether the first failed row aborts the rest.</param>
	/// <param name="cancellationToken">Caller token; the MCP host cancels it when it disconnects.</param>
	/// <returns>Outcomes for every ATTEMPTED row, plus the first unattempted row when the batch stopped early.</returns>
	private static List<ODataRowResult> PostRows(
		IApplicationClient client,
		string url,
		JsonElement rows,
		bool stopOnError,
		CancellationToken cancellationToken) {
		List<ODataRowResult> results = [];
		int index = 0;
		//The bound the batch actually PROMISES is the wall-clock budget: it is deterministic and depends on
		//nothing outside this process. The cancellation token is honoured too, but is deliberately NOT
		//advertised as a guarantee - measured end to end, a cancelled MCP call does not reach the running
		//tool (see docs/knowledge/McpServer/mcp-cancellation-does-not-reach-tools.md), so promising that
		//later rows stop on cancellation would be promising something a caller cannot rely on.
		//Both are checked BETWEEN rows, so a row that is already in flight completes and is reported -
		//abandoning it mid-POST would leave its side effect unknown for no gain.
		Stopwatch elapsed = Stopwatch.StartNew();
		foreach (JsonElement row in rows.EnumerateArray()) {
			int remainingMs = MaxBatchDurationMs - (int)Math.Min(elapsed.ElapsedMilliseconds, MaxBatchDurationMs);
			string abortReason = DescribeAbort(cancellationToken, remainingMs);
			if (abortReason is not null) {
				// Not attempted, so not-inserted is KNOWN - the same shape as a locally rejected row.
				results.Add(new ODataRowResult {
					Index = index,
					Success = false,
					RecordCreated = false,
					Error = abortReason
				});
				break;
			}
			// Cap the per-row timeout to what is left of the batch budget, so the LAST row cannot overshoot
			// the deadline by a further full timeout.
			ODataRowResult result = CreateRow(client, url, row, index, Math.Min(RowRequestTimeoutMs, remainingMs));
			results.Add(result);
			if (!result.Success && stopOnError) {
				break;
			}
			index++;
		}
		return results;
	}

	/// <summary>Reason to stop before the next row, or <see langword="null"/> to keep going.</summary>
	/// <param name="cancellationToken">Caller token.</param>
	/// <param name="remainingMs">Milliseconds left of the batch budget.</param>
	private static string DescribeAbort(CancellationToken cancellationToken, int remainingMs) {
		if (cancellationToken.IsCancellationRequested) {
			return CancelledMessage;
		}
		return remainingMs <= 0 ? DeadlineMessage : null;
	}

	/// <summary>
	/// Resolves the batch payload from the mutually exclusive <c>rows</c> / <c>rows-file</c> pair. Returns
	/// <see langword="null"/> when <paramref name="rows"/> holds a valid non-empty array, otherwise the
	/// request-level failure to hand straight back to the caller.
	/// </summary>
	private ODataCreateBatchResponse ResolveRequestedRows(ODataCreateArgs args, out JsonElement rows) {
		rows = default;
		bool hasRowsFile = !string.IsNullOrWhiteSpace(args.RowsFile);
		if (args.Rows is not null && hasRowsFile) {
			return ODataCreateBatchResponse.RequestError("Provide either rows or rows-file, not both.");
		}
		JsonElement? fileRows = null;
		if (args.Rows is null && hasRowsFile) {
			if (!_fileContract.TryReadJson(args.RowsFile, "rows-file", out string rowsJson, out string fileError)) {
				return ODataCreateBatchResponse.RequestError(fileError);
			}
			try {
				// JsonDocument rents from ArrayPool<byte>; without disposing, the buffers are only returned on a
				// finalizer cycle. Clone() detaches the element from the document, so disposing here is correct.
				using JsonDocument document = JsonDocument.Parse(rowsJson);
				fileRows = document.RootElement.Clone();
			} catch (JsonException ex) {
				return ODataCreateBatchResponse.RequestError($"rows-file must contain valid JSON: {ex.Message}");
			}
		}
		JsonElement? requestedRows = args.Rows ?? fileRows;
		if (requestedRows is not { ValueKind: JsonValueKind.Array } parsedRows || parsedRows.GetArrayLength() == 0) {
			return ODataCreateBatchResponse.RequestError(
				"rows is required and must be a non-empty array of field/value objects.");
		}
		// The byte limit bounds SIZE, not CARDINALITY: a 10 MB array still holds well over a million tiny
		// objects, and every one of them becomes its own sequential POST plus a retained result. Inline rows
		// are bounded in practice by the MCP context; rows-file is exactly the path that is not, so the count
		// is rejected HERE - before the environment is resolved and before any request leaves clio.
		int rowCount = parsedRows.GetArrayLength();
		if (rowCount > MaxRowCount) {
			return ODataCreateBatchResponse.RequestError(
				$"rows contains {rowCount} entries, which exceeds the {MaxRowCount}-row limit for one call. "
				+ "Split the input into chunks of at most " + MaxRowCount + " rows and submit them separately.");
		}
		rows = parsedRows;
		return null;
	}

	private static ODataRowResult CreateRow(
		IApplicationClient client, string url, JsonElement row, int index, int requestTimeoutMs) {
		try {
			if (row.ValueKind != JsonValueKind.Object || !row.EnumerateObject().MoveNext()) {
				return new ODataRowResult {
					Index = index,
					Success = false,
					// Rejected locally: no request left clio, so not-inserted is KNOWN, not assumed.
					RecordCreated = false,
					Error = "row must be a non-empty object of field/value pairs."
				};
			}
			string responseJson = client.ExecutePostRequest(url, row.GetRawText(), requestTimeoutMs);
			return ParseCreated(responseJson, index);
		} catch (Exception ex) {
			// The request may have reached Creatio and been applied before the failure surfaced here, so the
			// side effect is unknown - never report not-inserted from a transport-level failure.
			return new ODataRowResult {
				Index = index,
				Success = false,
				RecordCreated = null,
				RetryGuidance = UnknownSideEffectGuidance,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message)
			};
		}
	}

	private static ODataRowResult ParseCreated(string json, int index) {
		if (string.IsNullOrWhiteSpace(json)) {
			return new ODataRowResult { Index = index, Success = true, RecordCreated = true };
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;
			if (ODataResponseError.TryDetect(root, out string serverError)) {
				// Redact like the sibling error paths: a routing Message can embed the absolute request
				// URI (host/port/app path), which must not leak into the MCP transcript or logs.
				return new ODataRowResult {
					Index = index,
					Success = false,
					RecordCreated = null,
					RetryGuidance = UnknownSideEffectGuidance,
					Error = SensitiveErrorTextRedactor.Redact(serverError)
				};
			}
			// The primary key is normally a GUID string, but some entities key on a numeric column;
			// accept either representation so a created record is never misreported as a failure.
			string? id = root.TryGetProperty("Id", out JsonElement idEl)
				? idEl.ValueKind switch {
					JsonValueKind.String => idEl.GetString(),
					JsonValueKind.Number => idEl.GetRawText(),
					_ => null
				}
				: null;
			if (string.IsNullOrEmpty(id)) {
				// A successful OData create always echoes the new record with its Id; its absence
				// means the body is not a created record (an unrecognized error or empty payload).
				// Redact: an unrecognized error shape reaching this fallback embeds up to 500 raw
				// response characters, which can carry the absolute request URI or other host detail —
				// keep redaction parity with the TryDetect and exception paths in this method.
				return new ODataRowResult {
					Index = index,
					Success = false,
					RecordCreated = null,
					RetryGuidance = UnknownSideEffectGuidance,
					Error = SensitiveErrorTextRedactor.Redact($"OData create did not return a record Id. Response: {ODataResponseError.Truncate(json)}")
				};
			}
			return new ODataRowResult { Index = index, Success = true, RecordCreated = true, Id = id };
		} catch (JsonException) {
			// A non-JSON body never comes from Creatio's OData pipeline by itself - even a server
			// error is one of the JSON shapes ODataResponseError.TryDetect recognizes. This shape means
			// the request did not reach Creatio intact (a proxy/IIS/routing error, or a session
			// redirect), so the row's side effect is UNKNOWN, not a confirmed create - never report
			// record-created here.
			return new ODataRowResult {
				Index = index,
				Success = false,
				RecordCreated = null,
				RetryGuidance = UnknownSideEffectGuidance,
				Error = SensitiveErrorTextRedactor.Redact(ODataResponseError.DescribeNonJsonResponse(json))
			};
		}
	}
}

/// <summary>Arguments for <see cref="ODataCreateTool"/>.</summary>
public sealed record ODataCreateArgs {
	/// <summary>Creatio OData entity set name (e.g., Contact, Account).</summary>
	[JsonPropertyName("entity")]
	[Description("Creatio OData entity set name (e.g., Contact, Account, Activity). Call dataforge-find-tables to discover names.")]
	[Required]
	public required string Entity { get; init; }

	/// <summary>Array of row objects (field/value pairs) for the new records.</summary>
	[JsonPropertyName("rows")]
	[Description(
		"Array of row objects to insert; each row is an object of field/value pairs for one new record. " +
		"Pass all rows for the same entity here rather than calling the tool once per row. " +
		ODataCreateTool.RowCountLimitDescription + " " +
		"Use dataforge-get-table-columns to discover field names. " +
		"Set lookup fields via their <Field>Id column with a GUID (e.g. AccountId), not the display name. " +
		"Example: [ { \"Name\": \"Acme\", \"TypeId\": \"8ecab4a1-0ca3-4515-9399-efe0a19390bd\" }, { \"Name\": \"Globex\" } ] " +
		"Exactly one of rows or rows-file is required; supplying both is rejected.")]
	public JsonElement? Rows { get; init; }

	/// <summary>Whether to stop after the first failed row.</summary>
	[JsonPropertyName("stop-on-error")]
	[Description("Stop inserting after the first failed row. Default false: continue and report every row independently. " +
		"When true and a row fails, the rows after it are NOT attempted and do NOT appear in 'results', so 'results' may be shorter than the input 'rows'.")]
	public bool StopOnError { get; init; }

	/// <summary>Optional path to a JSON array of row objects, used instead of <see cref="Rows"/>.</summary>
	[JsonPropertyName("rows-file")]
	[Description("Optional path to a JSON array of field/value objects. Use this instead of rows for large payloads; the file must be readable JSON. " +
		ODataCreateTool.RowCountLimitDescription + " A 10 MB byte bound applies to the file contents.")]
	public string? RowsFile { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Unbound JSON members, rejected before any file access or Creatio request.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
