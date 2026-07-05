using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.Theming;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that refreshes the Creatio theme catalog cache on a target environment via the native <c>ThemeService</c>.
/// </summary>
public class ClearThemesCacheTool(
	ClearThemesCacheCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ClearThemesCacheOptions>(command, logger, commandResolver) {

	internal const string ToolName = "clear-themes-cache";

	/// <summary>Refreshes the theme catalog cache on the target environment.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Refresh the Creatio theme cache for a registered environment. For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult ClearThemesCache(
		[Description("Parameters: environment-name (required).")]
		[Required] ClearThemesCacheArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".",
			"Valid: environment-name.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return CommandExecutionResult.FromValidationError(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		ClearThemesCacheOptions options = new() {
			Environment = args.EnvironmentName
		};
		return InternalExecute<ClearThemesCacheCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>clear-themes-cache</c> tool.
/// </summary>
public sealed record ClearThemesCacheArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
