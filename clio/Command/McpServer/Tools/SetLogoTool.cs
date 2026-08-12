using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.Branding;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that applies the product logos and the browser-tab favicon from local image files — one Binary
/// sys-setting per slot — and binds the applied values into a package as Creatio data bindings. Changes the
/// look for all users, so it is annotated <c>Destructive=true</c>; re-running with the same files converges
/// to the same state (<c>Idempotent=true</c>).
/// </summary>
public class SetLogoTool(
	SetLogoCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<SetLogoOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-logo";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["loginLogo"] = "login-logo",
			["login_logo"] = "login-logo",
			["menuLogo"] = "menu-logo",
			["menu_logo"] = "menu-logo",
			["configurationLogo"] = "configuration-logo",
			["configuration_logo"] = "configuration-logo",
			["darkLogo"] = "dark-logo",
			["dark_logo"] = "dark-logo",
			["faviconImage"] = "favicon",
			["favicon_image"] = "favicon",
			["packageName"] = "package",
			["package_name"] = "package",
			["package-name"] = "package"
		};

	/// <summary>Applies the requested images, binds them into the package, and returns a structured result.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Apply the product logos and the browser-tab favicon on a registered environment from local " +
		"image files and bind them into a package as data bindings. Pass at " +
		"least one of: logo (one file for every slot at once), login-logo (login page), menu-logo (main menu), " +
		"configuration-logo (configuration page), dark-logo (the Freedom UI top panel — a dark surface, pass " +
		"the light logo variant), favicon (the browser tab). A slot argument overrides logo for that slot, so " +
		"one call can brand every slot and still give the dark panel its own file. The stock splash logo is " +
		"suppressed automatically, and a favicon also turns on its UseFaviconFromSysSettings gate. " +
		"When package is " +
		"omitted, the environment's CurrentPackageId system setting decides where the bindings land. The logos " +
		"change for all users and cannot be automatically reverted — warn the user first. A refused image " +
		"returns success: false even though the accepted ones stayed applied and bound, so read applied " +
		"and bound before retrying. Read get-guidance branding first.")]
	public SetLogoToolResult SetLogo(
		[Description("Parameters: environment-name (required); at least one of logo (all slots), login-logo, menu-logo, configuration-logo, dark-logo, favicon (local image paths); package (optional, the environment's CurrentPackageId when omitted).")]
		[Required] SetLogoArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, logo, login-logo, menu-logo, configuration-logo, dark-logo, favicon, package.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return SetLogoToolResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return SetLogoToolResult.Failure("environment-name is required and cannot be empty.");
		}
		bool anyImage = !string.IsNullOrWhiteSpace(args.Logo)
			|| !string.IsNullOrWhiteSpace(args.LoginLogo)
			|| !string.IsNullOrWhiteSpace(args.MenuLogo)
			|| !string.IsNullOrWhiteSpace(args.ConfigurationLogo)
			|| !string.IsNullOrWhiteSpace(args.DarkLogo)
			|| !string.IsNullOrWhiteSpace(args.Favicon);
		if (!anyImage) {
			return SetLogoToolResult.Failure(SetLogoCommand.NoImageError);
		}
		SetLogoOptions options = new() {
			Environment = args.EnvironmentName,
			Logo = args.Logo,
			LoginLogo = args.LoginLogo,
			MenuLogo = args.MenuLogo,
			ConfigurationLogo = args.ConfigurationLogo,
			DarkLogo = args.DarkLogo,
			Favicon = args.Favicon,
			PackageName = args.Package
		};
		return Execute(options);
	}

	private SetLogoToolResult Execute(SetLogoOptions options) {
		return ExecuteResolved<SetLogoCommand, SetLogoToolResult>(options,
			resolvedCommand => {
				SetLogoResult result = resolvedCommand.ApplyLogos(options);
				if (!result.Success) {
					return SetLogoToolResult.Failure(string.IsNullOrWhiteSpace(result.Error)
							? "ApplyLogos returned success=false."
							: SensitiveErrorTextRedactor.Redact(result.Error),
						result.Applied, result.Warnings, result.Package, result.Bound);
				}
				return SetLogoToolResult.Successful(result);
			},
			error => SetLogoToolResult.Failure(error));
	}
}

