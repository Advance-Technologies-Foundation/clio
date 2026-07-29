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
/// MCP tool that reads the content (<c>theme.css</c>) and metadata of a custom Creatio theme by id,
/// returning them as a structured result usable for an <c>update-theme</c> round-trip.
/// </summary>
public class GetThemeTool(
	GetThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<GetThemeOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-theme";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["outputFile"] = "output-file",
			["output_file"] = "output-file"
		};

	/// <summary>Reads the theme's metadata and CSS content as a structured result.</summary>
	// ReadOnly=false: with output-file set the tool writes the theme CSS to disk (a side effect), so it must
	// not advertise readOnlyHint=true. Destructive stays false — the write is confined to a trusted workspace
	// anchor or the OS temp directory (OutputPathConfinement, symlinks resolved) and refuses to overwrite an
	// existing target, rejected before any network call otherwise.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Read the content (theme.css) and metadata of a custom Creatio theme by its id. " +
		"Requires Creatio " + ThemeServiceRequirement.MinVersion + " or later on the target environment. " +
		"Returns { success, id, caption, cssClassName, cssFilePath, cssContent?, cssContentLength?, error? } — " +
		"caption, cssClassName, and cssContent feed update-theme verbatim, so use this before update-theme to " +
		"edit the current CSS instead of overwriting it blindly. Set output-file to write the CSS to disk and " +
		"keep it out of the transcript. For the theme workflow, read get-guidance theming first.")]
	public GetThemeResponse GetTheme(
		[Description("Parameters: environment-name (required); id (required); output-file (optional).")]
		[Required] GetThemeArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, id, output-file.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return GetThemeResponse.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return GetThemeResponse.Failure("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.Id)) {
			return GetThemeResponse.Failure("id is required and cannot be empty.");
		}
		GetThemeOptions options = new() {
			Environment = args.EnvironmentName,
			Id = args.Id,
			OutputFile = args.OutputFile
		};
		return ExecuteResolved<GetThemeCommand, GetThemeResponse>(options,
			resolvedCommand => {
				resolvedCommand.TryGetTheme(options, out GetThemeResponse response);
				// The error can carry a server-supplied ThemeService/transport message, so redact it before it
				// crosses into the MCP client transcript (the same boundary rule as list-themes). Success fields
				// are the round-trip payload and stay verbatim.
				return response.Success || string.IsNullOrWhiteSpace(response.Error)
					? response
					: response with { Error = SensitiveErrorTextRedactor.Redact(response.Error) };
			},
			GetThemeResponse.Failure);
	}
}

/// <summary>
/// MCP arguments for the <c>get-theme</c> tool.
/// </summary>
public sealed record GetThemeArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,
	[property: JsonPropertyName("id")]
	[property: Description("Id of the theme to read (see list-themes).")]
	[property: Required]
	string? Id = null,
	[property: JsonPropertyName("output-file")]
	[property: Description("Optional path to write the theme CSS to; when set, cssContent is omitted from the result.")]
	string? OutputFile = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
