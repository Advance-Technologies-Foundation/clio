using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for creating a Creatio record via OData v4 (HTTP POST).
/// </summary>
[McpServerToolType]
public sealed class ODataCreateTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "odata-create";

	/// <summary>Creates a Creatio record using OData v4.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description(
		"Create a Creatio record via OData v4 (POST). " +
		"Provide the entity set name and a data object of field/value pairs. " +
		"Returns the created record including its generated Id. " +
		"Call get-tool-contract for odata-create to see usage examples and discovery workflow hints.")]
	public ODataWriteResponse Create(
		[Description("Parameters: entity, data, environment-name (all required).")]
		[Required]
		ODataCreateArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.Entity)) {
				return ODataWriteResponse.Failure("entity is required.");
			}
			if (!ODataKeyFormatter.IsValidEntityName(args.Entity)) {
				return ODataWriteResponse.Failure("entity must be a valid OData entity set name (letters, digits, underscore).");
			}
			if (args.Data is not { ValueKind: JsonValueKind.Object } data || !data.EnumerateObject().MoveNext()) {
				return ODataWriteResponse.Failure("data is required and must be a non-empty object of field/value pairs.");
			}

			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			string url = urlBuilder.Build(ODataKeyFormatter.CollectionPath(args.Entity));
			string responseJson = client.ExecutePostRequest(url, data.GetRawText(), 30_000);
			return ODataResponseParser.ParseODataCreated(responseJson);
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(ex.Message);
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

	/// <summary>Field/value pairs for the new record.</summary>
	[JsonPropertyName("data")]
	[Description(
		"Object of field/value pairs for the new record. " +
		"Use dataforge-get-table-columns to discover field names. " +
		"Set lookup fields via their <Field>Id column with a GUID (e.g. AccountId), not the display name. " +
		"Example: { \"Name\": \"Acme\", \"TypeId\": \"8ecab4a1-0ca3-4515-9399-efe0a19390bd\" }")]
	[Required]
	public JsonElement? Data { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description("Registered clio environment name, e.g. 'dev_5001'.")]
	[Required]
	public required string EnvironmentName { get; init; }
}
