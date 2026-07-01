using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.Theming;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that builds a Creatio <c>theme.css</c> from brand colours and fonts using the deterministic
/// palette engine and the bundled, version-matched template. The template version comes from <c>version</c>,
/// or from <c>environmentName</c> (whose Creatio version it reads), or defaults to the newest supported
/// version. Delegates to <see cref="BuildThemeCommand"/> so this tool and the CLI verb resolve the version
/// and map inputs identically.
/// </summary>
[McpServerToolType]
[FeatureToggle("theming")]
public sealed class BuildThemeTool(BuildThemeCommand command) {

	internal const string ToolName = "build-theme";

	/// <summary>
	/// Builds the <c>theme.css</c> string from the supplied brand inputs and the bundled template.
	/// </summary>
	/// <returns>A structured result carrying the built CSS, or a failure message.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Build the artifacts of a Creatio theme from brand colours and fonts. " +
		"Returns { success, css, descriptor, warnings?, error? } — the theme.css and its theme.json descriptor. " +
		"Pure compute — pipe css into create-theme-by-environment's cssContent. " +
		"For the theme workflow, read get-guidance theming first.")]
	public BuildThemeResult BuildTheme(
		[Description("Brand primary colour (#rrggbb, #rgb, rgb(), hsl(), or a named colour)")] [Required] string primary,
		[Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100)")] [Required] string cssClassName,
		[Description("Human-readable theme caption for theme.json; derived from cssClassName when omitted")] string caption = null,
		[Description("Theme id for theme.json; an auto-generated UUID when omitted")] string id = null,
		[Description("Secondary colour; derived from the primary when omitted")] string secondary = null,
		[Description("Accent colour; chosen from the primary when omitted")] string accent = null,
		[Description("Success colour; the platform default when omitted")] string success = null,
		[Description("Error colour; the platform default when omitted")] string error = null,
		[Description("Heading font family; Montserrat when omitted")] string headingFont = null,
		[Description("Body font family; Montserrat when omitted")] string bodyFont = null,
		[Description("Font weights to load (e.g. [400,500,600]); ignored without a custom heading/body font; defaults to 400,500,600")] int[] fontWeights = null,
		[Description("Creatio version the theme targets (e.g. 10.0); the newest supported version is used when omitted; mutually exclusive with environmentName")] string version = null,
		[Description("Registered environment whose Creatio version the theme targets; mutually exclusive with version")] string environmentName = null) {
		if (string.IsNullOrWhiteSpace(primary)) {
			return BuildThemeResult.Failure("primary is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(cssClassName)) {
			return BuildThemeResult.Failure("cssClassName is required and cannot be empty.");
		}
		BuildThemeOptions options = new() {
			Primary = primary,
			Secondary = secondary,
			Accent = accent,
			Success = success,
			Error = error,
			CssClassName = cssClassName,
			Caption = caption,
			Id = id,
			HeadingFont = headingFont,
			BodyFont = bodyFont,
			FontWeights = fontWeights,
			Version = version,
			EnvironmentName = environmentName
		};
		if (!command.TryBuildTheme(options, out string css, out string descriptor, out IReadOnlyList<string> warnings, out string buildError)) {
			return BuildThemeResult.Failure(buildError);
		}
		return BuildThemeResult.Successful(css, descriptor, warnings);
	}
}

/// <summary>
/// Structured result of the <c>build-theme</c> MCP tool.
/// </summary>
public sealed record BuildThemeResult {
	/// <summary>Whether the theme CSS was built.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The built <c>theme.css</c> string; omitted on failure.</summary>
	[JsonPropertyName("css")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Css { get; init; }

	/// <summary>The built <c>theme.json</c> descriptor; omitted on failure.</summary>
	[JsonPropertyName("descriptor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Descriptor { get; init; }

	/// <summary>Non-fatal advisories; omitted when there are none.</summary>
	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a success result carrying the built theme artifacts and any non-fatal advisories.</summary>
	public static BuildThemeResult Successful(string css, string descriptor, IReadOnlyList<string> warnings) {
		return new() {
			Success = true,
			Css = css,
			Descriptor = descriptor,
			Warnings = warnings is { Count: > 0 } ? warnings : null
		};
	}

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static BuildThemeResult Failure(string error) {
		return new() {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}
