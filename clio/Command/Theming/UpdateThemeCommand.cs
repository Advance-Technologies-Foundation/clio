using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command.Branding;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the <c>update-theme</c> command.
/// </summary>
[Verb("update-theme", HelpText = "Overwrite an existing custom Creatio theme on the target environment via the native ThemeService, or change its background")]
[RequiresCreatioVersion(ThemeServiceRequirement.MinVersion)]
public class UpdateThemeOptions : RemoteCommandOptions
{
	/// <summary>Id of the existing theme to overwrite (required).</summary>
	[Option("id", Required = true, HelpText = "Id (a GUID) of the existing theme to overwrite")]
	public string Id { get; set; }

	/// <summary>Human-readable theme caption (≤250), required without a background argument.</summary>
	[Option("caption", Required = false, HelpText = "Human-readable theme caption (max 250)")]
	public string Caption { get; set; }

	/// <summary>CSS class applied when the theme is active (<c>^[A-Za-z][A-Za-z0-9_-]*$</c>, ≤100), required without a background argument.</summary>
	[Option("css-class-name", Required = false,
		HelpText = "CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100)")]
	public string CssClassName { get; set; }

	/// <summary>Inline theme CSS. Mutually exclusive with <c>--css-content-file</c>.</summary>
	[Option("css-content", Required = false,
		HelpText = "Inline theme CSS (mutually exclusive with --css-content-file)")]
	public string CssContent { get; set; }

	/// <summary>Path to a UTF-8 CSS file. Mutually exclusive with <c>--css-content</c>.</summary>
	[Option("css-content-file", Required = false,
		HelpText = "Path to a UTF-8 CSS file (mutually exclusive with --css-content)")]
	public string CssContentFile { get; set; }

	[Option("background-image-id", Required = false,
		HelpText = "Id of an image uploaded with upload-image, set as the theme background; null removes the theme's " +
			"own background. Pass at most one of --background-image-id and --background-color; each needs Creatio " +
			ThemeServiceRequirement.BackgroundMinVersion + " or later.")]
	public string BackgroundImageId { get; set; }

	[Option("background-color", Required = false,
		HelpText = "Solid theme background color: #RGB, #RRGGBB or #RRGGBBAA; null removes the theme's own background.")]
	public string BackgroundColor { get; set; }

	[Option("keep-icon-background", Required = false,
		HelpText = "Keep the UsePanelIconBackground feature as is; without it, a background turns it off for All employees.")]
	public bool KeepIconBackground { get; set; }

	[RequiresCreatioVersion(ThemeServiceRequirement.BackgroundMinVersion, Hint = ThemeServiceRequirement.BackgroundHint)]
	public bool HasBackgroundArgument => ThemeBackground.IsGiven(BackgroundImageId, BackgroundColor);
}

/// <summary>
/// Overwrites an existing custom Creatio theme on the target environment by calling the native
/// <c>ThemeService.svc/UpdateTheme</c> endpoint. The theme is located by <c>id</c> and rewritten in its
/// current package (the package cannot be changed). Requires the <c>CanCustomizeBranding</c> license and the
/// <c>CanManageThemes</c> system operation. Without a background argument this is a full overwrite — caption,
/// css-class-name, and CSS are all required; with one, the omitted fields are kept.
/// </summary>
public class UpdateThemeCommand : RemoteCommand<UpdateThemeOptions>
{
	private static readonly Regex ThemePackagePattern = new(
		"^/?Terrasoft\\.Configuration/Pkg/(?<package>[A-Za-z0-9_]+)/Files/", RegexOptions.None, TimeSpan.FromSeconds(1));

	private readonly IServiceUrlBuilder _urlBuilder;
	private readonly IFileSystem _fileSystem;
	private readonly IThemeBackgroundWriter _backgroundWriter;
	private readonly IThemeCatalog _themeCatalog;

