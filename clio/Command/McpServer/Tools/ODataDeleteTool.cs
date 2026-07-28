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
		"This is a destructive operation: it requires confirm=true to proceed. " +
		"Use odata-read to find the record by its fields and obtain its Id. " +
		"Call get-tool-contract for odata-delete to see usage examples and discovery workflow hints.")]
	public ODataWriteResponse Delete(
		[Description("Parameters: entity, id, environment-name (all required).")]
		[Required]
		ODataDeleteArgs args) {
		try {
			ODataWriteResponse invalidTarget = ODataKeyedWrite.ValidateTarget(args.Entity, args.Id, "delete");
			if (invalidTarget is not null) {
				return invalidTarget;
			}
			ODataWriteResponse notConfirmed = ODataKeyedWrite.RequireConfirmation(args.Confirm, args.Entity, args.Id, "delete", "deletion");
			if (notConfirmed is not null) {
				return notConfirmed;
			}

			(IApplicationClient client, string url) = ODataKeyedWrite.ResolveTarget(commandResolver, args.EnvironmentName, args.Entity, args.Id);
			client.ExecuteDeleteRequest(url, string.Empty, 30_000);
			return new ODataWriteResponse(true, null, args.Id.Trim());
		} catch (Exception ex) {
			return ODataWriteResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}
}

/// <summary>Arguments for <see cref="ODataDeleteTool"/>.</summary>
public sealed record ODataDeleteArgs {
	/// <summary>Creatio OData entity set name (e.g., Contact, Account).</summary>
	[JsonPropertyName("entity")]
	[Description("Creatio OData entity set name (e.g., Contact, Account, Activity). Call dataforge-find-tables to discover names.")]
	[Required]
	public required string Entity { get; init; }

	/// <summary>GUID of the record to delete.</summary>
	[JsonPropertyName("id")]
	[Description("GUID of the record to delete. Required — a keyless mass delete is rejected.")]
	[Required]
	public required string Id { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Explicit confirmation gate for this destructive operation.</summary>
	[JsonPropertyName("confirm")]
	[Description("Must be true to authorize this destructive delete. When false or omitted, the tool refuses and returns what would be deleted without making any remote call.")]
	public bool Confirm { get; init; }
}
