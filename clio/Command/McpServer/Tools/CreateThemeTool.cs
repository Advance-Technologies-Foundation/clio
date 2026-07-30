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

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
	 Description("Create a custom Creatio theme on a registered environment via the native ThemeService. " +
		"Requires Creatio " + ThemeServiceRequirement.MinVersion + " or later on the target environment. " +
		"Returns { success, id, warnings?, error? } where id is the created theme's id, auto-generated when omitted. " +
		"The theme CSS is supplied inline via css-content, OR built server-side from brand colours and fonts " +
		"(primary, secondary, accent, success, error, heading-font, body-font, font-weights, version) and created " +
		"in one call — provide exactly one of css-content / primary; to supply CSS from a file, use the clio CLI " +
		"(--css-content-file) instead. " +
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
				? "theme-css-source-missing: primary is required for the brand mode — the other brand parameters " +
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
		return Execute(options, args, brandMode);
	}

	private CreateThemeResult Execute(CreateThemeOptions options, CreateThemeArgs args, bool brandMode) {
		return ExecuteResolved<CreateThemeCommand, CreateThemeResult>(options,
			resolvedCommand => {
				IReadOnlyList<string> buildWarnings = null;
				if (brandMode) {
					if (!TryBuildBrandCss(args, out string css, out buildWarnings, out string buildError)) {
						return CreateThemeResult.Failure($"theme-build-failed: {buildError}");
					}
					options.CssContent = css;
				}
				if (!resolvedCommand.TryCreateTheme(options, out string createdId, out string errorMessage)) {
					return CreateThemeResult.Failure(string.IsNullOrWhiteSpace(errorMessage)
						? "CreateTheme returned success=false."
						: SensitiveErrorTextRedactor.Redact(errorMessage));
				}
				return CreateThemeResult.Successful(createdId, buildWarnings);
			},
			CreateThemeResult.Failure);
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

	[property: JsonPropertyName("secondary")]
	[property: Description("Secondary colour (brand mode); derived from the primary when omitted.")]
	string? Secondary = null,

	[property: JsonPropertyName("accent")]
	[property: Description("Accent colour (brand mode); chosen from the primary when omitted.")]
	string? Accent = null,

	[property: JsonPropertyName("success")]
	[property: Description("Success colour (brand mode); the platform default when omitted.")]
	string? Success = null,

	[property: JsonPropertyName("error")]
	[property: Description("Error colour (brand mode); the platform default when omitted.")]
	string? Error = null,

	[property: JsonPropertyName("heading-font")]
	[property: Description("Heading font family (brand mode); Montserrat when omitted.")]
	string? HeadingFont = null,

	[property: JsonPropertyName("body-font")]
	[property: Description("Body font family (brand mode); Montserrat when omitted.")]
	string? BodyFont = null,

	[property: JsonPropertyName("font-weights")]
	[property: Description("Font weights to load (e.g. [400,500,600]) (brand mode); ignored without a custom heading/body font; defaults to 400,500,600.")]
	int[]? FontWeights = null,

	[property: JsonPropertyName("version")]
	[property: Description("Creatio version the built CSS targets (brand mode, e.g. 10.0); the target environment's version is used when omitted, falling back to the newest supported template when it cannot be determined.")]
	string? Version = null
) {
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record CreateThemeResult {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Id { get; init; }

	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	public static CreateThemeResult Successful(string id, IReadOnlyList<string> warnings = null) {
		return new CreateThemeResult {
			Success = true,
			Id = id,
			Warnings = warnings is { Count: > 0 } ? warnings : null
		};
	}

	public static CreateThemeResult Failure(string error) {
		return new CreateThemeResult {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}
