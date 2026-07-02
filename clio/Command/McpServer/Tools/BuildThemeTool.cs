using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.Theming;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that builds a Creatio <c>theme.css</c> from brand colours and fonts using the deterministic
/// palette engine and the bundled, version-matched template. The template version comes from <c>version</c>,
/// or from <c>environmentName</c> (whose Creatio version it reads), or defaults to the newest supported
/// version. It has two output modes: <b>compute</b> (neither <c>workspaceDirectory</c> nor <c>packageName</c>)
/// returns the CSS and descriptor strings, and <b>workspace-write</b> (<c>workspaceDirectory</c> +
/// <c>packageName</c>) writes <c>theme.css</c> + <c>theme.json</c> into
/// <c>&lt;workspaceDirectory&gt;/packages/&lt;packageName&gt;/Files/themes/&lt;cssClassName&gt;/</c> and returns the
/// written path without the CSS payload — keeping the large CSS out of the agent context. Delegates to
/// <see cref="BuildThemeCommand"/> so this tool and the CLI verb resolve the version, map inputs, and write
/// identically.
/// </summary>
[McpServerToolType]
public sealed class BuildThemeTool(BuildThemeCommand command) {

	internal const string ToolName = "build-theme";

	private static readonly Regex PackageNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

	/// <summary>
	/// Builds the theme from the supplied brand inputs and the bundled template. Returns the CSS + descriptor
	/// strings, or — when <paramref name="workspaceDirectory"/> and <paramref name="packageName"/> are given —
	/// writes the artifacts into that workspace package and returns the written path.
	/// </summary>
	/// <returns>A structured result carrying the built CSS (compute mode) or the written path (workspace-write mode), or a failure message.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Build the artifacts of a Creatio theme from brand colours and fonts. " +
		"Without workspaceDirectory+packageName: returns { success, css, descriptor, warnings?, error? } — pipe css into create-theme-by-environment's cssContent. " +
		"With workspaceDirectory+packageName (workspace/dev flow): writes theme.css + theme.json into <workspaceDirectory>/packages/<packageName>/Files/themes/<cssClassName>/ and returns { success, path, warnings?, error? } WITHOUT the css (avoids round-tripping the large CSS through the agent). " +
		"Never mutates an environment. For the theme workflow, read get-guidance theming first.")]
	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "Tool parameters intentionally mirror the build-theme MCP contract.")]
	public BuildThemeResult BuildTheme(
		[Description("Brand primary colour (#rrggbb, #rgb, rgb(), hsl(), or a named colour)")] [Required] string primary,
		[Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100); derived from caption (slugified) when omitted — prefer passing caption and letting clio derive this")] string cssClassName = null,
		[Description("Human-readable theme name/caption for theme.json; clio derives cssClassName from it (slugified) when cssClassName is omitted")] string caption = null,
		[Description("Theme id for theme.json; an auto-generated UUID when omitted")] string id = null,
		[Description("Secondary colour; derived from the primary when omitted")] string secondary = null,
		[Description("Accent colour; chosen from the primary when omitted")] string accent = null,
		[Description("Success colour; the platform default when omitted")] string success = null,
		[Description("Error colour; the platform default when omitted")] string error = null,
		[Description("Heading font family; Montserrat when omitted")] string headingFont = null,
		[Description("Body font family; Montserrat when omitted")] string bodyFont = null,
		[Description("Font weights to load (e.g. [400,500,600]); ignored without a custom heading/body font; defaults to 400,500,600")] int[] fontWeights = null,
		[Description("Creatio version the theme targets (e.g. 10.0); the newest supported version is used when omitted; mutually exclusive with environmentName")] string version = null,
		[Description("Registered environment whose Creatio version the theme targets; mutually exclusive with version")] string environmentName = null,
		[Description("Absolute path to the clio workspace to write into (workspace/dev flow); provide together with packageName. Omit both workspaceDirectory and packageName to return the css + descriptor strings instead")] string workspaceDirectory = null,
		[Description("Package inside the workspace to write theme.css + theme.json into, under Files/themes/<cssClassName>/; provide together with workspaceDirectory")] string packageName = null) {
		if (string.IsNullOrWhiteSpace(primary)) {
			return BuildThemeResult.Failure("primary is required and cannot be empty.");
		}
		bool writeToPackage = !string.IsNullOrWhiteSpace(workspaceDirectory) || !string.IsNullOrWhiteSpace(packageName);
		if (writeToPackage) {
			if (string.IsNullOrWhiteSpace(workspaceDirectory) || string.IsNullOrWhiteSpace(packageName)) {
				return BuildThemeResult.Failure(
					"workspaceDirectory and packageName must be provided together to write into a workspace package; omit both to return the css + descriptor strings instead.");
			}
			if (!Path.IsPathFullyQualified(workspaceDirectory)) {
				return BuildThemeResult.Failure(
					$"workspaceDirectory must be a fully-qualified absolute path. Drive-relative ('C:ws') and root-relative ('\\ws') paths are rejected because the MCP server working directory differs from the caller's. Received: '{workspaceDirectory}'.");
			}
			if (!PackageNamePattern.IsMatch(packageName)) {
				return BuildThemeResult.Failure(
					$"packageName must be a simple identifier matching '^[A-Za-z0-9_]+$'. Path separators, '..', and absolute paths are rejected to keep the write inside the workspace. Received: '{packageName}'.");
			}
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
		if (writeToPackage) {
			if (!command.TryBuildTheme(options, workspaceDirectory, packageName, out string writtenPath, out IReadOnlyList<string> writeWarnings, out string writeError)) {
				return BuildThemeResult.Failure(writeError);
			}
			return BuildThemeResult.Written(writtenPath, writeWarnings);
		}
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

	/// <summary>The built <c>theme.json</c> descriptor; omitted on failure and in output (workspace-write) mode.</summary>
	[JsonPropertyName("descriptor")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Descriptor { get; init; }

	/// <summary>The theme directory (inside the package) the <c>theme.css</c> + <c>theme.json</c> were written to; present only in workspace-write mode.</summary>
	[JsonPropertyName("path")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Path { get; init; }

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

	/// <summary>
	/// Creates a success result for the output (workspace-write) mode: the artifacts were written to
	/// <paramref name="path"/>, and the CSS payload is intentionally omitted to keep it out of the agent context.
	/// </summary>
	public static BuildThemeResult Written(string path, IReadOnlyList<string> warnings) {
		return new() {
			Success = true,
			Path = path,
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
