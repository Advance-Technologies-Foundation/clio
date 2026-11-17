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
/// MCP surface of <c>get-column-rights</c>: reads the column (field-level) permissions of an object — the switch and,
/// per column, every role's access level in stored position order.
/// </summary>
[McpServerToolType]
public sealed class GetColumnRightsTool(
	GetColumnRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetColumnRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-column-rights";

	internal const string ValidArguments = "Valid: environment-name, entity-schema-name, column, grantee.";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Reads column permissions. COLUMN (field-level) layer of an object: the switch from the service read get-object-rights uses, the rows from storage (VwSysEntitySchemaColumnRight), because the service read omits default columns. " +
		"entity-schema-name is the object's code; when no object has that code, the object with that title is read (several are refused, listing them). " +
		"Reports whether column permissions are ON and, per column with rows (its code), every row in stored position order: [position] (0 first; which row wins for a user in several roles is not measured), grantee name and id, access denied/read/edit. " +
		"ON: columns whose only row is the default (All employees, edit) are listed as default, not a restriction for members of All employees (external/portal users are not in it); schema columns with NO row are listed as accessible only to system administrators (Creatio documentation). OFF: rows are stored but not applied (default columns summarised). If storage cannot be read, the service read is shown with a warning: it omits default columns, and its columns with no rows are listed as locked. " +
		"column limits it to one column code; grantee lists, per column, the rows whose grantee is exactly that id first, then the other rows (role membership is not resolved). " +
		"Facts only, no coverage verdict. Fails (success=false) when the object cannot be read. Unknown argument names are refused.")]
	public ObjectRightsToolResponse GetColumnRights(
		[Description("Parameters: environment-name, entity-schema-name (required); column, grantee (optional).")]
		[Required]
		GetColumnRightsArgs args) {
		// The serializer would silently drop a misspelled key — e.g. "column-name" — and the read would then answer for
		// EVERY column instead of the one asked about. Refuse it instead.
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ObjectRightsToolResponse.FromValidationError(aliasError);
		}
		try {
			return ObjectRightsToolResponse.From(InternalExecute<GetColumnRightsCommand>(BuildOptions(args)));
		} catch (Exception ex) {
			return ObjectRightsToolResponse.FromError(ex);
		}
	}

	/// <summary>The command options an MCP call runs with.</summary>
	/// <param name="args">The call's arguments.</param>
	/// <returns>The options, with the MCP request limits of get-object-rights.</returns>
	internal static GetColumnRightsOptions BuildOptions(GetColumnRightsArgs args) => new() {
		Environment = args.EnvironmentName,
		EntitySchemaName = args.EntitySchemaName,
		Column = args.Column,
		Grantee = args.Grantee,
		TimeOut = GetObjectRightsTool.McpReadTimeoutMilliseconds,
		MaxAttempts = 1,
		ReadBudget = GetObjectRightsTool.McpReadBudget
	};
}

/// <summary>Arguments of the <c>get-column-rights</c> MCP tool.</summary>
public sealed record GetColumnRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("The object to read: its code (entity schema name), or its title when no object has that code.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("column")]
	[property: Description("Optional column code (for example UsrDescription): show only that column. Omit to list every column with rows.")]
	string Column = null,

	[property: JsonPropertyName("grantee")]
	[property: Description("Optional SysAdminUnit id (role or user): per column, show the rows whose grantee is exactly that id first, then the column's other rows. Role membership is not resolved: a user's id does not match its roles' rows. Omit to list every row.")]
	string Grantee = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
