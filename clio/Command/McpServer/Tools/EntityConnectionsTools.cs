using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.EntityConnections;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP surface of <c>get-entity-connections</c>: reads an object's effective "Connections" and the package's
/// <c>EntityConnections</c> add-on content. Long-tail (reached through <c>clio-run</c>).
/// </summary>
[McpServerToolType]
public sealed class GetEntityConnectionsTool(
	GetEntityConnectionsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetEntityConnectionsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-entity-connections";

	internal const string ValidArguments =
		"Valid: entity-schema-name, package-name, environment-name, uri, login, password.";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Read an object's Connections — the ordered lookup columns of the Object designer \"Connections\" node, shown as fields by the Freedom UI crt.Connections page component. " +
		"Returns the EFFECTIVE ordered list (connections[]: columnName, columnUId, caption, referenceSchemaName, position) as the server merges the EntityConnection rows with the EntityConnections add-on of every package layer, plus packageLayer (items/removed: the add-on content read for package-name, parent layers folded with it). " +
		"The list reflects the add-on only when the UseConnectionsComponent feature is on (CrtUIv2 EntityConnectionsService/GetConnections); with it off, or when that endpoint is unavailable (then read from the legacy EntityConnection table, with a warning), only the table rows are listed. " +
		"An entry with a null columnName points to a column not visible from package-name; a warning names it, and it must not be sent back. " +
		"Read-only — makes no changes. Use it before update-entity-connections, which takes the FULL desired ordered list. " +
		"Unknown or misspelled argument names are refused.")]
	public EntityConnectionsResponse GetEntityConnections(
		[Description("Parameters: entity-schema-name, package-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] GetEntityConnectionsArgs args) {
		string aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args?.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return EntityConnectionsResponse.Fail(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args?.EntitySchemaName)) {
			return EntityConnectionsResponse.Fail(EntityConnectionsService.EntitySchemaNameRequired);
		}
		if (string.IsNullOrWhiteSpace(args.PackageName)) {
			return EntityConnectionsResponse.Fail(EntityConnectionsService.PackageNameRequired);
		}

		GetEntityConnectionsOptions options = new() {
			EntitySchemaName = args.EntitySchemaName,
			PackageName = args.PackageName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		// The options-aware overload keys the execution lock on this call's environment, so one tenant's read does
		// not hold every other environment behind it.
		return ExecuteWithCleanLog(options, () => {
			GetEntityConnectionsCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetEntityConnectionsCommand>(options);
			} catch (Exception ex) {
				return EntityConnectionsResponse.Fail(ex.Message);
			}
			resolvedCommand.TryGet(options, out EntityConnectionsResponse response);
			return response;
		});
	}
}

/// <summary>
/// MCP surface of <c>update-entity-connections</c>: sets an object's "Connections" to a full ordered list of lookup
/// columns by writing the package's <c>EntityConnections</c> add-on layer. Long-tail (reached through <c>clio-run</c>).
/// </summary>
[McpServerToolType]
public sealed class UpdateEntityConnectionsTool(
	UpdateEntityConnectionsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<UpdateEntityConnectionsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "update-entity-connections";

	internal const string ValidArguments =
		"Valid: entity-schema-name, package-name, connections, environment-name, uri, login, password.";

	// Destructive: a column left out of `connections` is removed from the object's Connections.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Set an object's Connections (the Object designer \"Connections\" node; fields of the Freedom UI crt.Connections component) to the FULL desired ordered list of lookup column names. " +
		"Read-modify-write: call get-entity-connections first, change its connections[].columnName list (add, remove, reorder), and send the whole list back — a column left out is REMOVED, an empty list removes every connection. " +
		"Only LOOKUP columns of the object (own or inherited) are accepted; an unknown, non-lookup or repeated column fails with no write. " +
		"Do not send entries with a null columnName (columns not visible from package-name); they are kept as they are. " +
		"Writes ONLY the object's EntityConnections add-on in package-name, exactly as the Object designer does (only the difference from the parent layers); never rows of the legacy EntityConnection table. That list drives crt.Connections and, with the UseConnectionsComponent feature on, the server's merged list (CrtUIv2 EntityConnectionsService/GetConnections); with the feature off the server ignores the add-on and returns the table rows only. " +
		"Then resets the client script cache and returns the state read back with changed=true; when the list already matches nothing is written (changed=false). " +
		"Refuses to write when the effective list cannot be read with the add-on applied (EntityConnectionsService unavailable or not applying the add-on). A column removed by another package layer cannot be brought back from this package; a warning says so. " +
		"Unknown or misspelled argument names are refused.")]
	public EntityConnectionsResponse UpdateEntityConnections(
		[Description("Parameters: entity-schema-name, package-name, connections (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] UpdateEntityConnectionsArgs args) {
		string aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args?.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return EntityConnectionsResponse.Fail(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args?.EntitySchemaName)) {
			return EntityConnectionsResponse.Fail(EntityConnectionsService.EntitySchemaNameRequired);
		}
		if (string.IsNullOrWhiteSpace(args.PackageName)) {
			return EntityConnectionsResponse.Fail(EntityConnectionsService.PackageNameRequired);
		}
		if (args.Connections is null) {
			return EntityConnectionsResponse.Fail(EntityConnectionsService.ConnectionsRequired);
		}

		UpdateEntityConnectionsOptions options = new() {
			EntitySchemaName = args.EntitySchemaName,
			PackageName = args.PackageName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		IReadOnlyList<string> connections = args.Connections;
		return ExecuteWithCleanLog(options, () => {
			UpdateEntityConnectionsCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<UpdateEntityConnectionsCommand>(options);
			} catch (Exception ex) {
				return EntityConnectionsResponse.Fail(ex.Message);
			}
			// The list goes to the command as given (options.Connections stays unset), so a column name is never
			// re-split.
			resolvedCommand.TryUpdate(options, connections, out EntityConnectionsResponse response);
			return response;
		});
	}
}

/// <summary>Arguments of the <c>get-entity-connections</c> MCP tool.</summary>
public sealed record GetEntityConnectionsArgs(
	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Object (entity schema) name, e.g. 'Account'.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Package whose EntityConnections add-on layer is read.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName = null,

	[property: JsonPropertyName("uri")]
	[property: Description(McpToolDescriptions.Uri)]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Arguments of the <c>update-entity-connections</c> MCP tool.</summary>
public sealed record UpdateEntityConnectionsArgs(
	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Object (entity schema) name, e.g. 'Account'.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Package whose EntityConnections add-on layer is written.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("connections")]
	[property: Description("The FULL desired ordered list of lookup column names (codes), e.g. [\"Owner\", \"PrimaryContact\"]. A column left out is removed; [] removes every connection.")]
	[property: Required]
	string[] Connections,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName = null,

	[property: JsonPropertyName("uri")]
	[property: Description(McpToolDescriptions.Uri)]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
