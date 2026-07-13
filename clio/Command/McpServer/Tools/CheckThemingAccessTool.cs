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
/// Read-only MCP tool that reports whether the caller may manage custom themes on a target environment,
/// probing the <c>CanManageThemes</c> system operation and the <c>CanCustomizeBranding</c> license and
/// returning both verdicts as a structured result.
/// </summary>
public sealed class CheckThemingAccessTool(
	CheckThemingAccessCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<CheckThemingAccessOptions>(command, logger, commandResolver) {

	internal const string ToolName = "check-theming-access";

	/// <summary>Probes the <c>CanManageThemes</c> operation right and the <c>CanCustomizeBranding</c> license on the target environment and returns both verdicts.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Check whether the caller can manage custom themes on a registered environment. " +
		"Requires Creatio " + ThemeServiceRequirement.MinVersion + " or later on the target environment. " +
		"Probes the CanManageThemes system operation and the CanCustomizeBranding license. " +
		"Returns { success, canManageThemes, canCustomizeBranding, error? }. " +
		"Advisory only: run it before the no-code / server theme flow (create/update/delete-theme), " +
		"but create-theme is the authoritative access test. " +
		"For the theme workflow, read get-guidance theming first.")]
	public ThemingAccessResult CheckThemingAccess(
		[Description("Parameters: environment-name (required).")]
		[Required] CheckThemingAccessArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".",
			"Valid: environment-name.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ThemingAccessResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return ThemingAccessResult.Failure("environment-name is required and cannot be empty.");
		}
		CheckThemingAccessOptions options = new() {
			Environment = args.EnvironmentName
		};
		return Execute(options);
	}

	private ThemingAccessResult Execute(CheckThemingAccessOptions options) {
		return ExecuteResolved<CheckThemingAccessCommand, ThemingAccessResult>(options,
			resolvedCommand => {
				ThemingAccess access = resolvedCommand.GetThemingAccess();
				return ThemingAccessResult.Successful(access.CanManageThemes, access.CanCustomizeBranding);
			},
			ThemingAccessResult.Failure);
	}
}

/// <summary>
/// MCP arguments for the <c>check-theming-access</c> tool.
/// </summary>
public sealed record CheckThemingAccessArgs(
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
/// Structured result of the <c>check-theming-access</c> MCP tool.
/// </summary>
public sealed record ThemingAccessResult {
	/// <summary>Whether the access check completed successfully.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>Whether the caller holds the <c>CanManageThemes</c> system operation right. Omitted on failure.</summary>
	[JsonPropertyName("canManageThemes")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? CanManageThemes { get; init; }

	/// <summary>Whether the caller holds the <c>CanCustomizeBranding</c> license. Omitted on failure.</summary>
	[JsonPropertyName("canCustomizeBranding")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? CanCustomizeBranding { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result from the two independent checks.</summary>
	public static ThemingAccessResult Successful(bool canManageThemes, bool canCustomizeBranding) {
		return new ThemingAccessResult {
			Success = true,
			CanManageThemes = canManageThemes,
			CanCustomizeBranding = canCustomizeBranding
		};
	}

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static ThemingAccessResult Failure(string error) {
		return new ThemingAccessResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}
