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
/// MCP surface of <c>set-default-record-rights</c>: changes one default record rule and/or the "Use record permissions"
/// switch of ONE object. Destructive: the host's approval of the call is the confirmation — the arguments name the
/// rule and every switch transition. <c>preview</c> is a dry run that writes nothing.
/// </summary>
[McpServerToolType]
public sealed class SetDefaultRecordRightsTool(
	SetDefaultRecordRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetDefaultRecordRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-default-record-rights";

	internal const string ValidArguments =
		"Valid: environment-name, entity-schema-name, author, grantee, operations, level, do-not-apply-for-manager, "
		+ "revoke, enable-record-permissions, disable-record-permissions, preview.";

	// The worker is killed at its budget (120 s by default). A call makes up to seven requests in sequence: two
	// SysAdminUnit lookups, the SysSchema lookup, the read, the record count, the save and the read-back. Each gets one
	// attempt of at most 25 s, all share a 100 s limit, and the save is sent only while 50 s are left for it and the
	// read-back — the same limits as set-object-rights.
	internal const int McpRequestTimeoutMilliseconds = SetObjectRightsTool.McpRequestTimeoutMilliseconds;

	internal static readonly TimeSpan McpCallBudget = SetObjectRightsTool.McpCallBudget;

	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Turn RECORD permissions on/off for ONE object and grant or revoke ONE of its default record rules — the \"Use record permissions\" layer of Object permissions (DESTRUCTIVE — changes access rights). " +
		"A default rule says: records created by members of author get read/edit/delete for grantee. Rules have no order and add up. One call changes at most one rule (one author+grantee pair) and/or the switch; every other rule is kept exactly as read, and the read-back proves it. " +
		"A call is EITHER a rule change (author + grantee + operations, all three; author and grantee are SysAdminUnit ids that must exist; operations read,edit,delete is required, nothing is granted by default) OR a switch-only change (none of them, plus enable-record-permissions or disable-record-permissions). " +
		"level granted|delegated (default granted) is what a grant sets for the named operations; do-not-apply-for-manager true/false sets the rule's flag (omitted: kept, false for a new rule). revoke=true sets the named operations to not set; a rule left with no right is removed; a revoke is allowed while record permissions are OFF (it cleans a stored rule before an enable brings it into effect). " +
		"The switch changes only with its flag: a grant on an object whose record permissions are OFF is refused without enable-record-permissions (the refusal names the stored rules that would come into effect); disable-record-permissions is a call of its own (only the object and the flag; it keeps every rule), and a revoke never changes the switch. Turning record permissions ON with NO rule means every user sees only the records they create. " +
		"The tool NEVER applies the rules to existing records: an enable or a rule change affects records created from then on, and the result reports the number of existing records. Ask the user whether and when to apply them with apply-default-record-rights; never run it on your own. " +
		"Refused, writing nothing: duplicate author+grantee rules or invalid levels in the stored list (repair them in the designer). preview=true is a dry run. Read back with get-object-rights. Does NOT change operation or column permissions. " +
		"Unknown or misspelled argument names are REFUSED before any read or write.")]
	public ObjectRightsToolResponse SetDefaultRecordRights(
		[Description("Parameters: environment-name, entity-schema-name (required); author, grantee, operations (a rule change, together); level, do-not-apply-for-manager, revoke, enable-record-permissions, disable-record-permissions, preview (optional).")]
		[Required]
		SetDefaultRecordRightsArgs args) {
		// A long-tail tool reached through clio-run: the serializer silently DROPS unknown keys, and on a destructive tool
		// a typo can turn into the opposite change ({"revok":true} would grant), so refuse before any read or write.
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ObjectRightsToolResponse.FromValidationError(aliasError);
		}
		try {
			return ObjectRightsToolResponse.From(InternalExecute<SetDefaultRecordRightsCommand>(BuildOptions(args)));
		} catch (Exception ex) {
			return ObjectRightsToolResponse.FromError(ex);
		}
	}

	/// <summary>The command options an MCP call runs with.</summary>
	/// <param name="args">The call's arguments.</param>
	/// <returns>The options, with the MCP request limits.</returns>
	internal static SetDefaultRecordRightsOptions BuildOptions(SetDefaultRecordRightsArgs args) {
		bool preview = args.Preview ?? false;
		return new SetDefaultRecordRightsOptions {
			Environment = args.EnvironmentName,
			EntitySchemaName = args.EntitySchemaName,
			Author = args.Author,
			Grantee = args.Grantee,
			Operations = args.Operations,
			Level = args.Level,
			DoNotApplyForManager = args.DoNotApplyForManager,
			Revoke = args.Revoke ?? false,
			EnableRecordPermissions = args.EnableRecordPermissions ?? false,
			DisableRecordPermissions = args.DisableRecordPermissions ?? false,
			Preview = preview,
			// MCP cannot prompt anyone: the host's approval of this call is the confirmation. A preview writes nothing.
			Confirm = !preview,
			TimeOut = McpRequestTimeoutMilliseconds,
			MaxAttempts = 1,
			CallBudget = McpCallBudget
		};
	}
}

/// <summary>Arguments of the <c>set-default-record-rights</c> MCP tool.</summary>
public sealed record SetDefaultRecordRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("The one object (entity schema) whose record permissions are changed.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("author")]
	[property: Description("SysAdminUnit id (role or user) of the rule's author: the rule applies to records created by its members. Given with grantee and operations, or not at all (switch-only call).")]
	string? Author = null,

	[property: JsonPropertyName("grantee")]
	[property: Description("SysAdminUnit id (role or user) that gets the rights on those records. Given with author and operations.")]
	string? Grantee = null,

	[property: JsonPropertyName("operations")]
	[property: Description("Comma-separated operations to grant or revoke: read,edit,delete. Required for a rule change: nothing is granted by default.")]
	string? Operations = null,

	[property: JsonPropertyName("level")]
	[property: Description("Level a grant sets for the named operations: granted (default) or delegated (granted with the right to delegate).")]
	string? Level = null,

	[property: JsonPropertyName("do-not-apply-for-manager")]
	[property: Description("Grant only: set the rule's 'Do not apply for manager' flag (managers of the grantee role do not inherit the rule). Omitted: an existing rule keeps its flag, a new rule gets false.")]
	bool? DoNotApplyForManager = null,

	[property: JsonPropertyName("revoke")]
	[property: Description("Set the named operations to not set instead of granting them (default false). A rule left with no right is removed.")]
	bool? Revoke = null,

	[property: JsonPropertyName("enable-record-permissions")]
	[property: Description("Turn the object's record permissions ON (default false), alone or with a grant (required for a grant on an object whose record permissions are off). Never with a revoke.")]
	bool? EnableRecordPermissions = null,

	[property: JsonPropertyName("disable-record-permissions")]
	[property: Description("Turn the object's record permissions OFF (default false): every user with read operation rights reaches every record. A call of its own: no author, grantee, operations or revoke; every rule is kept.")]
	bool? DisableRecordPermissions = null,

	[property: JsonPropertyName("preview")]
	[property: Description("Dry run (default false): write nothing and show what the call would change and whether it would be refused.")]
	bool? Preview = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call before any read or write.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
