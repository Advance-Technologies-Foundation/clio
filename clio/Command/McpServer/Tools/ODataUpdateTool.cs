using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for updating a single Creatio record via OData v4 (HTTP PATCH).
/// </summary>
[McpServerToolType]
public sealed class ODataUpdateTool(IToolCommandResolver commandResolver) {

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
		[Description("Parameters: entity, id, data, environment-name (all required).")]
		[Required]
		ODataUpdateArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.Entity)) {
				return ODataWriteResponse.Failure("entity is required.");
			}
			if (!ODataKeyFormatter.IsValidEntityName(args.Entity)) {
				return ODataWriteResponse.Failure("entity must be a valid OData entity set name (letters, digits, underscore).");
			}
			if (string.IsNullOrWhiteSpace(args.Id) || !ODataKeyFormatter.IsGuid(args.Id.Trim())) {
				return ODataWriteResponse.Failure("id is required and must be a record GUID; keyless mass update is not allowed.");
			}
			if (args.Data is not { ValueKind: JsonValueKind.Object } data || !data.EnumerateObject().MoveNext()) {
				return ODataWriteResponse.Failure("data is required and must be a non-empty object of field/value pairs.");
			}
			if (!args.Confirm) {
				return ODataWriteResponse.Failure(
					$"Refusing to update {args.Entity.Trim()}({args.Id.Trim()}) without confirmation. " +
					"This is a destructive operation; re-call odata-update with \"confirm\": true to authorize this change.");
			}

			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);
			IODataPatchClient patchClient = commandResolver.Resolve<IODataPatchClient>(options);

			string url = urlBuilder.Build(ODataKeyFormatter.KeyPath(args.Entity, args.Id));
			patchClient.ExecutePatch(url, data.GetRawText(), 30_000);
			return new ODataWriteResponse(true, null, args.Id.Trim());
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(ex.Message);
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
		"Example: { \"Name\": \"New name\", \"JobTitle\": \"CEO\" }")]
	[Required]
	public JsonElement? Data { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description("Registered clio environment name, e.g. 'dev_5001'.")]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Explicit confirmation gate for this destructive operation.</summary>
	[JsonPropertyName("confirm")]
	[Description("Must be true to authorize this destructive update. When false or omitted, the tool refuses and returns what would change without making any remote call.")]
	public bool Confirm { get; init; }
}
