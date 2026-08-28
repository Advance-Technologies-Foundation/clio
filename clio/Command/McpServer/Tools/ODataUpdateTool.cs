using System;
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
public sealed class ODataUpdateTool(IToolCommandResolver commandResolver, IoFileSystem fileSystem = null) {

	private readonly IoFileSystem _fileSystem = fileSystem ?? new System.IO.Abstractions.FileSystem();

	internal const string ToolName = "odata-update";

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
			ODataWriteResponse invalidTarget = ODataKeyedWrite.ValidateTarget(args.Entity, args.Id, "update");
			if (invalidTarget is not null) {
				return invalidTarget;
			}
			if (args.Data is not null && !string.IsNullOrWhiteSpace(args.RowsFile)) {
				return ODataWriteResponse.Failure("Provide either data or rows-file, not both.");
			}
			if (args.Data is null && string.IsNullOrWhiteSpace(args.RowsFile)) {
				return ODataWriteResponse.Failure("data is required and must be a non-empty object of field/value pairs.");
			}
			// The confirmation gate runs BEFORE the file is touched. It is the first meaningful guard, so an
			// exploratory confirm=false call must answer "here is what would change", not "rows-file was not
			// found" - and an exploratory call should not read and parse a payload it will not send.
			ODataWriteResponse notConfirmed = ODataKeyedWrite.RequireConfirmation(args.Confirm, args.Entity, args.Id, "update", "change");
			JsonElement? fileData = null;
			if (args.Data is null) {
				if (notConfirmed is not null) {
					return notConfirmed;
				}
				if (!ODataFileContract.TryReadJson(_fileSystem, args.RowsFile, "rows-file", out string dataJson, out string fileError)) {
					return ODataWriteResponse.Failure(fileError);
				}
				try {
					// JsonDocument rents from ArrayPool<byte>; Clone() detaches the element, so dispose here.
					using JsonDocument document = JsonDocument.Parse(dataJson);
					fileData = document.RootElement.Clone();
				} catch (JsonException ex) {
					return ODataWriteResponse.Failure($"rows-file must contain valid JSON: {ex.Message}");
				}
			}
			JsonElement? requestedData = args.Data ?? fileData;
			if (requestedData is not { ValueKind: JsonValueKind.Object } data || !data.EnumerateObject().MoveNext()) {
				return ODataWriteResponse.Failure("data is required and must be a non-empty object of field/value pairs.");
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
}
