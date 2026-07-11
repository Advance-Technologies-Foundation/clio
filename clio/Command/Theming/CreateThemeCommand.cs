using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Theming;
using CommandLine;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the <c>create-theme</c> command.
/// </summary>
[Verb("create-theme", HelpText = "Create a custom Creatio theme directly on the target environment via the native ThemeService")]
[RequiresCreatioVersion(ThemeServiceRequirement.MinVersion)]
public class CreateThemeOptions : RemoteCommandOptions
{
	/// <summary>Theme id (<c>^[A-Za-z0-9_-]+$</c>, ≤100). When omitted, an auto-generated UUID v4 is used.</summary>
	[Option("id", Required = false,
		HelpText = "Theme id (^[A-Za-z0-9_-]+$, max 100). When omitted, an auto-generated UUID is used and reported back.")]
	public string Id { get; set; }

	/// <summary>Human-readable theme caption (≤250). When omitted, it is derived from <see cref="CssClassName"/>.</summary>
	[Option("caption", Required = false,
		HelpText = "Human-readable theme caption (max 250). When omitted, it is derived from css-class-name.")]
	public string Caption { get; set; }

	/// <summary>CSS class applied when the theme is active (<c>^[A-Za-z][A-Za-z0-9_-]*$</c>, ≤100); derived from <see cref="Caption"/> (lowercased and hyphenated) when omitted.</summary>
	[Option("css-class-name", Required = false,
		HelpText = "CSS class applied when the theme is active (^[A-Za-z][A-Za-z0-9_-]*$, max 100); derived from --caption (lowercased and hyphenated) when omitted.")]
	public string CssClassName { get; set; }

	/// <summary>Inline theme CSS. Mutually exclusive with <c>--css-content-file</c>.</summary>
	[Option("css-content", Required = false,
		HelpText = "Inline theme CSS (mutually exclusive with --css-content-file).")]
	public string CssContent { get; set; }

	/// <summary>Path to a UTF-8 CSS file. Mutually exclusive with <c>--css-content</c>.</summary>
	[Option("css-content-file", Required = false,
		HelpText = "Path to a UTF-8 CSS file (mutually exclusive with --css-content).")]
	public string CssContentFile { get; set; }

	/// <summary>Owning package name; omitted means the environment's CurrentPackageId system setting.</summary>
	[Option("package-name", Required = false,
		HelpText = "Owning package name. When omitted, the environment's CurrentPackageId system setting is used.")]
	public string PackageName { get; set; }
}

/// <summary>
/// Creates a custom Creatio theme on the target environment by calling the native
/// <c>ThemeService.svc/CreateTheme</c> endpoint. Requires the <c>CanCustomizeBranding</c> license and the
/// <c>CanManageThemes</c> system operation on the caller.
/// </summary>
public class CreateThemeCommand : RemoteCommand<CreateThemeOptions>
{
	private readonly IServiceUrlBuilder _urlBuilder;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreateThemeCommand"/> class.
	/// </summary>
	public CreateThemeCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder urlBuilder, IFileSystem fileSystem)
		: base(applicationClient, settings) {
		_urlBuilder = urlBuilder;
		_fileSystem = fileSystem;
	}

	/// <inheritdoc />
	protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.CreateTheme);

	/// <summary>
	/// Creates the theme on the target environment and reports the effective id (the supplied id or an
	/// auto-generated one). Resolves the CSS and validates the fields before any HTTP call.
	/// </summary>
	/// <param name="options">Create options carrying the theme fields and connection settings.</param>
	/// <param name="createdId">On success, the effective theme id (supplied or auto-generated).</param>
	/// <param name="errorMessage">On failure, the validation or server-provided message.</param>
	/// <returns><c>true</c> when the theme was created; otherwise <c>false</c>.</returns>
	public virtual bool TryCreateTheme(CreateThemeOptions options, out string createdId, out string errorMessage) {
		createdId = null;
		errorMessage = null;
		string id = string.IsNullOrWhiteSpace(options.Id) ? Guid.NewGuid().ToString("D") : options.Id;
		if (!ThemeRequestBuilder.TryResolveCssContent(_fileSystem, options.CssContent, options.CssContentFile,
				out string cssContent, out errorMessage)) {
			return false;
		}
		if (!ThemeParameterValidator.TryResolveCssClassName(options.CssClassName, options.Caption, out string cssClassName, out errorMessage)) {
			return false;
		}
		string caption = string.IsNullOrWhiteSpace(options.Caption)
			? ThemeRequestBuilder.DeriveCaptionFromCssClassName(cssClassName)
			: options.Caption;
		CreateThemeRequest request = new() {
			Id = id,
			Caption = caption,
			CssClassName = cssClassName,
			CssContent = cssContent
		};
		if (!ThemeRequestBuilder.TryValidateRequest(request, out errorMessage)) {
			return false;
		}
		if (!TryResolvePackageUId(options.PackageName, out string packageUId, out errorMessage)) {
			return false;
		}
		string requestData = JsonSerializer.Serialize(request with { PackageUId = packageUId });
		string response = ApplicationClient.ExecutePostRequest(ServiceUri, requestData,
			options.TimeOut, options.MaxAttempts, options.RetryDelay);
		if (ThemeServiceResponseParser.TryGetFailure(response, out string failure)) {
			errorMessage = ThemeServiceResponseParser.DescribeFailure("CreateTheme", failure);
			return false;
		}
		createdId = id;
		return true;
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(CreateThemeOptions options) {
		if (TryCreateTheme(options, out string createdId, out string errorMessage)) {
			Logger.WriteInfo($"Created theme '{createdId}'.");
			return;
		}
		CommandSuccess = false;
		Logger.WriteError(errorMessage);
	}

	private bool TryResolvePackageUId(string packageName, out string packageUId, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(packageName)) {
			packageUId = null;
			return true;
		}
		(string uId, string queryError) = PageSchemaMetadataHelper.QueryPackageUId(ApplicationClient, _urlBuilder, packageName);
		if (queryError is not null) {
			packageUId = null;
			error = queryError;
			return false;
		}
		packageUId = uId;
		return true;
	}

	private sealed record CreateThemeRequest : ThemeRequest
	{
		[JsonPropertyName("packageUId")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string PackageUId { get; init; }
	}
}
