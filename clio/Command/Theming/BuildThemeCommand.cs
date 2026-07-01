using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Theming;
using Clio.UserEnvironment;
using CommandLine;
using ThemeBuilderOptions = Clio.Theming.BuildThemeOptions;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the <c>build-theme</c> command.
/// </summary>
[Verb("build-theme", HelpText = "Build the artifacts of a Creatio theme from brand colours and fonts")]
[FeatureToggle("theming")]
public sealed class BuildThemeOptions {
	/// <summary>Required brand primary colour (any CSS form: #rrggbb, #rgb, rgb(), hsl(), or a named colour).</summary>
	[Option("primary", Required = true, HelpText = "Brand primary colour (#rrggbb, #rgb, rgb(), hsl(), or a named colour)")]
	public string Primary { get; set; }

	/// <summary>Optional secondary colour; derived from the primary when omitted.</summary>
	[Option("secondary", Required = false, HelpText = "Secondary colour; derived from the primary when omitted")]
	public string Secondary { get; set; }

	/// <summary>Optional accent colour; chosen from the primary when omitted.</summary>
	[Option("accent", Required = false, HelpText = "Accent colour; chosen from the primary when omitted")]
	public string Accent { get; set; }

	/// <summary>Optional success colour; the platform default when omitted.</summary>
	[Option("success", Required = false, HelpText = "Success colour; the platform default when omitted")]
	public string Success { get; set; }

	/// <summary>Optional error colour; the platform default when omitted.</summary>
	[Option("error", Required = false, HelpText = "Error colour; the platform default when omitted")]
	public string Error { get; set; }

	/// <summary>Required CSS class applied when the theme is active.</summary>
	[Option("css-class-name", Required = true, HelpText = "CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100)")]
	public string CssClassName { get; set; }

	/// <summary>Optional heading font family; Montserrat when omitted.</summary>
	[Option("heading-font", Required = false, HelpText = "Heading font family; Montserrat when omitted")]
	public string HeadingFont { get; set; }

	/// <summary>Optional body font family; Montserrat when omitted.</summary>
	[Option("body-font", Required = false, HelpText = "Body font family; Montserrat when omitted")]
	public string BodyFont { get; set; }

	/// <summary>Optional font weights to load; defaults to 400,500,600.</summary>
	[Option("font-weights", Required = false, Separator = ',', HelpText = "Font weights to load, comma-separated (e.g. 400,500,600); ignored without a custom heading/body font; defaults to 400,500,600")]
	public IEnumerable<int> FontWeights { get; set; }

	/// <summary>Optional theme id written to theme.json (directory output); an auto-generated UUID when omitted.</summary>
	[Option("id", Required = false, HelpText = "Theme id for theme.json (directory output); an auto-generated UUID when omitted")]
	public string Id { get; set; }

	/// <summary>Optional theme caption written to theme.json (directory output); derived from the CSS class when omitted.</summary>
	[Option("caption", Required = false, HelpText = "Theme caption for theme.json (directory output); derived from --css-class-name when omitted")]
	public string Caption { get; set; }

	/// <summary>Optional Creatio version the theme targets; the newest supported version is used when omitted.</summary>
	[Option("version", Required = false, HelpText = "Creatio version the theme targets (e.g. 10.0); the newest supported version is used when omitted")]
	public string Version { get; set; }

	/// <summary>Optional registered environment whose Creatio version the theme targets; mutually exclusive with --version.</summary>
	[Option("environment-name", Required = false, HelpText = "Registered environment whose Creatio version the theme targets; mutually exclusive with --version")]
	public string EnvironmentName { get; set; }

	/// <summary>Optional output directory; writes theme.css + theme.json. Prints theme.css to stdout when omitted.</summary>
	[Option("output", Required = false, HelpText = "Output directory; writes theme.css + theme.json. Prints theme.css to stdout when omitted")]
	public string Output { get; set; }
}

/// <summary>
/// Builds a theme's <c>theme.css</c> (and, when an output directory is given, its <c>theme.json</c>) from
/// brand inputs and the bundled theme template; prints the CSS to stdout otherwise.
/// </summary>
public class BuildThemeCommand : Command<BuildThemeOptions> {

	private readonly IThemeCssBuilder _themeCssBuilder;
	private readonly IThemeTemplateProvider _themeTemplateProvider;
	private readonly IPlatformVersionResolverFactory _resolverFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;

