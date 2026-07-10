using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Theming;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Read-only MCP tool that lists the custom Creatio themes available on a target environment, returning them
/// as a structured result.
/// </summary>
public class ListThemesTool(
	ListThemesCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ListThemesOptions>(command, logger, commandResolver) {

	internal const string ToolName = "list-themes";

	/// <summary>Lists the custom themes available on the target environment as a structured result.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("List the custom Creatio themes available on a registered environment. " +
		"Returns { success, themes:[{ id, caption, cssClassName, cssFilePath }], error? }. " +
		"An empty themes array means the catalog is empty or the caller lacks the CanCustomizeBranding license. " +
		"For the theme workflow, read get-guidance theming first.")]
	public ListThemesResult ListThemes(
		[Description("Parameters: environment-name (required).")]
		[Required] ListThemesArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".",
			"Valid: environment-name.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ListThemesResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return ListThemesResult.Failure("environment-name is required and cannot be empty.");
		}
		ListThemesOptions options = new() {
			Environment = args.EnvironmentName
		};
		return Execute(options);
	}

	private ListThemesResult Execute(ListThemesOptions options) {
		return ExecuteWithCleanLog(() => {
			ListThemesCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ListThemesCommand>(options);
			}
			catch (Exception ex) {
				return ListThemesResult.Failure(ex.Message);
			}
			try {
				if (!resolvedCommand.TryGetAvailableThemes(options,
						out IReadOnlyList<ThemeDescriptor> themes, out string errorMessage)) {
					return ListThemesResult.Failure(string.IsNullOrWhiteSpace(errorMessage)
						? "GetAvailableThemes returned success=false."
						: errorMessage);
				}
				return ListThemesResult.Successful(themes);
			}
			catch (Exception ex) {
				return ListThemesResult.Failure(ex.Message);
			}
		});
	}
}

/// <summary>
/// MCP arguments for the <c>list-themes</c> tool.
/// </summary>
public sealed record ListThemesArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured result of the <c>list-themes</c> MCP tool.
/// </summary>
public sealed record ListThemesResult {
	/// <summary>Whether the theme catalog was read successfully.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The available themes; empty when none exist or the caller is unlicensed. Omitted on failure.</summary>
	[JsonPropertyName("themes")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<ThemeDescriptorResult> Themes { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>
	/// Creates a success result carrying the resolved theme catalog. Server-provided fields are
	/// control-character-stripped and length-capped (as in the CLI printer) before being returned.
	/// </summary>
	public static ListThemesResult Successful(IReadOnlyList<ThemeDescriptor> themes) {
		return new ListThemesResult {
			Success = true,
			Themes = (themes ?? Array.Empty<ThemeDescriptor>())
				.Select(theme => new ThemeDescriptorResult(
					TextUtilities.SanitizeForDisplay(theme.Id ?? string.Empty, ThemeParameterValidator.MaxIdLength),
					TextUtilities.SanitizeForDisplay(theme.Caption ?? string.Empty, ThemeParameterValidator.MaxCaptionLength),
					TextUtilities.SanitizeForDisplay(theme.CssClassName ?? string.Empty, ThemeParameterValidator.MaxCssClassNameLength),
					TextUtilities.SanitizeForDisplay(theme.CssFilePath ?? string.Empty)))
				.ToList()
		};
	}

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static ListThemesResult Failure(string error) {
		return new ListThemesResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}

/// <summary>
/// A single theme entry returned by the <c>list-themes</c> MCP tool.
/// </summary>
public sealed record ThemeDescriptorResult(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("cssClassName")] string CssClassName,
	[property: JsonPropertyName("cssFilePath")] string CssFilePath);
