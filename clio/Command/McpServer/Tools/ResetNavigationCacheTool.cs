using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>reset-navigation-cache</c> command.
/// </summary>
[McpServerToolType]
public sealed class ResetNavigationCacheTool(
	ResetNavigationCacheCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	ICredentialPassthroughToolGuard? passthroughGuard = null)
	: BaseTool<ResetNavigationCacheOptions>(command, logger, commandResolver, passthroughGuard: passthroughGuard) {

	/// <summary>
	/// Stable MCP tool name; identical to the CLI verb.
	/// </summary>
	internal const string ToolName = "reset-navigation-cache";

	/// <summary>
	/// Clears the navigation cache of clio's own Creatio session for the environment.
	/// </summary>
	// ReadOnly=false: the call drops and rebuilds server-side cache entries. Destructive=false: it changes no
	// data and logs nobody out. Idempotent: a repeated call leaves the same cache state.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Clears the menu cache (module structure, workplaces, sections) of clio's own Creatio session.
				 It calls ConfigurationDataService/GetData with forceGet=true, which also bumps the client cache hash.
				 It does NOT refresh other sessions or open browser tabs: for a browser tab that still shows the
				 old menu after a reload, run the in-tab call from the returned next-step, then reload that tab.
				 Use it after direct writes to SysModule / SysModuleInWorkplace (odata-update, data-binding
				 upserts) when clio's later reads must see the change; create-app, create-app-section and
				 update-app-section already do it. It is never a substitute for clear-redis-db-by-environment,
				 and Redis must not be cleared for a stale menu: that logs out every user.
				 Changes no data. Returns success, error (when the server did not confirm the reset) and
				 next-step.
				 """)]
	public ResetNavigationCacheResponse ResetNavigationCache(
		[Description("Parameters: environment-name (required unless the request carries credential passthrough, "
			+ "which supplies the target environment itself).")]
		[Required] ResetNavigationCacheArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(args.ExtensionData,
			McpToolArgumentSupport.EnvironmentNameAliases, ".", "Valid: environment-name.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return Failure(aliasError);
		}
		if (!IsPassthroughActive && string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return Failure("environment-name is required and cannot be empty.");
		}
		ResetNavigationCacheOptions options = new() { Environment = args.EnvironmentName };
		return ExecuteResolved<ResetNavigationCacheCommand, ResetNavigationCacheResponse>(options,
			resolvedCommand => {
				ResetNavigationCacheResponse response = resolvedCommand.Reset(options);
				return response.Error is null
					? response
					: response with { Error = SensitiveErrorTextRedactor.Redact(response.Error) };
			},
			Failure);
	}

	private static ResetNavigationCacheResponse Failure(string error) =>
		new() { Success = false, Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error };
}

/// <summary>
/// MCP arguments for the <c>reset-navigation-cache</c> tool.
/// </summary>
public sealed record ResetNavigationCacheArgs {
	/// <summary>
	/// Registered clio environment name. Required on every path except a credential-passthrough request,
	/// which carries the target environment itself and refuses an explicit one.
	/// </summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	public string? EnvironmentName { get; init; }

	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
