using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Theming;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that builds a Creatio <c>theme.css</c> from brand colours and fonts using the deterministic
/// palette engine and the bundled, version-matched template. The template version comes from <c>version</c>,
/// or from <c>environment-name</c> (whose Creatio version it reads), or defaults to the newest supported
/// version. It has two output modes: <b>compute</b> (neither <c>workspace-directory</c> nor <c>package-name</c>)
/// returns the CSS and descriptor strings, and <b>workspace-write</b> (<c>workspace-directory</c> +
/// <c>package-name</c>) writes <c>theme.css</c> + <c>theme.json</c> into
/// <c>&lt;workspace-directory&gt;/packages/&lt;package-name&gt;/Files/themes/&lt;css-class-name&gt;/</c> and returns the
/// written path without the CSS payload — keeping the large CSS out of the agent context. Delegates to
/// <see cref="BuildThemeCommand"/> so this tool and the CLI verb resolve the version, map inputs, and write
/// identically.
/// </summary>
/// <remarks>
/// Execution goes through <see cref="BaseTool{T}.ExecuteWithCleanLogUnderToolLock{TResponse}"/>, so it is
/// serialized against other <c>build-theme</c> calls only: the workspace-write mode mutates the singleton
/// <c>IWorkspacePathBuilder.RootPath</c> inside <see cref="BuildThemeCommand"/>, which must not race with
/// concurrent invocations in a long-lived MCP server. The tool resolves no environment and acquires no
/// session container, so it deliberately does NOT take the shared fallback lock — its bounded Google Fonts
/// probe would otherwise stall every other environment-less tool for the probe budget.
/// </remarks>
/// <remarks>
/// Pattern B (ADR verification #5, ENG-93347): <see cref="BuildThemeOptions"/> is not
/// <see cref="EnvironmentOptions"/>-derived, so this tool cannot route through
/// <see cref="BaseTool{T}"/>'s <c>InternalExecute&lt;TCommand&gt;</c> environment-resolution path. Instead,
/// when <c>version</c> is blank, the tool resolves <see cref="EnvironmentSettings"/> itself (via the
/// optional <c>commandResolver</c> constructor dependency) and passes the result into the command's
/// resolvedSettings-aware <c>TryBuildTheme</c> overloads, so the version probe reaches the correct — possibly
/// header-derived, credential-passthrough — tenant instead of a header-blind name lookup.
/// </remarks>
public sealed class BuildThemeTool(
	BuildThemeCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver = null) : BaseTool<BuildThemeOptions>(command, logger) {

	internal const string ToolName = "build-theme";

	/// <summary>The argument roster returned when a caller sends an unrecognised field name.</summary>
	internal static readonly string ValidArgumentNames =
		"Valid: primary, css-class-name, caption, id, secondary, accent, success, error, " +
		"heading-font, body-font, font-weights, version, environment-name, workspace-directory, package-name.";

	private static readonly Regex PackageNamePattern = new(@"^[A-Za-z0-9_]+\z", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

	// Known mis-spellings an LLM tends to emit instead of the kebab-case argument names. Rejected with
	// an actionable rename hint so a camelCase 'cssClassName' never silently binds to nothing.
	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["cssClassName"] = "css-class-name",
		["css_class_name"] = "css-class-name",
		["headingFont"] = "heading-font",
		["heading_font"] = "heading-font",
		["bodyFont"] = "body-font",
		["body_font"] = "body-font",
		["fontWeights"] = "font-weights",
		["font_weights"] = "font-weights",
		["environmentName"] = "environment-name",
		["environment_name"] = "environment-name",
		["workspaceDirectory"] = "workspace-directory",
		["workspace_directory"] = "workspace-directory",
		["packageName"] = "package-name",
		["package_name"] = "package-name"
	};

	/// <summary>
	/// Builds the theme from the supplied brand inputs and the bundled template. Returns the CSS + descriptor
	/// strings, or — when <c>workspace-directory</c> and <c>package-name</c> are given — writes the artifacts
	/// into that workspace package and returns the written path.
	/// </summary>
	/// <returns>A structured result carrying the built CSS (compute mode) or the written path (workspace-write mode), or a failure message.</returns>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
	[Description("Build the artifacts of a Creatio theme from brand colours and fonts. " +
		"Without workspace-directory+package-name: returns { success, css, descriptor, warnings?, error? } — use it when you need the CSS itself. " +
		"To create a theme on an environment from brand inputs, prefer create-theme's brand mode: it builds the same CSS server-side in one call instead of routing it through the agent. " +
		"With workspace-directory+package-name (workspace/dev flow): writes theme.css + theme.json into <workspace-directory>/packages/<package-name>/Files/themes/<css-class-name>/ and returns { success, path, warnings?, error? } WITHOUT the css (avoids round-tripping the large CSS through the agent). " +
		"Custom font families are checked against Google Fonts over the network (a short bounded probe): a family the catalog does not publish gets NO @import (it renders only where installed locally) plus a warning, and an unverifiable probe keeps the import plus a warning — so the css can vary with probe outcomes, which the warnings always disclose. " +
		"Re-running with the same css-class-name overwrites the previously written files; when id is omitted, each run generates a fresh descriptor id — pass id to keep reruns byte-identical (given the same probe outcomes). " +
		"Never mutates an environment. For the theme workflow, read get-guidance theming first.")]
	public BuildThemeResult BuildTheme(
		[Description("Parameters: primary (required), css-class-name, caption, id, secondary, accent, success, error, " +
			"heading-font, body-font, font-weights, version, environment-name, workspace-directory, package-name (all optional).")]
		[Required] BuildThemeArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".", ValidArgumentNames);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return BuildThemeResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.Primary)) {
			return BuildThemeResult.Failure("primary is required and cannot be empty.");
		}
		bool writeToPackage = !string.IsNullOrWhiteSpace(args.WorkspaceDirectory) || !string.IsNullOrWhiteSpace(args.PackageName);
		if (writeToPackage && !TryValidateWorkspaceTarget(args, out string? targetError)) {
			return BuildThemeResult.Failure(targetError);
		}
		BuildThemeOptions options = new() {
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
			EnvironmentName = args.EnvironmentName
		};
		EnvironmentSettings resolvedSettings = ResolveVersionSettings(args, out string environmentFallbackWarning);
		return ExecuteWithCleanLogUnderToolLock(() => {
			if (writeToPackage) {
				if (!command.TryBuildTheme(options, resolvedSettings, args.WorkspaceDirectory, args.PackageName, out string writtenPath, out IReadOnlyList<string> writeWarnings, out string writeError)) {
					return BuildThemeResult.Failure(writeError);
				}
				return BuildThemeResult.Written(writtenPath, PrependWarning(environmentFallbackWarning, writeWarnings));
			}
			if (!command.TryBuildTheme(options, resolvedSettings, out string css, out string descriptor, out IReadOnlyList<string> warnings, out string buildError)) {
				return BuildThemeResult.Failure(buildError);
			}
			return BuildThemeResult.Successful(css, descriptor, PrependWarning(environmentFallbackWarning, warnings));
		});
	}

	// Pattern B (ADR verification #5, ENG-93347): BuildThemeOptions is not EnvironmentOptions-derived, so this
	// tool cannot route the version probe through BaseTool's InternalExecute<TCommand> environment resolution.
	// Instead, when --version is blank, the TOOL resolves EnvironmentSettings itself (the same commandResolver
	// every other matrix tool probes with) and passes the result into the command, which resolves the version
	// against it directly — reaching the header tenant under credential passthrough instead of a header-blind
	// name lookup. An explicit --version always wins and skips this attempt entirely (AC-07).
	//
	// A caller-actionable resolution failure (unresolvable/typo environment, broken bootstrap, or the
	// mixed-input HasExplicitCredentialArgs rejection under passthrough) surfaces from the resolver as an
	// EnvironmentResolutionException; it is caught and fails soft to LatestFallback (AC-02/AC-03) — the same
	// documented fallback the CLI's offline path produces. The catch is DELIBERATELY narrowed to
	// EnvironmentResolutionException — not a broad catch-all of the base Exception type, which would violate the
	// no-bare-catch rule / S2221: an unexpected fault such as a NullReference or a DI/wiring bug must NOT be
	// masked as a silent newest-version build — it propagates to a real error response, as the resolver's own
	// expected-vs-unexpected contract (exit 1 vs -1) intends. And the fallback is no longer SILENT: when the caller explicitly named an
	// environment we could not resolve, fallbackWarning names the drop to the newest template so it is visible
	// instead of a silent success that diverges from the CLI's hard error.
	private EnvironmentSettings ResolveVersionSettings(BuildThemeArgs args, out string fallbackWarning) {
		fallbackWarning = null;
		if (!string.IsNullOrWhiteSpace(args.Version) || commandResolver is null) {
			return null;
		}
		try {
			return commandResolver.Resolve<EnvironmentSettings>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
		}
		catch (EnvironmentResolutionException) {
			if (!string.IsNullOrWhiteSpace(args.EnvironmentName)) {
				fallbackWarning = SensitiveErrorTextRedactor.Redact(
					$"build-theme: could not resolve environment '{args.EnvironmentName}' — built against the "
					+ "newest supported version instead. Pass version to target a specific template, or omit "
					+ "environment-name to use the credential-passthrough tenant's version.");
			}
			return null;
		}
	}

	// Prepends the optional environment-fallback advisory (when the caller named an environment that could not
	// be resolved) to the command's own warnings, so the non-silent fallback marker rides the same warnings
	// channel. Returns the command warnings unchanged when there is no advisory to add.
	private static IReadOnlyList<string> PrependWarning(string warning, IReadOnlyList<string> warnings) {
		if (string.IsNullOrWhiteSpace(warning)) {
			return warnings;
		}
		List<string> combined = [warning];
		if (warnings is { Count: > 0 }) {
			combined.AddRange(warnings);
		}
		return combined;
	}

	private static bool TryValidateWorkspaceTarget(BuildThemeArgs args, [NotNullWhen(false)] out string? error) {
		error = null;
		if (string.IsNullOrWhiteSpace(args.WorkspaceDirectory) || string.IsNullOrWhiteSpace(args.PackageName)) {
			error = "workspace-directory and package-name must be provided together to write into a workspace package; omit both to return the css + descriptor strings instead.";
			return false;
		}
		if (!Path.IsPathFullyQualified(args.WorkspaceDirectory)) {
			error = $"workspace-directory must be a fully-qualified absolute path. Drive-relative ('C:ws') and root-relative ('\\ws') paths are rejected because the MCP server working directory differs from the caller's. Received: '{args.WorkspaceDirectory}'.";
			return false;
		}
		if (!PackageNamePattern.IsMatch(args.PackageName)) {
			error = $"package-name must be a simple identifier matching '^[A-Za-z0-9_]+$'. Path separators, '..', and absolute paths are rejected to keep the write inside the workspace. Received: '{args.PackageName}'.";
			return false;
		}
		if (!string.IsNullOrWhiteSpace(args.CssClassName)
			&& !ThemeParameterValidator.TryValidateCssClassName(args.CssClassName, out string cssClassError)) {
			error = cssClassError;
			return false;
		}
		return true;
	}
}

