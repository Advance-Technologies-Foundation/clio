using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
[McpServerToolType]
public sealed class CheckThemingAccessTool(IToolCommandResolver commandResolver) {

	internal const string CheckThemingAccessByEnvironmentName = "check-theming-access-by-environment";
	internal const string CheckThemingAccessByCredentialsToolName = "check-theming-access-by-credentials";

	/// <summary>The system operation that grants management of custom themes on an environment.</summary>
	private const string CanManageThemesOperation = "CanManageThemes";

	/// <summary>The license operation that grants branding customization (custom themes).</summary>
	private const string CanCustomizeBrandingLicense = "CanCustomizeBranding";

	[McpServerTool(Name = CheckThemingAccessByEnvironmentName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Check whether the caller can manage custom themes on a registered environment. " +
		"Probes the CanManageThemes system operation and the CanCustomizeBranding license. " +
		"Returns { success, canManageThemes, canCustomizeBranding }. " +
		"Advisory only: run it before the no-code / server theme flow (create/update/delete-theme-by-environment), " +
		"but create-theme-by-environment is the authoritative access test. " +
		"For the theme workflow, read get-guidance theming first.")]
	public ThemingAccessResult CheckThemingAccessByName(
		[Description("Target Environment name")] [Required] string environmentName
	) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return ThemingAccessResult.Failure("environment-name is required and cannot be empty.");
		}
		EnvironmentOptions options = new() {
			Environment = environmentName
		};
		return Execute(options);
	}

	[McpServerTool(Name = CheckThemingAccessByCredentialsToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Check whether the caller can manage custom themes using explicit credentials. " +
		"Probes the CanManageThemes system operation and the CanCustomizeBranding license. " +
		"Returns { success, canManageThemes, canCustomizeBranding }. Advisory only: create-theme-by-environment " +
		"is the authoritative access test. For the theme workflow, read get-guidance theming first.")]
	public ThemingAccessResult CheckThemingAccessByCredentials(
		[Description("Creatio instance url")] [Required] string url,
		[Description("Creatio instance Username")] [Required] string userName,
		[Description("Creatio instance Password")] [Required] string password,
		[DefaultValue(false)][Description("Specifies if creatio runtime is a NET8 or NET472, default: false")] bool isNetCore = false
	) {
		if (string.IsNullOrWhiteSpace(url)) {
			return ThemingAccessResult.Failure("url is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(userName)) {
			return ThemingAccessResult.Failure("userName is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(password)) {
			return ThemingAccessResult.Failure("password is required and cannot be empty.");
		}
		EnvironmentOptions options = new() {
			Login = userName,
			Password = password,
			Uri = url,
			IsNetCore = isNetCore
		};
		return Execute(options);
	}

	private ThemingAccessResult Execute(EnvironmentOptions options) {
		try {
			ICreatioRightsClient rightsClient = commandResolver.Resolve<ICreatioRightsClient>(options);
			ICreatioLicenseClient licenseClient = commandResolver.Resolve<ICreatioLicenseClient>(options);
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
	}
}

/// <summary>
/// Structured result of the <c>check-theming-access</c> MCP tools.
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
	public static ThemingAccessResult Successful(bool canManageThemes, bool canCustomizeBranding) => new() {
		Success = true,
		CanManageThemes = canManageThemes,
		CanCustomizeBranding = canCustomizeBranding
	};

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static ThemingAccessResult Failure(string error) => new() {
		Success = false,
		Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
	};
}
