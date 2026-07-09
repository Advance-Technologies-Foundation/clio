using System;
using System.Collections.Generic;
using System.ComponentModel;
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

	/// <summary>Creates one or more Creatio records using OData v4.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description(
		"Create one or more Creatio records via OData v4 (POST) in a single call. " +
		"Provide the entity set name and a 'rows' array of field/value objects; pass all rows for the same " +
		"entity in one call rather than one call per row. Each row is inserted sequentially and reported " +
		"independently — a failed row does not abort the rest unless 'stop-on-error' is set. " +
		"Returns a created/failed summary and a per-row result array with each created record's Id. " +
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
		List<ODataRowResult> results = [];
		int index = 0;
		foreach (JsonElement row in rows.EnumerateArray()) {
			ODataRowResult result = CreateRow(client, url, row, index);
			results.Add(result);
			if (!result.Success && args.StopOnError) {
				break;
			}
			index++;
		}
		return ODataCreateBatchResponse.From(results);
	}

	private static ODataRowResult CreateRow(IApplicationClient client, string url, JsonElement row, int index) {
		try {
			if (row.ValueKind != JsonValueKind.Object || !row.EnumerateObject().MoveNext()) {
				return new ODataRowResult {
					Index = index,
					Success = false,
					Error = "row must be a non-empty object of field/value pairs."
				};
			}
			string responseJson = client.ExecutePostRequest(url, row.GetRawText(), 30_000);
			return ParseCreated(responseJson, index);
		} catch (Exception ex) {
			return new ODataRowResult { Index = index, Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
		}
	}

	private static ODataRowResult ParseCreated(string json, int index) {
		if (string.IsNullOrWhiteSpace(json)) {
			return new ODataRowResult { Index = index, Success = true };
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;
			if (ODataResponseError.TryDetect(root, out string serverError)) {
				return new ODataRowResult { Index = index, Success = false, Error = serverError };
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
				return new ODataRowResult {
					Index = index,
					Success = false,
					Error = $"OData create did not return a record Id. Response: {Truncate(json)}"
				};
			}
			return new ODataRowResult { Index = index, Success = true, Id = id };
		} catch (JsonException) {
			// A non-JSON body on a successful POST still means the record was created.
			return new ODataRowResult { Index = index, Success = true };
		}
	}

	private static string Truncate(string value) {
		if (string.IsNullOrEmpty(value)) {
			return "<empty>";
		}
		return value.Length > 500 ? value[..500] + "..." : value;
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
