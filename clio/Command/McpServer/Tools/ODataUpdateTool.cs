using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for updating a single Creatio record via OData v4 (HTTP PATCH).
/// </summary>
[McpServerToolType]
public sealed class ODataUpdateTool(IToolCommandResolver commandResolver, IoFileSystem fileSystem) {

	//File access and confinement are core behaviour here, and IFileSystem is registered in DI, so a
	//`new FileSystem()` fallback would mask missing wiring and let a unit test touch the real host.
	private readonly IoFileSystem _fileSystem =
		fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

	internal const string ToolName = "odata-update";

	private const string DataRequiredMessage =
		"data is required and must be a non-empty object of field/value pairs.";

	private const string ValidArgumentsHint =
		"Valid: entity, id, environment-name, data, rows-file, confirm.";

	/// <summary>
	/// The camelCase / snake_case spellings an LLM emits for this tool's kebab-case fields. Without this map
	/// (and the overflow bag it reads) a request carrying inline <c>data</c> plus <c>rows_file</c> bound only
	/// the inline object, slipped past the mutual-exclusion check, and PATCHed an ambiguous request.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> ArgumentAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["environmentName"] = "environment-name",
			["environment_name"] = "environment-name",
			["rowsFile"] = "rows-file",
			["rows_file"] = "rows-file"
		};

	/// <summary>Updates a single Creatio record using OData v4.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description(
		"Update a single Creatio record via OData v4 (PATCH). " +
		"Requires the record's GUID id; only the supplied fields are changed. " +
		"This tool never performs a keyless mass update. " +
		"This is a destructive operation: it requires confirm=true to proceed. " +
		"Use odata-read to find the record by its fields and obtain its Id. " +
		"Call get-tool-contract for odata-update to see usage examples and discovery workflow hints.")]
	public ODataWriteResponse Update(
		[Description("Parameters: entity, id, data or rows-file, environment-name (required).")]
		[Required]
		ODataUpdateArgs args) {
		try {
			//Runs before the confirmation gate, before the file is touched and before any PATCH: an unbound
			//file-source key such as rows_file would otherwise be dropped silently and the inline data sent
			//instead, which is the ambiguous request this rejects.
			string? argumentError = McpToolArgumentSupport.BuildLegacyAliasError(
				args.ExtensionData,
				ArgumentAliases,
				".",
				ValidArgumentsHint);
			if (argumentError is not null) {
				return ODataWriteResponse.Failure(argumentError);
			}
			ODataWriteResponse invalidTarget = ODataKeyedWrite.ValidateTarget(args.Entity, args.Id, "update");
			if (invalidTarget is not null) {
				return invalidTarget;
			}
			// The confirmation gate runs BEFORE the file is touched. It is the first meaningful guard, so an
			// exploratory confirm=false call must answer "here is what would change", not "rows-file was not
			// found" - and an exploratory call should not read and parse a payload it will not send.
			ODataWriteResponse notConfirmed = ODataKeyedWrite.RequireConfirmation(args.Confirm, args.Entity, args.Id, "update", "change");
			ODataWriteResponse payloadFailure = ResolveData(args, notConfirmed, out JsonElement data);
			if (payloadFailure is not null) {
				return payloadFailure;
			}
			if (notConfirmed is not null) {
				return notConfirmed;
			}

			(IApplicationClient client, string url) = ODataKeyedWrite.ResolveTarget(commandResolver, args.EnvironmentName, args.Entity, args.Id);
			string response = client.ExecutePatchRequest(url, data.GetRawText(), 30_000);
			string validationError = ODataKeyedWrite.ValidateWriteResponse(response);
			if (validationError is not null) {
				return ODataWriteResponse.Failure(validationError);
			}
			return new ODataWriteResponse(true, null, args.Id.Trim());
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}

	/// <summary>
	/// Resolves the field/value payload from either the inline <c>data</c> argument or <c>rows-file</c>,
	/// and returns the failure response to send back when the payload is unusable.
	/// </summary>
	/// <param name="args">Tool arguments.</param>
	/// <param name="notConfirmed">Confirmation refusal to return instead of reading the file, or <c>null</c>.</param>
	/// <param name="data">Resolved non-empty JSON object payload when the method returns <c>null</c>.</param>
	/// <returns><c>null</c> when the payload is valid; otherwise the response to return to the caller.</returns>
	private ODataWriteResponse ResolveData(ODataUpdateArgs args, ODataWriteResponse notConfirmed, out JsonElement data) {
		data = default;
		bool hasRowsFile = !string.IsNullOrWhiteSpace(args.RowsFile);
		if (args.Data is not null && hasRowsFile) {
			return ODataWriteResponse.Failure("Provide either data or rows-file, not both.");
		}
		if (args.Data is null && !hasRowsFile) {
			return ODataWriteResponse.Failure(DataRequiredMessage);
		}
		JsonElement? requestedData = args.Data;
		if (requestedData is null) {
			if (notConfirmed is not null) {
				return notConfirmed;
			}
			ODataWriteResponse fileFailure = ReadFileData(args.RowsFile, out JsonElement fileData);
			if (fileFailure is not null) {
				return fileFailure;
			}
			requestedData = fileData;
		}
		if (requestedData is not { ValueKind: JsonValueKind.Object } payload || !payload.EnumerateObject().MoveNext()) {
			return ODataWriteResponse.Failure(DataRequiredMessage);
		}
		data = payload;
		return null;
	}

	/// <summary>Reads and parses the JSON payload held in <paramref name="rowsFile"/>.</summary>
	/// <param name="rowsFile">Path supplied through the <c>rows-file</c> argument.</param>
	/// <param name="fileData">Parsed payload when the method returns <c>null</c>.</param>
	/// <returns><c>null</c> on success; otherwise the failure response to return to the caller.</returns>
	private ODataWriteResponse ReadFileData(string rowsFile, out JsonElement fileData) {
		fileData = default;
		if (!ODataFileContract.TryReadJson(_fileSystem, rowsFile, "rows-file", out string dataJson, out string fileError)) {
			return ODataWriteResponse.Failure(fileError);
		}
		try {
			// JsonDocument rents from ArrayPool<byte>; Clone() detaches the element, so dispose here.
			using JsonDocument document = JsonDocument.Parse(dataJson);
			fileData = document.RootElement.Clone();
			return null;
		} catch (JsonException ex) {
			return ODataWriteResponse.Failure($"rows-file must contain valid JSON: {ex.Message}");
		}
	}
}

