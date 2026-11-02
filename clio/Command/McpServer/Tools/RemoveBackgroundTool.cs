using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.Branding;
using Clio.Command.Theming;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class RemoveBackgroundTool(
	RemoveBackgroundCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<RemoveBackgroundOptions>(command, logger, commandResolver) {

	internal const string ToolName = "remove-background";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["id"] = "theme",
			["themeId"] = "theme",
			["theme-id"] = "theme",
			["theme_id"] = "theme"
		};

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Remove a custom theme's own background, so its users see the site background again. " +
		"Requires Creatio " + ThemeServiceRequirement.BackgroundMinVersion + " or later on the target " +
		"environment. For the full branding flow, read get-guidance branding first.")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult RemoveBackground(
		[Description("Parameters: environment-name (required); theme (required, the theme id from list-themes).")]
		[Required] RemoveBackgroundArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".", "Valid: environment-name, theme.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return CommandExecutionResult.FromValidationError(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.Theme)) {
			return CommandExecutionResult.FromValidationError("theme is required and cannot be empty.");
		}
		return InternalExecute<RemoveBackgroundCommand>(new RemoveBackgroundOptions {
			Environment = args.EnvironmentName,
			Theme = args.Theme
		});
	}
}

public sealed record RemoveBackgroundArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("theme")]
	[property: Description("Id (a GUID) of the custom theme whose background to remove (see list-themes).")]
	[property: Required]
	string? Theme = null
) {
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