	/// <summary>
	/// Initializes a new instance of the <see cref="UpdateThemeCommand"/> class.
	/// </summary>
	public UpdateThemeCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder urlBuilder, IFileSystem fileSystem, IThemeBackgroundWriter backgroundWriter,
		IThemeCatalog themeCatalog)
		: base(applicationClient, settings) {
		_urlBuilder = urlBuilder;
		_fileSystem = fileSystem;
		_backgroundWriter = backgroundWriter;
		_themeCatalog = themeCatalog;
	}

	/// <inheritdoc />
	protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.UpdateTheme);

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(UpdateThemeOptions options) {
		if (!TryBuildRequest(options, out ThemeRequest request, out string error)
			|| !TryResolveBackgroundPackage(options, request.Background, out string packageName, out error)
			|| !_backgroundWriter.TryAddImageToBackgroundGallery(request.Background, packageName,
				out BackgroundGalleryImage galleryImage, out error)) {
			Fail(error);
			return;
		}
		string requestData = JsonSerializer.Serialize(request);
		string response = ApplicationClient.ExecutePostRequest(ServiceUri, requestData,
			options.TimeOut, options.MaxAttempts, options.RetryDelay);
		if (ThemeServiceResponseParser.TryGetFailure(response, out string failure)) {
			Fail(ThemeServiceResponseParser.DescribeFailure("UpdateTheme", failure));
			return;
		}
		ThemeBackground background = request.Background;
		if (background is not null) {
			Logger.WriteInfo(background.RemovesBackground
				? "The theme has no background now."
				: $"Theme background: {background.Describe()}.");
			PanelIconBackgroundOutcome panelIcon =
				_backgroundWriter.TurnOffPanelIconBackground(background, options.KeepIconBackground);
			ThemeBackgroundBindingOutcome binding = _backgroundWriter.BindBackgroundIntoThemePackage(background,
				galleryImage, options.Id, packageName);
			Logger.WriteWarnings(ThemeBackgroundOutcome.From(panelIcon, binding).Warnings);
		}
	}

	private bool TryBuildRequest(UpdateThemeOptions options, out ThemeRequest request, out string error) {
		request = null;
		if (!ThemeBackground.TryParse(options.BackgroundImageId, options.BackgroundColor, allowRemoval: true,
				out ThemeBackground background, out error)) {
			return false;
		}
		bool partial = background is not null;
		bool hasCss = !string.IsNullOrWhiteSpace(options.CssContent) || !string.IsNullOrWhiteSpace(options.CssContentFile);
		string cssContent = null;
		if ((!partial || hasCss) && !ThemeRequestBuilder.TryResolveCssContent(_fileSystem, options.CssContent,
				options.CssContentFile, out cssContent, out error)) {
			return false;
		}
		request = new ThemeRequest {
			Id = options.Id,
			Caption = partial && string.IsNullOrWhiteSpace(options.Caption) ? null : options.Caption,
			CssClassName = partial && string.IsNullOrWhiteSpace(options.CssClassName) ? null : options.CssClassName,
			CssContent = cssContent,
			Background = background
		};
		return ThemeRequestBuilder.TryValidateRequest(request, out error, partial);
	}

	private bool TryResolveBackgroundPackage(UpdateThemeOptions options, ThemeBackground background,
		out string packageName, out string error) {
		packageName = null;
		error = null;
		return background is null || background.RemovesBackground
			|| TryResolveThemePackage(options, out packageName, out error);
	}

	private void Fail(string error) {
		CommandSuccess = false;
		Logger.WriteError(error);
	}

	private bool TryResolveThemePackage(UpdateThemeOptions options, out string packageName, out string error) {
		packageName = null;
		if (!_themeCatalog.TryGetAvailableThemes(ListThemesOptions.From(options),
				out IReadOnlyList<ThemeDescriptor> themes, out error)) {
			error = ThemeServiceResponseParser.DescribeFailure("GetAvailableThemes", error);
			return false;
		}
		List<ThemeDescriptor> matchingThemes = ThemeCatalogLookup.FindById(themes, options.Id);
		if (matchingThemes.Count > 1) {
			error = $"Theme id '{options.Id}' matches more than one theme on the environment, so the package to bind " +
				"its background into is ambiguous; nothing was changed. Run 'clio list-themes' to inspect it.";
			return false;
		}
		if (matchingThemes.Count == 0) {
			string caveat = themes.Count == 0 ? " " + ThemeCatalogMessages.EmptyCatalogLicenseCaveat : string.Empty;
			error = $"No theme with id '{options.Id}' is on the environment; nothing was changed." + caveat;
			return false;
		}
		Match match = ThemePackagePattern.Match(matchingThemes[0].CssFilePath ?? string.Empty);
		if (match.Success) {
			packageName = match.Groups["package"].Value;
			error = null;
			return true;
		}
		error = $"Theme '{options.Id}' has no package in its cssFilePath, so its background cannot be bound; " +
			"nothing was changed.";
		return false;
	}
}
