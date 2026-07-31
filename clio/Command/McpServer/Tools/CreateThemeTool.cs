using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.Theming;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that creates a custom Creatio theme on a target environment via the native <c>ThemeService</c>,
/// returning a structured result with the theme id. The CSS is either supplied inline or, in the brand mode,
/// built server-side from the brand colours and fonts in the same call.
/// </summary>
public class CreateThemeTool(
	CreateThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<CreateThemeOptions>(command, logger, commandResolver) {

	internal const string ToolName = "create-theme";

	private readonly IToolCommandResolver _commandResolver = commandResolver;

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["cssContent"] = "css-content",
			["css_content"] = "css-content",
			["cssClassName"] = "css-class-name",
			["css_class_name"] = "css-class-name",
			["packageName"] = "package-name",
			["package_name"] = "package-name",
			["headingFont"] = "heading-font",
			["heading_font"] = "heading-font",
			["bodyFont"] = "body-font",
			["body_font"] = "body-font",
			["fontWeights"] = "font-weights",
			["font_weights"] = "font-weights"
		};

	/// <summary>Creates the theme on the target environment and returns a structured result carrying the effective theme id.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
	 Description("Create a custom Creatio theme on a registered environment via the native ThemeService. " +
		"Requires Creatio " + ThemeServiceRequirement.MinVersion + " or later on the target environment. " +
		"Returns { success, id, warnings?, error? } where id is the created theme's id, auto-generated when omitted. " +
		"The theme CSS is supplied inline via css-content, OR built server-side from brand colours and fonts " +
		"(primary, secondary, accent, success, error, heading-font, body-font, font-weights, version) and created " +
		"in one call — provide exactly one of css-content / primary. " +
		"For the theme workflow, read get-guidance theming first.")]
	public CreateThemeResult CreateTheme(
		[Description("Parameters: environment-name (required), css-content (inline mode) or primary (brand mode) — exactly one of the two, " +
			"css-class-name, caption, id, package-name, secondary, accent, success, error, heading-font, body-font, font-weights, version (all optional).")]
		[Required] CreateThemeArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, css-content, css-class-name, caption, id, package-name, " +
			"primary, secondary, accent, success, error, heading-font, body-font, font-weights, version.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return CreateThemeResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CreateThemeResult.Failure("environment-name is required and cannot be empty.");
		}
		bool hasCssContent = !string.IsNullOrWhiteSpace(args.CssContent);
		bool brandMode = !string.IsNullOrWhiteSpace(args.Primary);
		if (hasCssContent && HasAnyBrandParameter(args)) {
			return CreateThemeResult.Failure(
				"theme-css-source-conflict: css-content and the brand parameters (primary, secondary, accent, " +
				"success, error, heading-font, body-font, font-weights, version) are mutually exclusive. " +
				"Provide inline CSS or brand colours, not both.");
		}
		if (!hasCssContent && !brandMode) {
			return CreateThemeResult.Failure(HasAnyBrandParameter(args)
				? "theme-brand-primary-missing: primary is required for the brand mode — the other brand parameters " +
					"only refine the palette derived from it. Pass primary, or provide css-content instead."
				: "theme-css-source-missing: provide either css-content (inline CSS) or primary (brand colours; " +
					"clio builds the theme CSS server-side and creates the theme in one call).");
		}
		CreateThemeOptions options = new() {
			Environment = args.EnvironmentName,
			Caption = args.Caption,
			CssClassName = args.CssClassName,
			CssContent = args.CssContent,
			Id = args.Id,
			PackageName = args.PackageName
		};
		return Execute(options, brandMode ? args : null);
	}

	private CreateThemeResult Execute(CreateThemeOptions options, CreateThemeArgs brandArgs) {
		IReadOnlyList<string> buildWarnings = null;
		return ExecuteResolved<CreateThemeCommand, CreateThemeResult>(options,
			resolvedCommand => {
				if (brandArgs is not null) {
					bool built = TryBuildBrandCss(brandArgs, out string css, out IReadOnlyList<string> rawWarnings, out string buildError);
					buildWarnings = RedactWarnings(rawWarnings);
					if (!built) {
						return CreateThemeResult.Failure(
							$"theme-build-failed: {SensitiveErrorTextRedactor.Redact(buildError)}", buildWarnings);
					}
					options.CssContent = css;
				}
				if (!resolvedCommand.TryCreateTheme(options, out string createdId, out string errorMessage)) {
					return CreateThemeResult.Failure(
						string.IsNullOrWhiteSpace(errorMessage)
							? "CreateTheme returned success=false."
							: SensitiveErrorTextRedactor.Redact(errorMessage),
						buildWarnings);
				}
				return CreateThemeResult.Successful(createdId, buildWarnings);
			},
			error => CreateThemeResult.Failure(error, buildWarnings));
	}

	private static bool HasAnyBrandParameter(CreateThemeArgs args) {
		return !string.IsNullOrWhiteSpace(args.Primary)
			|| !string.IsNullOrWhiteSpace(args.Secondary)
			|| !string.IsNullOrWhiteSpace(args.Accent)
			|| !string.IsNullOrWhiteSpace(args.Success)
			|| !string.IsNullOrWhiteSpace(args.Error)
			|| !string.IsNullOrWhiteSpace(args.HeadingFont)
			|| !string.IsNullOrWhiteSpace(args.BodyFont)
			|| args.FontWeights is { Length: > 0 }
			|| !string.IsNullOrWhiteSpace(args.Version);
	}

	private static IReadOnlyList<string> RedactWarnings(IReadOnlyList<string> warnings) {
		return warnings is { Count: > 0 }
			? warnings.Select(SensitiveErrorTextRedactor.Redact).ToList()
			: warnings;
	}

	private bool TryBuildBrandCss(CreateThemeArgs args, out string css,
		out IReadOnlyList<string> warnings, out string error) {
		EnvironmentOptions environmentOptions = new() { Environment = args.EnvironmentName };
		EnvironmentSettings resolvedSettings = string.IsNullOrWhiteSpace(args.Version)
			? _commandResolver.Resolve<EnvironmentSettings>(environmentOptions)
			: null;
		BuildThemeOptions buildOptions = new() {
			Primary = args.Primary,
			Secondary = args.Secondary,
			Accent = args.Accent,
			Success = args.Success,
			Error = args.Error,
			CssClassName = args.CssClassName,
			Caption = args.Caption,
			Id = args.Id,
			HeadingFont = args.HeadingFont,
			BodyFont = args.BodyFont,
			FontWeights = args.FontWeights,
			Version = args.Version,
			EnvironmentName = null
		};
		BuildThemeCommand buildCommand = _commandResolver.Resolve<BuildThemeCommand>(environmentOptions);
		return buildCommand.TryBuildTheme(buildOptions, resolvedSettings, out css, out _, out warnings, out error);
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
	[property: Description("Inline theme CSS content (max 1 MiB); must not be empty when supplied. Mutually exclusive with the brand parameters (primary, secondary, accent, success, error, heading-font, body-font, font-weights, version): provide inline CSS or brand colours, not both.")]
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
	string? PackageName = null,

	[property: JsonPropertyName("primary")]
	[property: Description("Brand primary colour (#rrggbb, #rgb, rgb(), hsl(), or a named colour); enables the brand mode — clio builds the theme CSS server-side and creates it in one call, so the CSS never enters the agent context. Mutually exclusive with css-content.")]
	string? Primary = null,

	[property: JsonPropertyName("version")]
	[property: Description("Creatio version the built CSS targets (brand mode, e.g. 10.0); the target environment's version is used when omitted, falling back to the newest supported template when it cannot be determined. Selects the build template, so it counts as a brand parameter and cannot be combined with css-content.")]
	string? Version = null
) : ThemeBrandArgs {
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

	/// <summary>Non-fatal advisories raised while building the CSS in the brand mode; omitted when there are none.</summary>
	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result carrying the effective theme id and any non-fatal build advisories.</summary>
	public static CreateThemeResult Successful(string id, IReadOnlyList<string> warnings = null) {
		return new CreateThemeResult {
			Success = true,
			Id = id,
			Warnings = warnings is { Count: > 0 } ? warnings : null
		};
	}

	/// <summary>
	/// Creates a failure result carrying the diagnostic message and any non-fatal advisories already raised —
	/// a brand-mode build can succeed (emitting advisories) and the subsequent create still fail, and the
	/// advisories are the caller's only signal about the CSS that was built.
	/// </summary>
	public static CreateThemeResult Failure(string error, IReadOnlyList<string> warnings = null) {
		return new CreateThemeResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error,
			Warnings = warnings is { Count: > 0 } ? warnings : null
		};
	}
}
