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
/// MCP surface of <c>set-object-rights</c>: grants or revokes a role's object operation permissions. Destructive; on
/// MCP the Destructive flag is the only gate, so the call is applied without a prompt.
/// </summary>
[McpServerToolType]
public sealed class SetObjectRightsTool(
	SetObjectRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetObjectRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-object-rights";

	internal const string ValidArguments =
		"Valid: environment-name, entity-schema-name, grantee, operations, revoke, include-connected, "
		+ "connected-operations, disable-operation-permissions, allow-security-object, confirm, confirmation-code.";

	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		// A fan-out makes several sequential round-trips per object; the default window can kill the worker
		// part-way through a destructive change.
		BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Grant or revoke OBJECT operation permissions (read/create/edit/delete) for one role on an object — the SysSchemaOperationRight / \"Object permissions\" layer (DESTRUCTIVE — changes access rights). " +
		"Object-level analog of set-record-rights, and works for ANY role. Grants turn on the object's operation permissions when needed. " +
		"grantee is a SysAdminUnit id (roles/users; names are not unique). " +
		"operations defaults to read/create/edit on the root object (delete not granted by default); revoke=true removes them (a role left with none is removed). " +
		"include-connected also applies to the root object's own lookup objects (security/system objects such as SysAdminUnit are skipped), which get connected-operations (default read only); on revoke the lookups are touched only when connected-operations is given. Fails without writing if the lookups cannot be enumerated. Does NOT change column permissions. Read it back with get-object-rights. " +
		"A revoke that would remove the root's LAST rights row is REFUSED unless disable-operation-permissions is set (it makes the object available to ALL internal users; never applied to lookups). " +
		"The grantee must exist in SysAdminUnit. A security/system ROOT object may only be granted read unless allow-security-object is set. " +
		"With include-connected the call can take minutes. " +
		"TWO-STEP: a call WITHOUT confirm writes nothing and returns a PREVIEW (every target object, its current state, and whether operation permissions will be turned ON) plus a confirmation-code. " +
		"Show the preview to the user; only after they approve, call again with confirm=true and that confirmation-code. The write is refused when the code no longer matches the targets (they changed since the preview). " +
		"Unknown or misspelled argument names are REFUSED before any write.")]
	public ObjectRightsToolResponse SetObjectRights(
		[Description("Parameters: environment-name, entity-schema-name, grantee (required); operations, revoke, include-connected, connected-operations, disable-operation-permissions, allow-security-object, confirm, confirmation-code (optional).")]
		[Required]
		SetObjectRightsArgs args) {
		// A long-tail tool reached through clio-run: the flat-argument classifier never sees this wrapped payload,
		// and the serializer silently DROPS unknown keys. On an auto-confirmed destructive tool that turns a typo
		// into the opposite change ({"revok":true} binds Revoke=false and GRANTS), so refuse before any write.
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ObjectRightsToolResponse.FromValidationError(aliasError);
		}
		bool confirmed = args.Confirm ?? false;
		if (confirmed && string.IsNullOrWhiteSpace(args.ConfirmationCode)) {
			return ObjectRightsToolResponse.FromValidationError(
				"confirm=true requires the confirmation-code returned by a preview call: call set-object-rights without "
				+ "confirm first, show the preview to the user, then repeat the call with confirm=true and that code.");
		}
		try {
			SetObjectRightsOptions options = new() {
				Environment = args.EnvironmentName,
				EntitySchemaName = args.EntitySchemaName,
				Grantee = args.Grantee,
				Operations = args.Operations,
				Revoke = args.Revoke ?? false,
				IncludeConnected = args.IncludeConnected ?? false,
				ConnectedOperations = args.ConnectedOperations,
				DisableOperationPermissions = args.DisableOperationPermissions ?? false,
				AllowSecurityObject = args.AllowSecurityObject ?? false,
				// MCP cannot prompt anyone, so the command's own confirmation is replaced by a two-step protocol: a
				// call without confirm is a PREVIEW that writes nothing and returns a code; the confirmed call must
				// carry that code, and the command refuses it when the targets changed since the preview.
				Preview = !confirmed,
				ConfirmationCode = confirmed ? args.ConfirmationCode : null
			};
			return ObjectRightsToolResponse.From(InternalExecute<SetObjectRightsCommand>(options));
		} catch (Exception ex) {
			return ObjectRightsToolResponse.FromError(ex);
		}
	}
}

/// <summary>Arguments of the <c>set-object-rights</c> MCP tool.</summary>
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
	[property: Description("SysAdminUnit id (role or user) to grant/revoke. Names are not unique — pass the id.")]
	[property: Required]
	string Grantee,

	[property: JsonPropertyName("operations")]
	[property: Description("Comma-separated operations: read,create,edit,delete. Default: read,create,edit (delete not granted by default).")]
	string Operations = null,

	[property: JsonPropertyName("revoke")]
	[property: Description("Revoke the operations instead of granting (default false). A role left with no operations is removed.")]
	bool? Revoke = null,

	[property: JsonPropertyName("include-connected")]
	[property: Description("Also apply to the root object's own lookup objects, skipping security/system objects (default false).")]
	bool? IncludeConnected = null,

	[property: JsonPropertyName("connected-operations")]
	[property: Description("Operations applied to the connected lookup objects when include-connected is set. Default on grant: read (picking a lookup value only needs read). On revoke the lookups are left untouched unless this is given. Widen explicitly only when the grantee must author lookup records.")]
	string ConnectedOperations = null,

	[property: JsonPropertyName("disable-operation-permissions")]
	[property: Description("Allow a revoke to remove the object's LAST rights row, turning the object's operation permissions OFF and making it available to ALL internal users (default false, which refuses such a revoke). Only set this when widening access to every internal user is the intent.")]
	bool? DisableOperationPermissions = null,

	[property: JsonPropertyName("confirm")]
	[property: Description("Apply the change. Default false: the call only returns a PREVIEW and a confirmation-code. Pass true only after the user approved that preview, together with confirmation-code.")]
	bool? Confirm = null,

	[property: JsonPropertyName("confirmation-code")]
	[property: Description("The confirmation-code from the preview the user approved. Required with confirm=true; the write is refused when the targets or their rights changed since that preview.")]
	string ConfirmationCode = null,

	[property: JsonPropertyName("allow-security-object")]
	[property: Description("Allow granting create/edit/delete when the ROOT object is a security or system object (SysAdmin*, SysUser*, SysSchema*, SysPackage*, SysSettings*, SysLic*, SysProcess*, Vw*, *Right/*Rights). Default false: such a root may only be granted read.")]
	bool? AllowSecurityObject = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call before any write.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
