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
/// MCP tool that deletes a custom Creatio theme from a target environment via the native <c>ThemeService</c>.
/// Destructive and not idempotent: deleting an unknown id is reported as a failure.
/// </summary>
public class DeleteThemeTool(
	DeleteThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<DeleteThemeOptions>(command, logger, commandResolver) {

	internal const string ToolName = "delete-theme";

	/// <summary>Deletes the addressed theme from the target environment.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Delete a custom Creatio theme from a registered environment via the native ThemeService. " +
		"Requires Creatio " + ThemeServiceRequirement.MinVersion + " or later on the target environment. " +
		"Deleting an unknown id is an error (not idempotent). For the theme workflow, read get-guidance theming first.")]
	public CommandExecutionResult DeleteTheme(
		[Description("Parameters: environment-name (required), id (required).")]
		[Required] DeleteThemeArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".",
			"Valid: environment-name, id.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return CommandExecutionResult.FromValidationError(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CommandExecutionResult.FromValidationError("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.Id)) {
			return CommandExecutionResult.FromValidationError("id is required and cannot be empty.");
		}
		DeleteThemeOptions options = new() {
			Environment = args.EnvironmentName,
			Id = args.Id
		};
		return InternalExecute<DeleteThemeCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>delete-theme</c> tool.
/// </summary>
public sealed record DeleteThemeArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("id")]
	[property: Description("Id of the theme to delete.")]
	[property: Required]
	string? Id = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
