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
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the <c>build-theme</c> command.
/// </summary>
[Verb("build-theme", HelpText = "Build the artifacts of a Creatio theme from brand colours and fonts")]
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

	/// <summary>CSS class applied when the theme is active; derived from <see cref="Caption"/> (lowercased and hyphenated) when omitted.</summary>
	[Option("css-class-name", Required = false, HelpText = "CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100); derived from --caption (lowercased and hyphenated) when omitted")]
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

	private const string PackageFilesFolderName = "Files";
	private const string ThemesFolderName = "themes";

	private readonly IThemeCssBuilder _themeCssBuilder;
	private readonly IThemeTemplateProvider _themeTemplateProvider;
	private readonly IPlatformVersionResolverFactory _resolverFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;

	/// <summary>Initializes the command with the theme builder, template provider, version resolver, settings repository, workspace path builder, file system, and logger.</summary>
	public BuildThemeCommand(IThemeCssBuilder themeCssBuilder, IThemeTemplateProvider themeTemplateProvider,
		IPlatformVersionResolverFactory resolverFactory, ISettingsRepository settingsRepository,
		IWorkspacePathBuilder workspacePathBuilder, IFileSystem fileSystem, ILogger logger) {
		_themeCssBuilder = themeCssBuilder;
		_themeTemplateProvider = themeTemplateProvider;
		_resolverFactory = resolverFactory;
		_settingsRepository = settingsRepository;
		_workspacePathBuilder = workspacePathBuilder;
		_fileSystem = fileSystem;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(BuildThemeOptions options) {
		if (string.IsNullOrEmpty(options.Output)) {
			if (!TryBuildTheme(options, out string css, out _, out IReadOnlyList<string> warnings, out string error)) {
				_logger.WriteError(error);
				return 1;
			}
			WriteWarnings(warnings);
			_logger.WriteInfo(css);
			return 0;
		}
		if (!TryBuildTheme(options, options.Output, out _, out IReadOnlyList<string> writeWarnings, out string writeError)) {
			_logger.WriteError(writeError);
			return 1;
		}
		WriteWarnings(writeWarnings);
		ThemeParameterValidator.TryResolveCssClassName(options.CssClassName, options.Caption, out string themeName, out _);
		_logger.WriteInfo($"Theme '{themeName}' written to {options.Output}");
		return 0;
	}

	private void WriteWarnings(IReadOnlyList<string> warnings) {
		foreach (string warning in warnings) {
			_logger.WriteWarning(warning);
		}
	}

	/// <summary>
	/// Builds the theme and writes its <c>theme.css</c> + <c>theme.json</c> into <paramref name="outputDirectory"/>.
	/// Shared by the CLI workspace-output mode and the <c>build-theme</c> MCP tool's output mode so both surfaces
	/// write identically; the tool returns only the written path (not the CSS) to keep the large payload out of
	/// the agent context.
	/// </summary>
	/// <param name="options">The brand inputs and template-version selectors.</param>
	/// <param name="outputDirectory">The directory to write <c>theme.css</c> and <c>theme.json</c> into.</param>
	/// <param name="outputPath">The written directory on success; otherwise <c>null</c>.</param>
	/// <param name="warnings">Non-fatal advisories on success; an empty list when there are none.</param>
	/// <param name="error">The diagnostic message on failure; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when the artifacts were built and written; <c>false</c> when a build or write error is reported in <paramref name="error"/>.</returns>
	public bool TryBuildTheme(BuildThemeOptions options, string outputDirectory, out string outputPath,
		out IReadOnlyList<string> warnings, out string error) {
		outputPath = null;
		if (!TryBuildTheme(options, out string css, out string descriptor, out warnings, out error)) {
			return false;
		}
		if (!TryWriteArtifacts(outputDirectory, css, descriptor, out error)) {
			return false;
		}
		outputPath = outputDirectory;
		return true;
	}

	/// <summary>
	/// Builds the theme and writes its <c>theme.css</c> + <c>theme.json</c> into a package of a local clio
	/// workspace — at <c>&lt;workspaceDirectory&gt;/packages/&lt;packageName&gt;/Files/themes/&lt;cssClassName&gt;/</c> —
	/// resolving the layout the same way the workspace path builder does so the caller supplies only the workspace
	/// and package (not a physical path). Used by the <c>build-theme</c> MCP tool's workspace-write mode.
	/// </summary>
	/// <param name="options">The brand inputs and template-version selectors (its <c>CssClassName</c> names the theme subfolder).</param>
	/// <param name="workspaceDirectory">The absolute root of the clio workspace.</param>
	/// <param name="packageName">The package inside the workspace to write the theme into.</param>
	/// <param name="outputPath">The written theme directory on success; otherwise <c>null</c>.</param>
	/// <param name="warnings">Non-fatal advisories on success; an empty list when there are none.</param>
	/// <param name="error">The diagnostic message on failure; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when the artifacts were built and written; <c>false</c> when validation, build, or write fails.</returns>
	public bool TryBuildTheme(BuildThemeOptions options, string workspaceDirectory, string packageName,
		out string outputPath, out IReadOnlyList<string> warnings, out string error) {
		outputPath = null;
		warnings = [];
		error = null;
		if (!ThemeParameterValidator.TryResolveCssClassName(options.CssClassName, options.Caption, out string resolvedClass, out error)) {
			return false;
		}
		_workspacePathBuilder.RootPath = workspaceDirectory;
		if (!_workspacePathBuilder.IsWorkspace) {
			error = $"build-theme: '{workspaceDirectory}' is not a clio workspace "
				+ "(missing .clio/workspaceSettings.json). Create it first with create-workspace.";
			return false;
		}
		string packagePath = _workspacePathBuilder.BuildPackagePath(packageName);
		if (!_fileSystem.ExistsDirectory(packagePath)) {
			error = $"build-theme: package '{packageName}' does not exist in the workspace "
				+ $"(expected at '{packagePath}'). Add it first with add-package.";
			return false;
		}
		string themeDirectory = Path.Combine(packagePath, PackageFilesFolderName, ThemesFolderName, resolvedClass);
		return TryBuildTheme(options, themeDirectory, out outputPath, out warnings, out error);
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
		if (!TryNormalizeOptions(options, out BuildThemeOptions normalizedOptions, out error)) {
			return false;
		}
		try {
			PlatformVersionResolution resolution = ResolveVersion(normalizedOptions);
			string templateVersion = resolution.Source == VersionResolutionSource.LatestFallback ? null : resolution.ResolvedVersion;
			css = _themeCssBuilder.Build(_themeTemplateProvider.GetCssTemplate(templateVersion), ToBuilderOptions(normalizedOptions));
			descriptor = BuildDescriptor(normalizedOptions, templateVersion);
			warnings = CollectWarnings(normalizedOptions);
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

	private bool TryWriteArtifacts(string outputDirectory, string css, string descriptor, out string error) {
		error = null;
		try {
			_fileSystem.CreateDirectoryIfNotExists(outputDirectory);
			_fileSystem.WriteAllTextToFile(Path.Combine(outputDirectory, "theme.css"), css);
			_fileSystem.WriteAllTextToFile(Path.Combine(outputDirectory, "theme.json"), descriptor);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			error = DescribeWriteFailure(outputDirectory, ex);
			return false;
		}
	}

	private static string DescribeWriteFailure(string outputDirectory, Exception ex) {
		return $"build-theme: failed to write theme files to '{outputDirectory}': {ex.Message}";
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

	private static bool TryNormalizeOptions(BuildThemeOptions options, out BuildThemeOptions normalizedOptions, out string error) {
		normalizedOptions = null;
		if (!ThemeParameterValidator.TryResolveCssClassName(options.CssClassName, options.Caption, out string resolvedClass, out error)) {
			return false;
		}
		normalizedOptions = new BuildThemeOptions {
			Primary = options.Primary,
			Secondary = options.Secondary,
			Accent = options.Accent,
			Success = options.Success,
			Error = options.Error,
			CssClassName = resolvedClass,
			HeadingFont = options.HeadingFont,
			BodyFont = options.BodyFont,
			FontWeights = options.FontWeights,
			Id = options.Id,
			Caption = options.Caption,
			Version = options.Version,
			EnvironmentName = options.EnvironmentName,
			Output = options.Output,
		};
		return true;
	}

	private static BuildThemeInput ToBuilderOptions(BuildThemeOptions options) {
		List<int> weights = options.FontWeights?.ToList();
		bool hasCustomFont = !string.IsNullOrEmpty(options.HeadingFont) || !string.IsNullOrEmpty(options.BodyFont)
			|| weights is { Count: > 0 };
		return new BuildThemeInput {
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
