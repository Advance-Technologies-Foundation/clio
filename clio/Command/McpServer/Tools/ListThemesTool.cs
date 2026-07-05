using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.Theming;
using Clio.Common;
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

	// Known mis-spellings an LLM tends to emit instead of the kebab-case argument names. Rejected with
	// an actionable rename hint so a camelCase 'environmentName' never silently binds to nothing.
	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["environmentName"] = "environment-name",
		["environment_name"] = "environment-name"
	};

	/// <summary>Lists the custom themes available on the target environment as a structured result.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("List the custom Creatio themes available on a registered environment. " +
		"Returns { success, themes:[{ id, caption, cssClassName, cssFilePath }] }. " +
		"An empty themes array means the catalog is empty or the caller lacks the CanCustomizeBranding license. " +
		"For the theme workflow, read get-guidance theming first.")]
	public ListThemesResult ListThemes(
		[Description("Parameters: environment-name (required).")]
		[Required] ListThemesArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
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

	/// <summary>Creates a success result carrying the resolved theme catalog.</summary>
	public static ListThemesResult Successful(IReadOnlyList<ThemeDescriptor> themes) => new() {
		Success = true,
		Themes = (themes ?? Array.Empty<ThemeDescriptor>())
			.Select(theme => new ThemeDescriptorResult(theme.Id, theme.Caption, theme.CssClassName, theme.CssFilePath))
			.ToList()
	};

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static ListThemesResult Failure(string error) => new() {
		Success = false,
		Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
	};
}

/// <summary>
/// A single theme entry returned by the <c>list-themes</c> MCP tool.
/// </summary>
public sealed record ThemeDescriptorResult(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("cssClassName")] string CssClassName,
	[property: JsonPropertyName("cssFilePath")] string CssFilePath);
