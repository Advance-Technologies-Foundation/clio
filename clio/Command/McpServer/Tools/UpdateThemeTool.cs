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
/// <c>ThemeService</c> (a full overwrite by theme id).
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

	/// <summary>Overwrites the addressed theme on the target environment with the supplied caption, CSS class name, and CSS content.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Overwrite an existing custom Creatio theme on a registered environment via the native ThemeService " +
		"(full overwrite by id; the package cannot be changed). Only inline CSS content is accepted; to supply CSS " +
		"from a file, use the clio CLI (--css-content-file) instead. For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult UpdateTheme(
		[Description("Parameters: environment-name (required), id (required), caption (required), " +
			"css-class-name (required), css-content (required).")]
		[Required] UpdateThemeArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, id, caption, css-class-name, css-content.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return CommandExecutionResult.FromValidationError(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.Id)) {
			return CommandExecutionResult.FromValidationError("id is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.Caption)) {
			return CommandExecutionResult.FromValidationError("caption is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.CssClassName)) {
			return CommandExecutionResult.FromValidationError("css-class-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.CssContent)) {
			return CommandExecutionResult.FromValidationError("css-content is required and cannot be empty.");
		}
		UpdateThemeOptions options = new() {
			Environment = args.EnvironmentName,
			Id = args.Id,
			Caption = args.Caption,
			CssClassName = args.CssClassName,
			CssContent = args.CssContent
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
	[property: Description("Id of the existing theme to overwrite.")]
	[property: Required]
	string? Id = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Human-readable theme caption (max 250).")]
	[property: Required]
	string? Caption = null,

	[property: JsonPropertyName("css-class-name")]
	[property: Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100).")]
	[property: Required]
	string? CssClassName = null,

	[property: JsonPropertyName("css-content")]
	[property: Description("Inline theme CSS content (max 1 MiB); must not be empty.")]
	[property: Required]
	string? CssContent = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
