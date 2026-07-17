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
/// MCP tool that applies a Creatio theme to the current (authenticated) user's profile on a target
/// environment via the DataService, or clears it (<c>reset</c>) to fall back to the environment default.
/// It affects only the authenticated account, so it is a write that needs no extra confirmation gate
/// (unlike the global <c>DefaultTheme</c> system setting).
/// </summary>
public class SetUserThemeTool(
	SetUserThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<SetUserThemeOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-user-theme";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal);

	/// <summary>Applies (or resets) the current user's profile theme and returns a structured result.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Apply a Creatio theme to the current (authenticated) user's profile on a registered environment, " +
		"or clear it with reset. Affects only the calling account (not other users) — unlike the global DefaultTheme. " +
		"Requires Creatio " + ThemeServiceRequirement.MinVersion + " or later on the target environment. " +
		"Returns { success, caption, css-class-name, id, error? }; the user must refresh the page to see the change. " +
		"For the theme workflow, read get-guidance theming first.")]
	public SetUserThemeResult SetUserTheme(
		[Description("Parameters: environment-name (required), theme (id, css-class-name, or caption from list-themes; " +
			"required unless reset is true), reset (optional bool — clear the theme; mutually exclusive with theme).")]
		[Required] SetUserThemeArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, theme, reset.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return SetUserThemeResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return SetUserThemeResult.Failure("environment-name is required and cannot be empty.");
		}
		bool hasTheme = !string.IsNullOrWhiteSpace(args.Theme);
		bool reset = args.Reset ?? false;
		// Validate the theme/reset combination up front so an obvious argument mistake fails immediately,
		// before the environment resolution and Creatio version-check round-trip (the command keeps its own
		// equivalent checks as defense-in-depth for the CLI path).
		if (reset && hasTheme) {
			return SetUserThemeResult.Failure("Specify either a theme to apply or reset=true, not both.");
		}
		if (!reset && !hasTheme) {
			return SetUserThemeResult.Failure(
				"A theme is required unless reset=true. Provide theme (id, css-class-name, or caption), or set reset=true.");
		}
		SetUserThemeOptions options = new() {
			Environment = args.EnvironmentName,
			Theme = args.Theme,
			Reset = reset
		};
		return Execute(options);
	}

	private SetUserThemeResult Execute(SetUserThemeOptions options) {
		return ExecuteResolved<SetUserThemeCommand, SetUserThemeResult>(options,
			resolvedCommand => {
				if (!resolvedCommand.TrySetUserTheme(options, out AppliedUserTheme applied, out string errorMessage)) {
					// errorMessage can carry a server-supplied DataService/ThemeService error body or a
					// transport message (URI/credentials/path), so redact it the same as CreateThemeTool /
					// ListThemesTool before it crosses into the MCP client transcript.
					return SetUserThemeResult.Failure(string.IsNullOrWhiteSpace(errorMessage)
						? "SetUserTheme returned success=false."
						: SensitiveErrorTextRedactor.Redact(errorMessage));
				}
				return SetUserThemeResult.Successful(applied);
			},
			SetUserThemeResult.Failure);
	}
}

/// <summary>
/// MCP arguments for the <c>set-user-theme</c> tool.
/// </summary>
public sealed record SetUserThemeArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("theme")]
	[property: Description("Theme to apply: id, css-class-name, or caption (case-insensitive), as reported by list-themes. Required unless reset is true.")]
	string? Theme = null,

	[property: JsonPropertyName("reset")]
	[property: Description("When true, clears the user's theme so the environment default (DefaultTheme) applies. Mutually exclusive with theme.")]
	bool? Reset = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured result of the <c>set-user-theme</c> MCP tool.
/// </summary>
public sealed record SetUserThemeResult {
	/// <summary>Whether the theme was applied (or cleared) and verified.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The applied theme's caption; empty on reset, omitted on failure.</summary>
	[JsonPropertyName("caption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Caption { get; init; }

	/// <summary>The applied theme's css class name; empty on reset, omitted on failure.</summary>
	[JsonPropertyName("css-class-name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string CssClassName { get; init; }

	/// <summary>The applied theme's id (the value written to the profile); empty on reset, omitted on failure.</summary>
	[JsonPropertyName("id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Id { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result carrying the applied theme (empty fields on reset).</summary>
	public static SetUserThemeResult Successful(AppliedUserTheme applied) {
		return new SetUserThemeResult {
			Success = true,
			Caption = applied?.Caption ?? string.Empty,
			CssClassName = applied?.CssClassName ?? string.Empty,
			Id = applied?.Id ?? string.Empty
		};
	}

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static SetUserThemeResult Failure(string error) {
		return new SetUserThemeResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}
