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

public class SetBackgroundColorTool(
	SetBackgroundColorCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<SetBackgroundColorOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-background-color";
	private const string ThemeArgument = "theme";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["id"] = ThemeArgument,
			["themeId"] = ThemeArgument,
			["theme-id"] = ThemeArgument,
			["theme_id"] = ThemeArgument,
			["keepIconBackground"] = "keep-icon-background",
			["keep_icon_background"] = "keep-icon-background"
		};

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Set a solid color (#RGB, #RRGGBB or #RRGGBBAA) as one custom theme's background. Requires " +
		"Creatio " + ThemeServiceRequirement.BackgroundMinVersion + " or later. The panel's own icon background is " +
		"turned off so the color is visible, unless keep-icon-background is true. For the full branding flow, read " +
		"get-guidance branding first.")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult SetBackgroundColor(
		[Description("Parameters: environment-name (required); color (required); theme (required, the theme id from list-themes); keep-icon-background (optional bool).")]
		[Required] SetBackgroundColorArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".", "Valid: environment-name, color, theme, keep-icon-background.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return CommandExecutionResult.FromValidationError(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.Color)) {
			return CommandExecutionResult.FromValidationError("color is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.Theme)) {
			return CommandExecutionResult.FromValidationError("theme is required and cannot be empty.");
		}
		return InternalExecute<SetBackgroundColorCommand>(new SetBackgroundColorOptions {
			Environment = args.EnvironmentName,
			Color = args.Color,
			Theme = args.Theme,
			KeepIconBackground = args.KeepIconBackground ?? false
		});
	}
}

public sealed record SetBackgroundColorArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("color")]
	[property: Description("Hex color (#RGB, #RRGGBB or #RRGGBBAA) to use as the background.")]
	[property: Required]
	string? Color = null,

	[property: JsonPropertyName("theme")]
	[property: Description("Id (a GUID) of the custom theme to give the background (see list-themes).")]
	[property: Required]
	string? Theme = null,

	[property: JsonPropertyName("keep-icon-background")]
	[property: Description("When true, leaves the panel's own icon background (the UsePanelIconBackground feature) as is; it can cover the color.")]
	bool? KeepIconBackground = null
) {
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
