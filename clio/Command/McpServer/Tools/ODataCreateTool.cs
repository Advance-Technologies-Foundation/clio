using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for creating one or more Creatio records via OData v4 (HTTP POST) in a single call.
/// </summary>
[McpServerToolType]
public sealed class ODataCreateTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "odata-create";

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
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Create one or more Creatio records via OData v4 (POST) in a single call. " +
		"Provide the entity set name and a 'rows' array of field/value objects; pass all rows for the same " +
		"entity in one call rather than one call per row. Each row is inserted sequentially and reported " +
		"independently — a failed row does not abort the rest unless 'stop-on-error' is set. " +
		"Returns a created/failed summary and a per-row result array with each created record's Id. " +
		"A date-time value without a UTC designator or offset (e.g. '2024-01-01T04:00:00') fails its row before any " +
		"request - send '...Z' or '...+02:00' instead. " +
		"CRITICAL for failed rows — read 'record-created' before reacting: true inserted, false definitely not " +
		"inserted (rejected locally, safe to fix and re-send), null UNKNOWN. Null means Creatio failed the call " +
		"but may already have written the record, which happens when a post-insert entity event handler throws; " +
		"re-sending such a row DUPLICATES it. On null, read the entity back and re-send only if absent — the " +
		"row's 'retry-guidance' says so too, and the batch's 'unverified' count is how many rows are in that " +
		"state. " +
		"Call get-tool-contract for odata-create to see usage examples and discovery workflow hints.")]
	public ODataCreateBatchResponse Create(
		[Description("Parameters: entity, rows, environment-name (all required); stop-on-error (optional).")]
		[Required]
		ODataCreateArgs args) {
		if (string.IsNullOrWhiteSpace(args.Entity)) {
			return ODataCreateBatchResponse.RequestError("entity is required.");
		}
		if (!ODataKeyFormatter.IsValidEntityName(args.Entity)) {
			return ODataCreateBatchResponse.RequestError(
				"entity must be a valid OData entity set name (letters, digits, underscore).");
		}
		if (args.Rows is not { ValueKind: JsonValueKind.Array } rows || rows.GetArrayLength() == 0) {
			return ODataCreateBatchResponse.RequestError(
				"rows is required and must be a non-empty array of field/value objects.");
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
		// The metadata read is at most ONE per batch, and only when some row actually carries a
		// date-time-shaped literal: the service-root CSDL is a multi-megabyte document, and a batch of
		// plain rows would otherwise pay that download for a guard that cannot fire. It only ever types
		// the value guard - odata-create does not validate field NAMES - so an unresolved metadata
		// endpoint must never fail the insert; the guard then falls back to the literal's shape alone.
		IReadOnlyDictionary<string, string> propertyTypes =
			rows.EnumerateArray().Any(ODataDateTimeGuard.HasZoneLessCandidate)
				? ODataFieldValidation.TryGetPropertyTypes(client, urlBuilder, args.Entity.Trim())
				: null;
		List<ODataRowResult> results = [];
		int index = 0;
		foreach (JsonElement row in rows.EnumerateArray()) {
			ODataRowResult result = CreateRow(client, url, row, index, propertyTypes);
			results.Add(result);
			if (!result.Success && args.StopOnError) {
				break;
			}
			index++;
		}
		return ODataCreateBatchResponse.From(results);
	}

	private static ODataRowResult CreateRow(IApplicationClient client, string url, JsonElement row, int index,
		IReadOnlyDictionary<string, string> propertyTypes) {
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
			string zoneLessDateTime = ODataDateTimeGuard.FindZoneLessDateTime(row, propertyTypes);
			if (zoneLessDateTime is not null) {
				return new ODataRowResult {
					Index = index,
					Success = false,
					// Rejected locally before any POST, so not-inserted is KNOWN for this row.
					RecordCreated = false,
					Error = zoneLessDateTime
				};
			}
			string responseJson = client.ExecutePostRequest(url, row.GetRawText(), 30_000);
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
			if (CreatioResponseError.TryDetect(root, CreatioResponseContext.ODataPayload, out string serverError)) {
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
					Error = $"OData create did not return a record Id. Response: {CreatioResponseError.Truncate(SensitiveErrorTextRedactor.Redact(json))}"
				};
			}
			return new ODataRowResult { Index = index, Success = true, RecordCreated = true, Id = id };
		} catch (JsonException) {
			// A non-JSON body never comes from Creatio's OData pipeline by itself - even a server
			// error is one of the JSON shapes CreatioResponseError.TryDetect recognizes. This shape means
			// the request did not reach Creatio intact (a proxy/IIS/routing error, or a session
			// redirect), so the row's side effect is UNKNOWN, not a confirmed create - never report
			// record-created here.
			return new ODataRowResult {
				Index = index,
				Success = false,
				RecordCreated = null,
				RetryGuidance = UnknownSideEffectGuidance,
				Error = SensitiveErrorTextRedactor.Redact(CreatioResponseError.DescribeNonJsonResponse(json))
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
		"Use dataforge-get-table-columns to discover field names. " +
		"Set lookup fields via their <Field>Id column with a GUID (e.g. AccountId), not the display name. " +
		"Date-time values MUST carry a UTC designator or offset ('2024-01-01T04:00:00Z' or '2024-01-01T04:00:00+02:00'); " +
		"a zone-less literal fails that row before any request, because the platform may silently store 0001-01-01. " +
		"Example: [ { \"Name\": \"Acme\", \"TypeId\": \"8ecab4a1-0ca3-4515-9399-efe0a19390bd\" }, { \"Name\": \"Globex\" } ]")]
	[Required]
	public JsonElement? Rows { get; init; }

	/// <summary>Whether to stop after the first failed row.</summary>
	[JsonPropertyName("stop-on-error")]
	[Description("Stop inserting after the first failed row. Default false: continue and report every row independently. " +
		"When true and a row fails, the rows after it are NOT attempted and do NOT appear in 'results', so 'results' may be shorter than the input 'rows'.")]
	public bool StopOnError { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }
}
