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
/// MCP surface of <c>set-column-rights</c>: sets, moves or removes one role's row in ONE column of ONE object, or turns
/// the object's column permissions on or off. Destructive: the call applies the change, and the host's approval of the
/// call is the confirmation — the arguments name every access-changing transition. <c>preview</c> writes nothing.
/// Idempotent: a repeated identical call finds its change in place and writes nothing — a repeated removal included,
/// which answers "no change" because the row is already gone.
/// </summary>
[McpServerToolType]
public sealed class SetColumnRightsTool(
	SetColumnRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetColumnRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-column-rights";

	// The worker is killed at its budget (120 s by default). A write makes up to thirteen requests in sequence (grantee,
	// SysSchema lookup, read, storage count and rows, schema columns, a read by column UId when turning column
	// permissions on or, for a removal, whether the grantee is external, the object and its rows again right before the
	// save, the save, and the read-back of the object and of storage, counted). Each gets one attempt of at most 20 s, all share a 100 s limit, and the save is sent only
	// while two timeouts (40 s) are left for it and a read-back request; a read-back the limit cuts short reports the
	// save as saved but NOT verified.
	internal const int McpRequestTimeoutMilliseconds = 20_000;

	internal static readonly TimeSpan McpCallBudget = TimeSpan.FromSeconds(100);

	internal const string ValidArguments =
		"Valid: environment-name, entity-schema-name, column, grantee, access, remove-row, position, "
		+ "enable-column-permissions, disable-column-permissions, preview.";

	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Changes column permissions. Sets one role's access (denied/read/edit) to ONE column of ONE object, moves or removes that role's row, or turns the object's column permissions on or off - the COLUMN (field-level) layer (DESTRUCTIVE - changes access rights). " +
		"entity-schema-name is the object's CODE: a title is refused, naming the code to pass. column is the column CODE. grantee is a SysAdminUnit id (names are not unique); it must exist. " +
		"access sets the grantee's row: a new row goes to the TOP (position 0) unless position is given - All employees' to the BOTTOM, so a broad grant does not override specific rows; an existing one keeps its place. Per Creatio's documentation the higher row wins for a user in several roles (documented, not measured); a column with no rows leaves every user to the object's operation permissions. position (0 first) places or, alone, moves the row and renumbers the column's rows 0..n-1. An All employees row above a row with another level is warned about (the All employees row decides for its members); a column whose rows share a position is refused. remove-row=true deletes the grantee's row (a repeated removal is no change); removing a column's last row leaves it with no rows, so every user follows the object's operation permissions - the result says when that widens access. " +
		"The switch changes only through its flags: a grant while column permissions are OFF is refused unless enable-column-permissions is passed; a move or a removal while OFF is refused; enable-column-permissions turns them on and gives EVERY column with no stored row the default row (All employees: edit), naming those columns in the result, and rows kept on other columns since a disable apply again - run preview=true before an enable; disable-column-permissions ALONE turns them off (rows kept, not applied; the result names the columns with no rows, which a later enable through clio gives the default row). " +
		"A save with a column list gives every column it does not name the default row, so the call builds the list from storage: every column whose stored rows are not the default, the changed one with its new rows; a column with no rows is sent with none while ON. The call then reads storage back: a difference fails it. " +
		"Calls for one object must NOT run in parallel: a second write of the object (also set-object-rights, set-default-record-rights) while one runs is refused, and a save is aborted when the object or its stored rows changed since they were read. While ON, the call is refused when storage disagrees with the service's list. " +
		"The rows are written to the environment only, NOT bound to a package. preview=true writes nothing and shows the column's rows before and after. Read with get-column-rights. " +
		"Unknown argument names are REFUSED before any read or write.")]
	public ObjectRightsToolResponse SetColumnRights(
		[Description("Parameters: environment-name, entity-schema-name (required); column, grantee and access (a grant); column, grantee and remove-row or position (a removal or a move); enable-column-permissions, disable-column-permissions, preview (optional).")]
		[Required]
		SetColumnRightsArgs args) {
		// The serializer silently DROPS unknown keys; on a destructive tool a typo ({"remove_row":true}) would turn a
		// removal into a different call, so it is refused before any read or write.
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ObjectRightsToolResponse.FromValidationError(aliasError);
		}
		try {
			return ObjectRightsToolResponse.From(InternalExecute<SetColumnRightsCommand>(BuildOptions(args)));
		} catch (Exception ex) {
			return ObjectRightsToolResponse.FromError(ex);
		}
	}

	/// <summary>The command options an MCP call runs with.</summary>
	/// <param name="args">The call's arguments.</param>
	/// <returns>The options, with the MCP request limits above.</returns>
	internal static SetColumnRightsOptions BuildOptions(SetColumnRightsArgs args) {
		bool preview = args.Preview ?? false;
		return new SetColumnRightsOptions {
			Environment = args.EnvironmentName,
			EntitySchemaName = args.EntitySchemaName,
			Column = args.Column,
			Grantee = args.Grantee,
			Access = args.Access,
			RemoveRow = args.RemoveRow ?? false,
			Position = args.Position,
			EnableColumnPermissions = args.EnableColumnPermissions ?? false,
			DisableColumnPermissions = args.DisableColumnPermissions ?? false,
			Preview = preview,
			// MCP cannot prompt anyone: the host's approval of this call is the confirmation. A preview writes nothing,
			// so it is never confirmed.
			Confirm = !preview,
			TimeOut = McpRequestTimeoutMilliseconds,
			MaxAttempts = 1,
			CallBudget = McpCallBudget
		};
	}
}