/// <summary>Arguments for <see cref="ODataUpdateTool"/>.</summary>
public sealed record ODataUpdateArgs {
	/// <summary>Creatio OData entity set name (e.g., Contact, Account).</summary>
	[JsonPropertyName("entity")]
	[Description("Creatio OData entity set name (e.g., Contact, Account, Activity). Call dataforge-find-tables to discover names.")]
	[Required]
	public required string Entity { get; init; }

	/// <summary>GUID of the record to update.</summary>
	[JsonPropertyName("id")]
	[Description("GUID of the record to update. Required — a keyless mass update is rejected.")]
	[Required]
	public required string Id { get; init; }

	/// <summary>Field/value pairs to change.</summary>
	[JsonPropertyName("data")]
	[Description(
		"Object of field/value pairs to change. Only supplied fields are updated. " +
		"Set lookup fields via their <Field>Id column with a GUID (e.g. AccountId), not the display name. " +
		"Example: { \"Name\": \"New name\", \"JobTitle\": \"CEO\" } " +
		"Exactly one of data or rows-file is required; supplying both is rejected.")]
	public JsonElement? Data { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Explicit confirmation gate for this destructive operation.</summary>
	[JsonPropertyName("confirm")]
	[Description("Must be true to authorize this destructive update. When false or omitted, the tool refuses and returns what would change without making any remote call.")]
	public bool Confirm { get; init; }

	/// <summary>Optional path to a JSON object of fields to change, used instead of <see cref="Data"/>.</summary>
	[JsonPropertyName("rows-file")]
	[Description("Optional path to a JSON object of field/value pairs. Use this instead of data for large payloads; confirm=true is still required.")]
	public string? RowsFile { get; init; }

	/// <summary>Unbound JSON members, rejected before any file access or Creatio request.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
