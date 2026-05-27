using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for deleting a single Creatio record via OData v4 (HTTP DELETE).
/// </summary>
[McpServerToolType]
public sealed class ODataDeleteTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "odata-delete";

	/// <summary>Deletes a single Creatio record using OData v4.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description(
		"Delete a single Creatio record via OData v4 (DELETE). " +
		"Requires the record's GUID id; this tool never performs a keyless mass delete. " +
		"Call get-tool-contract for odata-delete to see usage examples and discovery workflow hints.")]
	public ODataWriteResponse Delete(
		[Description("Parameters: entity, id, environment-name (all required).")]
		[Required]
		ODataDeleteArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.Entity)) {
				return ODataWriteResponse.Failure("entity is required.");
			}
			if (string.IsNullOrWhiteSpace(args.Id) || !ODataKeyFormatter.IsGuid(args.Id.Trim())) {
				return ODataWriteResponse.Failure("id is required and must be a record GUID; keyless mass delete is not allowed.");
			}

			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			string key = ODataKeyFormatter.FormatEntityKey(args.Id.Trim());
			string url = urlBuilder.Build($"odata/{args.Entity.Trim()}({key})");
			client.ExecuteDeleteRequest(url, string.Empty, 30_000);
			return new ODataWriteResponse(true, null, args.Id.Trim());
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(ex.Message);
		}
	}
}

/// <summary>Arguments for <see cref="ODataDeleteTool"/>.</summary>
public sealed record ODataDeleteArgs {
	/// <summary>Creatio OData entity set name (e.g., Contact, Account).</summary>
	[JsonPropertyName("entity")]
	[Description("Creatio OData entity set name (e.g., Contact, Account, Activity).")]
	[Required]
	public required string Entity { get; init; }

	/// <summary>GUID of the record to delete.</summary>
	[JsonPropertyName("id")]
	[Description("GUID of the record to delete. Required — a keyless mass delete is rejected.")]
	[Required]
	public required string Id { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description("Registered clio environment name, e.g. 'dev_5001'.")]
	[Required]
	public required string EnvironmentName { get; init; }
}
