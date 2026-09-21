using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.ObjectRights;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SetObjectRightsTool(
	SetObjectRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetObjectRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-object-rights";

	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Grant or revoke OBJECT operation permissions (read/create/edit/delete) for one role on an object — the SysSchemaOperationRight / \"Object permissions\" layer (DESTRUCTIVE — changes access rights). " +
		"Object-level analog of set-record-rights, and works for ANY role. Grants turn on the object's operation permissions when needed. " +
		"grantee is a SysAdminUnit id (roles/users; names are not unique). Portal audience: All external users = 720b771c-e7a7-4f31-9cfb-52cd21c3739f. " +
		"operations defaults to all four; revoke=true removes them (a role left with none is removed). " +
		"include-connected also applies to the root object's own lookup objects (the portal-section convenience). Does NOT change column permissions. Read it back with get-object-rights.")]
	public ObjectRightsToolResponse SetObjectRights(
		[Description("Parameters: environment-name, entity-schema-name, grantee (required); operations, revoke, include-connected (optional).")]
		[Required]
		SetObjectRightsArgs args) {
		try {
			SetObjectRightsOptions options = new() {
				Environment = args.EnvironmentName,
				EntitySchemaName = args.EntitySchemaName,
				Grantee = args.Grantee,
				Operations = args.Operations,
				Revoke = args.Revoke ?? false,
				IncludeConnected = args.IncludeConnected ?? false,
				// --confirm is a CLI-only interactive gate; on MCP the Destructive flag is the safety mechanism,
				// so confirm the apply here (the command otherwise refuses in a non-interactive run).
				Confirm = true
			};
			return ObjectRightsToolResponse.From(InternalExecute<SetObjectRightsCommand>(options));
		} catch (Exception ex) {
			return ObjectRightsToolResponse.FromError(ex);
		}
	}
}

public sealed record SetObjectRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Object (entity schema) name whose operation permissions are changed.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("grantee")]
	[property: Description("SysAdminUnit id (role or user) to grant/revoke. Names are not unique — pass the id. All external users = 720b771c-e7a7-4f31-9cfb-52cd21c3739f.")]
	[property: Required]
	string Grantee,

	[property: JsonPropertyName("operations")]
	[property: Description("Comma-separated operations: read,create,edit,delete. Default: all four.")]
	string Operations = null,

	[property: JsonPropertyName("revoke")]
	[property: Description("Revoke the operations instead of granting (default false). A role left with no operations is removed.")]
	bool? Revoke = null,

	[property: JsonPropertyName("include-connected")]
	[property: Description("Also apply to the root object's own lookup objects (portal-section convenience; default false).")]
	bool? IncludeConnected = null
);