/// <summary>
/// MCP arguments for the <c>build-theme</c> tool.
/// </summary>
public sealed record BuildThemeArgs(
	[property: JsonPropertyName("primary")]
	[property: Description("Brand primary colour (#rrggbb, #rgb, rgb(), hsl(), or a named colour).")]
	[property: Required]
	string? Primary = null,

	[property: JsonPropertyName("css-class-name")]
	[property: Description("CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100); derived from caption (lowercased and hyphenated) when omitted — prefer passing caption and letting clio derive this.")]
	string? CssClassName = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Human-readable theme name/caption for theme.json; clio derives css-class-name from it (lowercased and hyphenated) when css-class-name is omitted.")]
	string? Caption = null,

	[property: JsonPropertyName("id")]
	[property: Description("Theme id for theme.json; an auto-generated UUID when omitted.")]
	string? Id = null,

	[property: JsonPropertyName("version")]
	[property: Description("Creatio version the theme targets (e.g. 10.0); the newest supported version is used when omitted; mutually exclusive with environment-name.")]
	string? Version = null,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered environment whose Creatio version the theme targets; mutually exclusive with version. " +
		"If the environment cannot be resolved or its version cannot be determined, the theme is still built using the newest supported version instead of failing — but a non-fatal warning reports the fallback so it is not silent. " +
		"Always optional, including under credential passthrough: omit it so the header-supplied tenant's version is used; " +
		"if both a passthrough header and environment-name are supplied, the mismatch is treated the same as an unresolvable environment (soft fallback to the newest version with a warning, never an error).")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("workspace-directory")]
	[property: Description("Absolute path to the clio workspace to write into (workspace/dev flow); provide together with package-name. Omit both workspace-directory and package-name to return the css + descriptor strings instead.")]
	string? WorkspaceDirectory = null,

	[property: JsonPropertyName("package-name")]
	[property: Description("Package inside the workspace to write theme.css + theme.json into, under Files/themes/<css-class-name>/; provide together with workspace-directory.")]
	string? PackageName = null
) : ThemeBrandArgs {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
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
