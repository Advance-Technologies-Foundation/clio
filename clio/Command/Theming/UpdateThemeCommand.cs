using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
		HelpText = "Id of an image uploaded with upload-image, set as the theme background. Pass at most one of " +
			"--background-image-id, --background-color and --remove-background; each needs Creatio 10.2.96 or later.")]
	public string BackgroundImageId { get; set; }

	[Option("background-color", Required = false, HelpText = "Solid theme background color: #RGB, #RRGGBB or #RRGGBBAA.")]
	public string BackgroundColor { get; set; }

	[Option("remove-background", Required = false,
		HelpText = "Remove the theme's own background; the image binding stays in the package.")]
	public bool RemoveBackground { get; set; }

	[Option("keep-icon-background", Required = false,
		HelpText = "Keep the UsePanelIconBackground feature as is; without it, a background turns it off for All employees.")]
	public bool KeepIconBackground { get; set; }

	[RequiresCreatioVersion(ThemeServiceRequirement.BackgroundMinVersion, Hint = ThemeServiceRequirement.BackgroundHint)]
	public bool HasBackgroundArgument => GetBackgroundArguments().IsPresent;

	internal ThemeBackgroundArguments GetBackgroundArguments() =>
		new(BackgroundImageId, BackgroundColor, KeepIconBackground, RemoveBackground);
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
		ThemeBackgroundArguments background = options.GetBackgroundArguments();
		if (!TryBuildRequest(options, background, out ThemeRequest request, out string packageName, out string error)) {
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
		if (background.IsPresent) {
			Logger.WriteInfo(background.Remove
				? "Removed the theme background."
				: $"Theme background: {request.Background.Describe()}.");
			Logger.WriteWarnings(_backgroundWriter.Complete(background, options.Id, packageName).Warnings);
		}
	}

	private bool TryBuildRequest(UpdateThemeOptions options, ThemeBackgroundArguments background,
		out ThemeRequest request, out string packageName, out string error) {
		request = null;
		packageName = null;
		bool partial = background.IsPresent;
		bool hasCss = !string.IsNullOrWhiteSpace(options.CssContent) || !string.IsNullOrWhiteSpace(options.CssContentFile);
		string cssContent = null;
		if (!background.TryValidate(out error)
			|| ((!partial || hasCss) && !ThemeRequestBuilder.TryResolveCssContent(_fileSystem, options.CssContent,
				options.CssContentFile, out cssContent, out error))) {
			return false;
		}
		request = new ThemeRequest {
			Id = options.Id,
			Caption = partial && string.IsNullOrWhiteSpace(options.Caption) ? null : options.Caption,
			CssClassName = partial && string.IsNullOrWhiteSpace(options.CssClassName) ? null : options.CssClassName,
			CssContent = cssContent,
			Background = background.ToBackground()
		};
		return ThemeRequestBuilder.TryValidateRequest(request, out error, partial)
			&& (!background.HasImageId || TryResolveThemePackage(options, out packageName, out error))
			&& _backgroundWriter.TryConfirmImage(background, out error);
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
		Guid themeId = Guid.Parse(options.Id);
		ThemeDescriptor theme = themes.FirstOrDefault(candidate =>
			Guid.TryParse(candidate.Id, out Guid id) && id == themeId);
		if (theme is null) {
			string caveat = themes.Count == 0 ? " " + ThemeCatalogMessages.EmptyCatalogLicenseCaveat : string.Empty;
			error = $"No theme with id '{options.Id}' is on the environment (see list-themes); nothing was changed." + caveat;
			return false;
		}
		Match match = ThemePackagePattern.Match(theme.CssFilePath ?? string.Empty);
		if (match.Success) {
			packageName = match.Groups["package"].Value;
			error = null;
			return true;
		}
		error = $"Theme '{options.Id}' has no package in its cssFilePath, so its background image cannot be bound; " +
			"nothing was changed.";
		return false;
	}
}
