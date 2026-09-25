using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.ObjectRights;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GetObjectRightsTool(
	GetObjectRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetObjectRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-object-rights";

	internal const string ValidArguments = "Valid: environment-name, entity-schema-name, grantee, include-connected.";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Read OBJECT operation permissions — who may read/create/edit/delete a whole entity (the SysSchemaOperationRight / \"Object permissions\" layer). " +
		"Read-only companion of set-object-rights. Reports, per object, the operations every role holds; pass grantee to filter to one role. " +
		"include-connected also reports the root object's own lookup objects (security/system objects are skipped). The output is facts only, with no coverage verdict. Fails (success=false) when the root object cannot be read; a connected object that cannot be read is reported with a warning. " +
		"An object not administered by operation permissions is available to all INTERNAL users; external users reach it only through an explicit grant. " +
		"Unknown or misspelled argument names are refused.")]
	public ObjectRightsToolResponse GetObjectRights(
		[Description("Parameters: environment-name, entity-schema-name (required); grantee, include-connected (optional).")]
		[Required]
		GetObjectRightsArgs args) {
		// Long-tail (clio-run): the serializer would silently drop a misspelled key — e.g. "grantee-id" — and the
		// read would then answer for EVERY role instead of the one asked about. Refuse it instead.
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ObjectRightsToolResponse.FromValidationError(aliasError);
		}
		try {
			GetObjectRightsOptions options = new() {
				Environment = args.EnvironmentName,
				EntitySchemaName = args.EntitySchemaName,
				Grantee = args.Grantee,
				IncludeConnected = args.IncludeConnected ?? false
			};
			return ObjectRightsToolResponse.From(InternalExecute<GetObjectRightsCommand>(options));
		} catch (Exception ex) {
			return ObjectRightsToolResponse.FromError(ex);
		}
	}
}

public sealed record GetObjectRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Object (entity schema) name to read.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("grantee")]
	[property: Description("Optional SysAdminUnit id (role or user) to filter to one role. Omit to report every role.")]
	string Grantee = null,

	[property: JsonPropertyName("include-connected")]
	[property: Description("Also read the root object's own lookup objects, skipping security/system objects (default false).")]
	bool? IncludeConnected = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
