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
/// MCP tool that creates a custom Creatio theme on a target environment via the native <c>ThemeService</c>,
/// returning a structured result with the theme id.
/// </summary>
public class CreateThemeTool(
	CreateThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<CreateThemeOptions>(command, logger, commandResolver) {

	internal const string ToolName = "create-theme";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["cssContent"] = "css-content",
			["css_content"] = "css-content",
			["cssClassName"] = "css-class-name",
			["css_class_name"] = "css-class-name",
			["packageName"] = "package-name",
			["package_name"] = "package-name"
		};

	/// <summary>Creates the theme on the target environment and returns a structured result carrying the effective theme id.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
	 Description("Create a custom Creatio theme on a registered environment via the native ThemeService. " +
		"Returns { success, id, error? } where id is the created theme's id, auto-generated when omitted. " +
		"Only inline CSS content is accepted; to supply CSS from a file, use the clio CLI (--css-content-file) instead. " +
		"For the theme workflow, read get-guidance theming first.")]
	public CreateThemeResult CreateTheme(
		[Description("Parameters: environment-name (required), css-content (required), " +
			"css-class-name (optional), caption (optional), id (optional), package-name (optional).")]
		[Required] CreateThemeArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, css-content, css-class-name, caption, id, package-name.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return CreateThemeResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CreateThemeResult.Failure("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.CssContent)) {
			return CreateThemeResult.Failure("css-content is required and cannot be empty.");
		}
		CreateThemeOptions options = new() {
			Environment = args.EnvironmentName,
			Caption = args.Caption,
			CssClassName = args.CssClassName,
			CssContent = args.CssContent,
			Id = args.Id,
			PackageName = args.PackageName
		};
		return Execute(options);
	}

	private CreateThemeResult Execute(CreateThemeOptions options) {
		return ExecuteWithCleanLog(() => {
			CreateThemeCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<CreateThemeCommand>(options);
			}
			catch (Exception ex) {
				return CreateThemeResult.Failure(ex.Message);
			}
			try {
				if (!resolvedCommand.TryCreateTheme(options, out string createdId, out string errorMessage)) {
					return CreateThemeResult.Failure(string.IsNullOrWhiteSpace(errorMessage)
						? "CreateTheme returned success=false."
						: errorMessage);
				}
				return CreateThemeResult.Successful(createdId);
			}
			catch (Exception ex) {
				return CreateThemeResult.Failure(ex.Message);
			}
		});
	}
}

/// <summary>
/// MCP arguments for the <c>create-theme</c> tool.
/// </summary>
public sealed record CreateThemeArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("css-content")]
	[property: Description("Inline theme CSS content (max 1 MiB); must not be empty.")]
	[property: Required]
	string? CssContent = null,

	[property: JsonPropertyName("css-class-name")]
	[property: Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100); derived from caption (lowercased and hyphenated) when omitted — pass caption and omit this to let clio derive it.")]
	string? CssClassName = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Human-readable theme name/caption (max 250); clio derives css-class-name from it (lowercased and hyphenated) when css-class-name is omitted.")]
	string? Caption = null,

	[property: JsonPropertyName("id")]
	[property: Description("Theme id (^[A-Za-z0-9_-]+$, max 100); an auto-generated UUID is used and returned when omitted.")]
	string? Id = null,

	[property: JsonPropertyName("package-name")]
	[property: Description("Owning package name; the environment's CurrentPackageId system setting is used when omitted.")]
	string? PackageName = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured result of the <c>create-theme</c> MCP tool.
/// </summary>
public sealed record CreateThemeResult {
	/// <summary>Whether the theme was created.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The effective theme id (supplied or auto-generated); omitted on failure.</summary>
	[JsonPropertyName("id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Id { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result carrying the effective theme id.</summary>
	public static CreateThemeResult Successful(string id) {
		return new CreateThemeResult {
			Success = true,
			Id = id
		};
	}

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static CreateThemeResult Failure(string error) {
		return new CreateThemeResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}
