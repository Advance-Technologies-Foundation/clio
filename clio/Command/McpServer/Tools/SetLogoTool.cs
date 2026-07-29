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
/// MCP tool that applies the product logos from local image files — one Binary sys-setting per slot —
/// and binds the applied values into a package as Creatio data bindings so the logos ship with the
/// package. Changes the look for all users, so it is annotated <c>Destructive=true</c>; re-running with
/// the same files converges to the same state (<c>Idempotent=true</c>).
/// </summary>
public class SetLogoTool(
	SetLogoCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<SetLogoOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-logo";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["menuLogo"] = "menu-logo",
			["configurationLogo"] = "configuration-logo",
			["darkLogo"] = "dark-logo",
			["packageName"] = "package"
		};

	/// <summary>Applies the requested logo slots, binds them into the package, and returns a structured result.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false),
	 Description("Apply the product logos on a registered environment from local image files and bind them " +
		"into a package as data bindings so they ship with the package. Pass at least one slot: logo (login " +
		"page), menu-logo (main menu), configuration-logo (configuration page), " +
		"dark-logo (the Freedom UI top panel — a dark surface, pass the white/light logo variant). The " +
		"stock splash logo is suppressed automatically. package defaults to Custom. The logos change for " +
		"all users and cannot be automatically reverted — warn the user first. " +
		"Read get-guidance branding first.")]
	public SetLogoToolResult SetLogo(
		[Description("Parameters: environment-name (required); at least one of logo, menu-logo, configuration-logo, dark-logo (local image paths); package (optional, default Custom).")]
		[Required] SetLogoArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, logo, menu-logo, configuration-logo, dark-logo, package.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return SetLogoToolResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return SetLogoToolResult.Failure("environment-name is required and cannot be empty.");
		}
		bool anySlot = !string.IsNullOrWhiteSpace(args.Logo)
			|| !string.IsNullOrWhiteSpace(args.MenuLogo)
			|| !string.IsNullOrWhiteSpace(args.ConfigurationLogo)
			|| !string.IsNullOrWhiteSpace(args.DarkLogo);
		if (!anySlot) {
			return SetLogoToolResult.Failure(SetLogoCommand.NoLogoError);
		}
		SetLogoOptions options = new() {
			Environment = args.EnvironmentName,
			Logo = args.Logo,
			MenuLogo = args.MenuLogo,
			ConfigurationLogo = args.ConfigurationLogo,
			DarkLogo = args.DarkLogo,
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
						result.Applied);
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
	[property: Description("Local image file for the main logo, shown on the login page (LogoImage).")]
	string? Logo = null,

	[property: JsonPropertyName("menu-logo")]
	[property: Description("Local image file for the main menu logo (MenuLogoImage).")]
	string? MenuLogo = null,

	[property: JsonPropertyName("configuration-logo")]
	[property: Description("Local image file for the configuration page logo (ConfigurationPageLogoImage).")]
	string? ConfigurationLogo = null,

	[property: JsonPropertyName("dark-logo")]
	[property: Description("Local image file for the logo shown on a dark background — the Freedom UI top panel (CrtAppToolbarLogo). Pass the white/light variant of the logo.")]
	string? DarkLogo = null,

	[property: JsonPropertyName("package")]
	[property: Description("Package that receives the logo data bindings. Defaults to Custom when omitted.")]
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

	/// <summary>The package the logo data was bound into; omitted when the run failed before binding.</summary>
	[JsonPropertyName("package")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Package { get; init; }

	/// <summary>Delivery gaps reported by the binding reconcile; relay them to the user. Omitted when empty.</summary>
	[JsonPropertyName("skipped")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Skipped { get; init; }

	/// <summary>A non-fatal problem the caller should surface; omitted when absent.</summary>
	[JsonPropertyName("warning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Warning { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result from the command outcome.</summary>
	public static SetLogoToolResult Successful(SetLogoResult result) {
		return new SetLogoToolResult {
			Success = true,
			Applied = result.Applied.Count > 0 ? result.Applied : null,
			Package = result.Package,
			Skipped = result.Skipped.Count > 0 ? result.Skipped : null,
			Warning = result.Warning
		};
	}

	/// <summary>Creates a failure result carrying the diagnostic message and any slots already applied.</summary>
	public static SetLogoToolResult Failure(string error, IReadOnlyList<string> applied = null) {
		return new SetLogoToolResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error,
			Applied = applied is { Count: > 0 } ? applied : null
		};
	}
}
