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

/// <summary>
/// MCP surface of <c>set-object-rights</c>: grants or revokes one role's operation permissions on ONE object.
/// Destructive: the call applies the change, and the host's approval of the call is the confirmation — the arguments
/// name every access-changing transition, so the approval shows everything the call can do. <c>preview</c> is a dry
/// run that writes nothing.
/// </summary>
[McpServerToolType]
public sealed class SetObjectRightsTool(
	SetObjectRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetObjectRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-object-rights";

	internal const string ValidArguments =
		"Valid: environment-name, entity-schema-name, grantee, operations, revoke, enable-operation-permissions, "
		+ "disable-operation-permissions, preview.";

	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Grant or revoke OBJECT operation permissions (read/create/edit/delete) for one role on ONE object — the SysEntitySchemaOperationRight / \"Object permissions\" layer (DESTRUCTIVE — changes access rights). " +
		"Works like the Object permissions designer, one object per call, for ANY role. To cover an object's lookups, read them with get-object-rights include-connected, decide per object, and make one call per object. " +
		"grantee is a SysAdminUnit id (roles/users; names are not unique); it must exist. " +
		"operations is required (read,create,edit,delete): nothing is granted by default. revoke=true clears the named operations on the role's row while KEEPING the row (rows are never removed or moved). " +
		"The rows are a priority list: a user in several roles gets the highest matching row, and a row with an operation cleared denies it to users for whom it is that row. A new row goes at the lowest priority; the result names the rows above the grantee's row. " +
		"Every access-changing transition must be named, or the call is refused: enable-operation-permissions to let a grant turn the object's operation permissions ON (from then on its rows decide who can reach it; when the object has rows but none for All employees, an All employees row with every operation is added below them); disable-operation-permissions with revoke to turn them OFF (the object becomes available to ALL internal users; accepted only when the revoke would leave no granting row, otherwise refused). " +
		"preview=true is a dry run: it writes nothing and shows what the call would change. Read the result back with get-object-rights. Does NOT change column or record permissions. " +
		"Unknown or misspelled argument names are REFUSED before any read or write.")]
	public ObjectRightsToolResponse SetObjectRights(
		[Description("Parameters: environment-name, entity-schema-name, grantee, operations (required); revoke, enable-operation-permissions, disable-operation-permissions, preview (optional).")]
		[Required]
		SetObjectRightsArgs args) {
		// A long-tail tool reached through clio-run: the flat-argument classifier never sees this wrapped payload,
		// and the serializer silently DROPS unknown keys. On a destructive tool that turns a typo into the opposite
		// change ({"revok":true} binds Revoke=false and GRANTS), so refuse before any read or write.
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ObjectRightsToolResponse.FromValidationError(aliasError);
		}
		try {
			bool preview = args.Preview ?? false;
			SetObjectRightsOptions options = new() {
				Environment = args.EnvironmentName,
				EntitySchemaName = args.EntitySchemaName,
				Grantee = args.Grantee,
				Operations = args.Operations,
				Revoke = args.Revoke ?? false,
				EnableOperationPermissions = args.EnableOperationPermissions ?? false,
				DisableOperationPermissions = args.DisableOperationPermissions ?? false,
				Preview = preview,
				// MCP cannot prompt anyone: the host's approval of this call is the confirmation (as for
				// set-record-rights and manage-access). A preview writes nothing, so it is never confirmed.
				Confirm = !preview
			};
			return ObjectRightsToolResponse.From(InternalExecute<SetObjectRightsCommand>(options));
		} catch (Exception ex) {
			return ObjectRightsToolResponse.FromError(ex);
		}
	}
}

/// <summary>Arguments of the <c>set-object-rights</c> MCP tool.</summary>
/// <param name="EnvironmentName">The registered environment.</param>
/// <param name="EntitySchemaName">The one object whose operation permissions change.</param>
/// <param name="Grantee">The SysAdminUnit id of the role or user.</param>
/// <param name="Operations">Comma-separated operations to grant or revoke; required.</param>
/// <param name="Revoke">Revoke instead of grant.</param>
/// <param name="EnableOperationPermissions">Allow a grant to turn operation permissions on.</param>
/// <param name="DisableOperationPermissions">With revoke: turn operation permissions off.</param>
/// <param name="Preview">Write nothing; show what the call would change.</param>
public sealed record SetObjectRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("The one object (entity schema) whose operation permissions are changed.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("grantee")]
	[property: Description("SysAdminUnit id (role or user) to grant/revoke. Names are not unique — pass the id.")]
	[property: Required]
	string Grantee,

	[property: JsonPropertyName("operations")]
	[property: Description("Comma-separated operations to grant or revoke: read,create,edit,delete. Required: nothing is granted by default. An empty value is refused.")]
	[property: Required]
	string Operations,

	[property: JsonPropertyName("revoke")]
	[property: Description("Revoke the operations named in operations instead of granting them (default false). The role's row is kept, with those operations cleared.")]
	bool? Revoke = null,

	[property: JsonPropertyName("enable-operation-permissions")]
	[property: Description("Allow a grant to turn the object's operation permissions ON (default false, which refuses a grant on an object that does not use them yet). From then on the object's rows decide who can reach it.")]
	bool? EnableOperationPermissions = null,

	[property: JsonPropertyName("disable-operation-permissions")]
	[property: Description("With revoke: turn the object's operation permissions OFF, making it available to ALL internal users (default false). Needed when the revoke would leave no row that grants any operation.")]
	bool? DisableOperationPermissions = null,

	[property: JsonPropertyName("preview")]
	[property: Description("Dry run (default false): write nothing and show what the call would change, the rows it affects, and whether it would be refused.")]
	bool? Preview = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call before any read or write.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
