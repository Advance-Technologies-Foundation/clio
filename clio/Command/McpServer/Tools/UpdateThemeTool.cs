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
/// MCP tool that overwrites an existing custom Creatio theme on a target environment via the native
/// <c>ThemeService</c> (a full overwrite by theme id), or changes only its background and keeps the omitted fields.
/// </summary>
public class UpdateThemeTool(
	UpdateThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<UpdateThemeOptions>(command, logger, commandResolver) {

	internal const string ToolName = "update-theme";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["cssContent"] = "css-content",
			["css_content"] = "css-content",
			["cssClassName"] = "css-class-name",
			["css_class_name"] = "css-class-name"
		};

	/// <summary>Overwrites the addressed theme on the target environment with the supplied caption, CSS class name, and CSS content, or changes its background.</summary>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Update a custom Creatio theme or its background on a registered environment via the native ThemeService " +
		"(full overwrite by id without a background argument; the package cannot be changed). Requires Creatio " + ThemeServiceRequirement.MinVersion +
		" or later on the target environment. Only inline CSS content is accepted. " +
		"A background argument is at most one of background-image-id (an image from upload-image) or background-color; " +
		"null for either removes the theme's own background. It needs Creatio " +
		ThemeServiceRequirement.BackgroundMinVersion + " or later, " +
		"checked before any write. Without one, caption, css-class-name and css-content are all required; with one, " +
		"they are optional and the omitted ones are kept. An image or a color turns the environment-wide " +
		"UsePanelIconBackground feature off for All employees unless keep-icon-background is true, and an image is " +
		"bound into the theme's package; problems after the write come back as warnings in the log. " +
		"For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult UpdateTheme(
		[Description("Parameters: environment-name (required), id (required), caption, css-class-name, css-content, " +
			"background-image-id, background-color, keep-icon-background.")]
		[Required] UpdateThemeArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, id, caption, css-class-name, css-content, background-image-id, " +
			"background-color, keep-icon-background.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return CommandExecutionResult.FromValidationError(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.Id)) {
			return CommandExecutionResult.FromValidationError("id is required and cannot be empty.");
		}
		bool hasBackground = ThemeBackground.IsGiven(args.BackgroundImageId, args.BackgroundColor);
		if (!hasBackground && string.IsNullOrWhiteSpace(args.Caption)) {
			return CommandExecutionResult.FromValidationError("caption is required and cannot be empty.");
		}
		if (!hasBackground && string.IsNullOrWhiteSpace(args.CssClassName)) {
			return CommandExecutionResult.FromValidationError("css-class-name is required and cannot be empty.");
		}
		if (!hasBackground && string.IsNullOrWhiteSpace(args.CssContent)) {
			return CommandExecutionResult.FromValidationError("css-content is required and cannot be empty.");
		}
		UpdateThemeOptions options = new() {
			Environment = args.EnvironmentName,
			Id = args.Id,
			Caption = args.Caption,
			CssClassName = args.CssClassName,
			CssContent = args.CssContent,
			BackgroundImageId = args.BackgroundImageId,
			BackgroundColor = args.BackgroundColor,
			KeepIconBackground = args.KeepIconBackground ?? false
		};
		return InternalExecute<UpdateThemeCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>update-theme</c> tool.
/// </summary>
public sealed record UpdateThemeArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("id")]
	[property: Description("Id (a GUID) of the existing theme to overwrite.")]
	[property: Required]
	string? Id = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Human-readable theme caption (max 250).")]
	string? Caption = null,

	[property: JsonPropertyName("css-class-name")]
	[property: Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100).")]
	string? CssClassName = null,

	[property: JsonPropertyName("css-content")]
	[property: Description("Inline theme CSS content (max 1 MiB); must not be empty.")]
	string? CssContent = null,

	[property: JsonPropertyName("keep-icon-background")]
	[property: Description("When true, leaves the UsePanelIconBackground feature as is, although the panel's icon background can hide the theme background.")]
	bool? KeepIconBackground = null
) {
	private readonly string? _backgroundImageId;

	private readonly string? _backgroundColor;

	[JsonPropertyName("background-image-id")]
	[Description("Id of an image uploaded with upload-image, set as the theme background; null removes the theme's own background, so its users see the site background again.")]
	public string? BackgroundImageId {
		get => _backgroundImageId;
		init => _backgroundImageId = value ?? ThemeBackground.RemovalValue;
	}

	[JsonPropertyName("background-color")]
	[Description("Solid theme background color: #RGB, #RRGGBB or #RRGGBBAA; null removes the theme's own background, so its users see the site background again.")]
	public string? BackgroundColor {
		get => _backgroundColor;
		init => _backgroundColor = value ?? ThemeBackground.RemovalValue;
	}

	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