/// <summary>Arguments of the <c>set-column-rights</c> MCP tool.</summary>
/// <param name="EnvironmentName">The registered environment.</param>
/// <param name="EntitySchemaName">The one object whose column permissions change.</param>
/// <param name="Column">The column code whose rows change.</param>
/// <param name="Grantee">The SysAdminUnit id of the role or user whose row changes.</param>
/// <param name="Access">The level the grantee's row gets: denied, read or edit.</param>
/// <param name="RemoveRow">Remove the grantee's row from the column.</param>
/// <param name="Position">Where the grantee's row goes in the column's stored position order, 0 first.</param>
/// <param name="EnableColumnPermissions">Turn column permissions on: with a grant, or alone.</param>
/// <param name="DisableColumnPermissions">Turn column permissions off, alone.</param>
/// <param name="Preview">Write nothing; show what the call would change.</param>
public sealed record SetColumnRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("The one object whose column permissions change, by its CODE (entity schema name). A title is refused, naming the code it belongs to.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("column")]
	[property: Description("Column code (for example UsrDescription) whose rows change. Required unless the call only turns column permissions on or off.")]
	string? Column = null,

	[property: JsonPropertyName("grantee")]
	[property: Description("SysAdminUnit id (role or user) whose row changes; names are not unique. Required unless the call only turns column permissions on or off.")]
	string? Grantee = null,

	[property: JsonPropertyName("access")]
	[property: Description("Access the grantee's row gets: denied, read or edit. A grantee with no row gets one at the top (position 0, which per Creatio's documentation wins) unless position is given - All employees at the bottom; an existing row keeps its place and gets the new level.")]
	string? Access = null,

	[property: JsonPropertyName("remove-row")]
	[property: Description("Remove the grantee's row from the column (default false); no change when it has none. Removing the column's last row leaves it with no rows: every user then follows the object's operation permissions (Creatio documentation); the summary says when that widens access. A call of its own: not with access, position or a switch flag.")]
	bool? RemoveRow = null,

	[property: JsonPropertyName("position")]
	[property: Description("Position in the column's stored position order, 0 first: with access it places the row, alone it moves the grantee's row; the column's rows are renumbered 0..n-1. A position past the end is refused.")]
	int? Position = null,

	[property: JsonPropertyName("enable-column-permissions")]
	[property: Description("Turn the object's column permissions ON (default false, which refuses a grant while they are off): with access, or alone. Rows kept on columns since a disable apply again, and every column with no stored row gets the default row (All employees: edit); the result names both. Run with preview=true first.")]
	bool? EnableColumnPermissions = null,

	[property: JsonPropertyName("disable-column-permissions")]
	[property: Description("Turn the object's column permissions OFF, keeping every stored row, not applied (default false). The result names the columns with no rows; a later enable through clio gives them the default row. A call of its own.")]
	bool? DisableColumnPermissions = null,

	[property: JsonPropertyName("preview")]
	[property: Description("Dry run (default false): write nothing and show what the call would change and whether it would be refused.")]
	bool? Preview = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call before any read or write.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