/// <summary>
/// MCP arguments for the <c>set-logo</c> tool.
/// </summary>
public sealed record SetLogoArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string? EnvironmentName = null,

	[property: JsonPropertyName("logo")]
	[property: Description("Local image file applied to every logo slot at once. A slot argument (login-logo, menu-logo, configuration-logo, dark-logo) overrides it for that slot.")]
	string? Logo = null,

	[property: JsonPropertyName("login-logo")]
	[property: Description("Local image file for the logo on the login page (LogoImage).")]
	string? LoginLogo = null,

	[property: JsonPropertyName("menu-logo")]
	[property: Description("Local image file for the main menu logo (MenuLogoImage).")]
	string? MenuLogo = null,

	[property: JsonPropertyName("configuration-logo")]
	[property: Description("Local image file for the configuration page logo (ConfigurationPageLogoImage).")]
	string? ConfigurationLogo = null,

	[property: JsonPropertyName("dark-logo")]
	[property: Description("Local image file for the logo on the dark Freedom UI top panel (CrtAppToolbarLogo). Pass the light variant of the logo here — a logo drawn for a white background is hard to read on the dark panel.")]
	string? DarkLogo = null,

	[property: JsonPropertyName("favicon")]
	[property: Description("Local image file for the browser-tab icon (FaviconImage). Pass a square icon — clio uploads the file as it is, without resizing or converting it. ICO, PNG and SVG are the safest formats. Never taken from logo.")]
	string? Favicon = null,

	[property: JsonPropertyName("package")]
	[property: Description("Package that receives the branding data bindings. When omitted, the package from the environment's CurrentPackageId system setting is used.")]
	string? Package = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured result of the <c>set-logo</c> MCP tool.
/// </summary>
public sealed record SetLogoToolResult {
	/// <summary>Whether every requested logo was applied and bound.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The logo slots that were applied (also populated on a partial failure); omitted when empty.</summary>
	[JsonPropertyName("applied")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Applied { get; init; }

	/// <summary>
	/// The setting codes the package delivery confirmed it bound. Omitted when nothing was bound, which can
	/// happen even with <c>applied</c> present — a slot can apply and still be refused by the delivery.
	/// </summary>
	[JsonPropertyName("bound")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Bound { get; init; }

	/// <summary>
	/// The package the logo data was bound into (also populated on a partial failure, where the applied slots
	/// were bound into it); omitted when the run never got as far as resolving one.
	/// </summary>
	[JsonPropertyName("package")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Package { get; init; }

	/// <summary>
	/// Every non-fatal problem: an apply-side caveat and each gap between what was applied and what the package
	/// will deliver. Relay them to the user — a run with warnings still succeeded, but delivers less than it
	/// looks like it did. Omitted when empty.
	/// </summary>
	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result from the command outcome.</summary>
	public static SetLogoToolResult Successful(SetLogoResult result) {
		return new SetLogoToolResult {
			Success = true,
			Applied = result.Applied.Count > 0 ? result.Applied : null,
			Bound = result.Bound.Count > 0 ? result.Bound : null,
			Package = result.Package,
			Warnings = result.Warnings.Count > 0 ? SensitiveErrorTextRedactor.RedactAll(result.Warnings) : null
		};
	}

	/// <summary>
	/// Creates a failure result carrying the diagnostic message, any slots already applied, any warnings
	/// raised before the failure — an apply-side caveat must not be lost just because binding failed after it —
	/// and the package the applied slots were bound into, when one was resolved.
	/// </summary>
	public static SetLogoToolResult Failure(string error, IReadOnlyList<string> applied = null,
		IReadOnlyList<string> warnings = null, string package = null, IReadOnlyList<string> bound = null) {
		return new SetLogoToolResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error,
			Applied = applied is { Count: > 0 } ? applied : null,
			Bound = bound is { Count: > 0 } ? bound : null,
			Warnings = warnings is { Count: > 0 } ? SensitiveErrorTextRedactor.RedactAll(warnings) : null,
			Package = package
		};
	}

}