	/// <summary>Initializes the command with the theme builder, template provider, version resolver, settings repository, file system, and logger.</summary>
	public BuildThemeCommand(IThemeCssBuilder themeCssBuilder, IThemeTemplateProvider themeTemplateProvider,
		IPlatformVersionResolverFactory resolverFactory, ISettingsRepository settingsRepository,
		IFileSystem fileSystem, ILogger logger) {
		_themeCssBuilder = themeCssBuilder;
		_themeTemplateProvider = themeTemplateProvider;
		_resolverFactory = resolverFactory;
		_settingsRepository = settingsRepository;
		_fileSystem = fileSystem;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(BuildThemeOptions options) {
		if (!TryBuildTheme(options, out string css, out string descriptor, out IReadOnlyList<string> warnings, out string error)) {
			_logger.WriteError(error);
			return 1;
		}
		foreach (string warning in warnings) {
			_logger.WriteWarning(warning);
		}
		if (string.IsNullOrEmpty(options.Output)) {
			_logger.WriteInfo(css);
			return 0;
		}
		_fileSystem.CreateDirectoryIfNotExists(options.Output);
		_fileSystem.WriteAllTextToFile(Path.Combine(options.Output, "theme.css"), css);
		_fileSystem.WriteAllTextToFile(Path.Combine(options.Output, "theme.json"), descriptor);
		_logger.WriteInfo($"Theme '{options.CssClassName}' written to {options.Output}");
		return 0;
	}

	/// <summary>
	/// Builds the artifacts of a theme from <paramref name="options"/> and the bundled, version-matched
	/// template — the <c>theme.css</c> and its <c>theme.json</c> descriptor. Shared by the CLI command and the
	/// <c>build-theme</c> MCP tool so both surfaces resolve the version and map the inputs identically.
	/// </summary>
	/// <param name="options">The brand inputs and template-version selectors.</param>
	/// <param name="css">The built <c>theme.css</c> on success; otherwise <c>null</c>.</param>
	/// <param name="descriptor">The built <c>theme.json</c> descriptor on success; otherwise <c>null</c>.</param>
	/// <param name="warnings">Non-fatal advisories on success; an empty list when there are none.</param>
	/// <param name="error">The diagnostic message on failure; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when the artifacts were built; <c>false</c> when an input or template error is reported in <paramref name="error"/>.</returns>
	public bool TryBuildTheme(BuildThemeOptions options, out string css, out string descriptor, out IReadOnlyList<string> warnings, out string error) {
		css = null;
		descriptor = null;
		warnings = [];
		error = null;
		try {
			PlatformVersionResolution resolution = ResolveVersion(options);
			string templateVersion = resolution.Source == VersionResolutionSource.LatestFallback ? null : resolution.ResolvedVersion;
			css = _themeCssBuilder.Build(_themeTemplateProvider.GetCssTemplate(templateVersion), ToBuilderOptions(options));
			descriptor = BuildDescriptor(options, templateVersion);
			warnings = CollectWarnings(options);
			return true;
		}
		catch (ArgumentException ex) {
			error = ex.Message;
			return false;
		}
		catch (InvalidOperationException ex) {
			error = ex.Message;
			return false;
		}
	}

	private PlatformVersionResolution ResolveVersion(BuildThemeOptions options) {
		bool hasVersion = !string.IsNullOrWhiteSpace(options.Version);
		bool hasEnvironment = !string.IsNullOrWhiteSpace(options.EnvironmentName);
		if (hasVersion && hasEnvironment) {
			throw new ArgumentException(
				"build-theme: --version and --environment-name are mutually exclusive. Pass one or neither.");
		}
		if (hasVersion) {
			return new PlatformVersionResolution(options.Version.Trim(), VersionResolutionSource.Environment);
		}
		if (hasEnvironment) {
			EnvironmentSettings settings = _settingsRepository.FindEnvironment(options.EnvironmentName)
				?? throw new ArgumentException($"build-theme: environment '{options.EnvironmentName}' is not registered.");
			PlatformVersionResolution resolution = _resolverFactory.Create(settings).ResolveAsync(CancellationToken.None).GetAwaiter().GetResult();
			if (resolution.Source == VersionResolutionSource.LatestFallback) {
				throw new ArgumentException(
					$"build-theme: could not determine the Creatio version of environment '{options.EnvironmentName}'. "
					+ "Pass --version to choose a template explicitly.");
			}
			return resolution;
		}
		return new PlatformVersionResolution(null, VersionResolutionSource.LatestFallback);
	}

	private static ThemeBuilderOptions ToBuilderOptions(BuildThemeOptions options) {
		List<int> weights = options.FontWeights?.ToList();
		bool hasCustomFont = !string.IsNullOrEmpty(options.HeadingFont) || !string.IsNullOrEmpty(options.BodyFont)
			|| weights is { Count: > 0 };
		return new ThemeBuilderOptions {
			Primary = options.Primary,
			Secondary = options.Secondary,
			Accent = options.Accent,
			Success = options.Success,
			Error = options.Error,
			ThemeCssClass = options.CssClassName,
			Fonts = hasCustomFont ? new FontsInput(options.HeadingFont, options.BodyFont, weights) : null,
		};
	}

	private static IReadOnlyList<string> CollectWarnings(BuildThemeOptions options) {
		List<string> warnings = [];
		if (FontWeightsWithoutFamily(options)) {
			warnings.Add("build-theme: font weights were ignored — they apply only to a custom heading or body font.");
		}
		return warnings;
	}

	private static bool FontWeightsWithoutFamily(BuildThemeOptions options) {
		return options.FontWeights?.Any() == true
			&& string.IsNullOrEmpty(options.HeadingFont)
			&& string.IsNullOrEmpty(options.BodyFont);
	}

	private string BuildDescriptor(BuildThemeOptions options, string creatioVersion) {
		string id = string.IsNullOrEmpty(options.Id) ? Guid.NewGuid().ToString("D") : options.Id;
		string caption = string.IsNullOrEmpty(options.Caption) ? options.CssClassName : options.Caption;
		return _themeTemplateProvider.GetJsonTemplate(creatioVersion)
			.Replace("<%themeId%>", JsonEscape(id))
			.Replace("<%themeCaption%>", JsonEscape(caption))
			.Replace("<%themeCssClass%>", JsonEscape(options.CssClassName));
	}

	private static string JsonEscape(string value) {
		return JsonEncodedText.Encode(value).ToString();
	}
}
