using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.Theming;
using Clio.Theming;
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

	/// <summary>
	/// Stable, caller-facing error-code prefixes for <c>create-theme</c>. Agents branch on these, and
	/// <c>docs/McpCapabilityMap.md</c> documents them, so they live here as constants rather than as
	/// literals repeated at each failure site — a reworded literal would silently desync the wire value
	/// from the documented contract. <c>CreateThemeToolTests.ErrorCodes_ShouldMatch_TheDocumentedContract</c> pins them against that document.
	/// </summary>
	internal static class ErrorCodes {
		internal const string CssSourceConflict = "theme-css-source-conflict";
		internal const string CssSourceMissing = "theme-css-source-missing";
		internal const string BrandPrimaryMissing = "theme-brand-primary-missing";
		internal const string BuildFailed = "theme-build-failed";
	}

	/// <summary>
	/// The brand surface, derived from <see cref="ThemeBrandArgs"/> so a property added there cannot be
	/// missed by the conflict guard. Ordered by metadata token because <c>Type.GetProperties()</c> gives no
	/// order and <see cref="BrandParameterNames"/> renders a caller-facing contract string from the same
	/// derivation (<see cref="ResolveBrandProperties"/>).
	/// </summary>
	private static readonly PropertyInfo[] BrandProperties = ResolveBrandProperties();

	/// <summary>
	/// The parameter list the conflict message names, built from the same source as the guard so the two can
	/// never disagree. <c>primary</c> and <c>version</c> bracket the reflective names because both are
	/// declared on <see cref="CreateThemeArgs"/> and checked explicitly.
	/// </summary>
	private static readonly string BrandParameterNames = RenderBrandParameterNames();

	/// <summary>
	/// The argument roster returned when a caller sends an unrecognised field name. Only the non-brand prefix
	/// is hand-listed — the brand half is the generated <see cref="RenderBrandParameterNames"/> output, because
	/// this is the corrective text an agent reads after a rejected call: a brand parameter missing from it is
	/// worse than stale, it tells the caller a legitimate argument is invalid.
	/// </summary>
	internal static readonly string ValidArgumentNames =
		"Valid: environment-name, css-content, css-class-name, caption, id, package-name, "
		+ $"{RenderBrandParameterNames()}.";

	// Each initializer above derives its value through these methods instead of reading a sibling field:
	// static field initializers run in textual order, so a field-on-field dependency would make an innocent
	// declaration reorder fault the type (or silently truncate the roster) with no compiler signal. The
	// method calls keep every initializer self-contained and the order irrelevant.
	private static PropertyInfo[] ResolveBrandProperties() {
		return typeof(ThemeBrandArgs).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.OrderBy(property => property.MetadataToken)
			.ToArray();
	}

	private static string RenderBrandParameterNames() {
		return string.Join(", ",
			new[] { "primary" }
				.Concat(ResolveBrandProperties().Select(WireNameOf))
				.Append("version"));
	}

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
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true),
	 Description("Create a custom Creatio theme on a registered environment via the native ThemeService. " +
		"Requires Creatio " + ThemeServiceRequirement.MinVersion + " or later on the target environment. " +
		"Returns { success, id, warnings?, error? } where id is the created theme's id, auto-generated when omitted. " +
		"The theme CSS is supplied inline via css-content, OR built server-side from brand colours and fonts " +
		"(primary, secondary, accent, success, error, heading-font, body-font, font-weights, version) and created " +
		"in one call — provide exactly one of css-content / primary. In brand mode custom font families are " +
		"checked against Google Fonts over the network (a short bounded probe): a family the catalog does not " +
		"publish gets no @import plus a warning, and an unverifiable probe keeps the import plus a warning. " +
		"The inline css-content path stays tenant-only (no Google Fonts probe) — OpenWorld reflects the brand-mode probe only. " +
		"For the theme workflow, read get-guidance theming first.")]
	public CreateThemeResult CreateTheme(
		[Description("Parameters: environment-name (required), css-content (inline mode) or primary (brand mode) — exactly one of the two, " +
			"css-class-name, caption, id, package-name, secondary, accent, success, error, heading-font, body-font, font-weights, version (all optional).")]
		[Required] CreateThemeArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".", ValidArgumentNames);
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
				$"{ErrorCodes.CssSourceConflict}: css-content and the brand parameters ({BrandParameterNames}) " +
				"are mutually exclusive. Provide inline CSS or brand colours, not both.");
		}
		if (!hasCssContent && !brandMode) {
			return CreateThemeResult.Failure(HasAnyBrandParameter(args)
				? $"{ErrorCodes.BrandPrimaryMissing}: primary is required for the brand mode — the other brand parameters " +
					"only refine the palette derived from it or select the build template. Pass primary, or provide css-content instead."
				: $"{ErrorCodes.CssSourceMissing}: provide either css-content (inline CSS) or primary (brand colours; " +
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
		return brandMode ? ExecuteBrandMode(options, args) : Execute(options);
	}

	private CreateThemeResult ExecuteBrandMode(CreateThemeOptions options, CreateThemeArgs args) {
		IReadOnlyList<string> buildWarnings = null;
		// Tracks whether a thrown exception escaped the BUILD phase. TryBuildTheme reports input/template
		// errors through its false-return channel (prefixed below), but an unexpected build-phase fault — an
		// unreadable bundled template, a resolver wiring failure — propagates to ExecuteResolved's catch,
		// which knows nothing about phases. Without this flag such a failure would reach the caller without
		// the documented theme-build-failed code, outside the taxonomy agents branch on
		// (docs/McpCapabilityMap.md). Version-gate and create-phase failures stay unprefixed.
		bool buildPhase = false;
		return ExecuteResolved<CreateThemeCommand, CreateThemeResult>(options,
			resolvedCommand => {
				buildPhase = true;
				if (!TryBuildBrandCss(args, out string css, out buildWarnings, out string buildError)) {
					return CreateThemeResult.Failure(
						$"{ErrorCodes.BuildFailed}: {SensitiveErrorTextRedactor.Redact(buildError)}", buildWarnings);
				}
				buildPhase = false;
				options.CssContent = css;
				return CreateOnEnvironment(resolvedCommand, options, buildWarnings);
			},
			// error arrives pre-redacted: ExecuteResolved's catch runs every escaping exception message
			// through SensitiveErrorTextRedactor before invoking this callback, so prefixing is all that
			// remains here. Pinned by CreateTheme_ShouldRedactFaultText_WhenBuildPhaseExceptionCarriesSensitiveContent.
			error => CreateThemeResult.Failure(
				buildPhase ? $"{ErrorCodes.BuildFailed}: {error}" : error, buildWarnings));
	}

	private CreateThemeResult Execute(CreateThemeOptions options) {
		return ExecuteResolved<CreateThemeCommand, CreateThemeResult>(options,
			resolvedCommand => CreateOnEnvironment(resolvedCommand, options, buildWarnings: null),
			error => CreateThemeResult.Failure(error));
	}

	private static CreateThemeResult CreateOnEnvironment(CreateThemeCommand command, CreateThemeOptions options,
		IReadOnlyList<string> buildWarnings) {

		if (!command.TryCreateTheme(options, out string createdId, out string errorMessage)) {
			return CreateThemeResult.Failure(
				string.IsNullOrWhiteSpace(errorMessage)
					? "CreateTheme returned success=false."
					: SensitiveErrorTextRedactor.Redact(errorMessage),
				buildWarnings);
		}
		return CreateThemeResult.Successful(createdId, buildWarnings);
	}

	private static bool HasAnyBrandParameter(CreateThemeArgs args) {
		return !string.IsNullOrWhiteSpace(args.Primary)
			|| !string.IsNullOrWhiteSpace(args.Version)
			|| BrandProperties.Any(property => IsSupplied(property, property.GetValue(args)));
	}

	private static string WireNameOf(PropertyInfo property) {
		return property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
	}

	// The nullable check and the ValueType arm are unreachable until ThemeBrandArgs gains a value-type
	// property; without them boxing makes such a property read as always-supplied, so every request would
	// look like a brand request. Do not drop them as dead code.
	private static bool IsSupplied(PropertyInfo property, object value) {
		if (Nullable.GetUnderlyingType(property.PropertyType) is not null) {
			return value is not null;
		}
		return value switch {
			null => false,
			string text => !string.IsNullOrWhiteSpace(text),
			ICollection collection => collection.Count > 0,
			ValueType boxed => !Equals(boxed, Activator.CreateInstance(boxed.GetType())),
			_ => true
		};
	}

	// Runs inside ExecuteResolved's per-tenant lock, which means the Google Fonts availability probe
	// (TryBuildTheme -> ResolveFontAvailability) can hold that lock across network I/O. Accepted because the
	// probe is tightly bounded: at most 2 distinct families per call, probed CONCURRENTLY (Task.WhenAll), so
	// the worst case is one ProbeTimeout (~3s) total — zero for the default/Montserrat fonts — and verdicts
	// are memoized process-wide (definitive ones for 5 minutes, unverified ones for 30 seconds). The
	// ThemeService create round-trip already holds this same lock for longer. Hoisting the build out of
	// ExecuteResolved would invert the post-gate ordering guarantee: the version gate must refuse a
	// below-floor environment BEFORE any build work runs.
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
			// Must stay null: the environment reaches the build only as resolvedSettings. Copying it here
			// alongside an explicit version trips ResolveVersion's version/environment mutual-exclusion
			// guard on every version+environment call (pinned by
			// CreateTheme_ShouldKeepBuildEnvironmentNameNull_WhenEnvironmentNameAndVersionBothSupplied).
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
