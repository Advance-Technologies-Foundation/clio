using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Read-only MCP tool that reports whether the caller may manage custom themes on a target environment by
/// composing two native Creatio checks: the <c>CanManageThemes</c> system operation
/// (<see cref="ICreatioRightsClient"/>) and the <c>CanCustomizeBranding</c> license
/// (<see cref="ICreatioLicenseClient"/>). Both clients are resolved for the per-call target environment.
/// </summary>
public sealed class CheckThemingAccessTool(
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<EnvironmentOptions>(null, logger, commandResolver) {

	private readonly IToolCommandResolver _commandResolver = commandResolver;

	internal const string ToolName = "check-theming-access";

	/// <summary>The system operation that grants management of custom themes on an environment.</summary>
	private const string CanManageThemesOperation = "CanManageThemes";

	/// <summary>The license operation that grants branding customization (custom themes).</summary>
	private const string CanCustomizeBrandingLicense = "CanCustomizeBranding";

	/// <summary>Probes the <c>CanManageThemes</c> operation right and the <c>CanCustomizeBranding</c> license on the target environment and returns both verdicts.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Check whether the caller can manage custom themes on a registered environment. " +
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
		EnvironmentOptions options = new() {
			Environment = args.EnvironmentName
		};
		return Execute(options);
	}

	private ThemingAccessResult Execute(EnvironmentOptions options) {
		return ExecuteWithCleanLog(() => {
			try {
				ICreatioRightsClient rightsClient = _commandResolver.Resolve<ICreatioRightsClient>(options);
				ICreatioLicenseClient licenseClient = _commandResolver.Resolve<ICreatioLicenseClient>(options);
				CreatioRequestOptions requestOptions = new();
				bool canManageThemes = rightsClient.GetCanExecuteOperation(CanManageThemesOperation, requestOptions);
				IReadOnlyDictionary<string, bool> licenseStatuses = licenseClient.GetLicenseOperationStatuses(
					new[] { CanCustomizeBrandingLicense }, requestOptions);
				bool canCustomizeBranding = licenseStatuses.TryGetValue(CanCustomizeBrandingLicense, out bool granted)
					&& granted;
				return ThemingAccessResult.Successful(canManageThemes, canCustomizeBranding);
			}
			catch (Exception ex) {
				return ThemingAccessResult.Failure(ex.Message);
			}
		});
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
