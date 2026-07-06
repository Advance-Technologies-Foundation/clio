using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the <c>update-theme</c> command.
/// </summary>
[Verb("update-theme", HelpText = "Overwrite an existing custom Creatio theme on the target environment via the native ThemeService")]
public class UpdateThemeOptions : RemoteCommandOptions
{
	/// <summary>Id of the existing theme to overwrite (required).</summary>
	[Option("id", Required = true, HelpText = "Id of the existing theme to overwrite")]
	public string Id { get; set; }

	/// <summary>Human-readable theme caption (required, ≤250).</summary>
	[Option("caption", Required = true, HelpText = "Human-readable theme caption (max 250)")]
	public string Caption { get; set; }

	/// <summary>CSS class applied when the theme is active (required, <c>^[A-Za-z][A-Za-z0-9_-]*$</c>, ≤100).</summary>
	[Option("css-class-name", Required = true,
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
}

/// <summary>
/// Overwrites an existing custom Creatio theme on the target environment by calling the native
/// <c>ThemeService.svc/UpdateTheme</c> endpoint. The theme is located by <c>id</c> and rewritten in its
/// current package (the package cannot be changed). Requires the <c>CanCustomizeBranding</c> license and the
/// <c>CanManageThemes</c> system operation. This is a full overwrite — caption, css-class-name, and CSS are all required.
/// </summary>
public class UpdateThemeCommand : RemoteCommand<UpdateThemeOptions>
{
	private readonly IServiceUrlBuilder _urlBuilder;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="UpdateThemeCommand"/> class.
	/// </summary>
	public UpdateThemeCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder urlBuilder, IFileSystem fileSystem)
		: base(applicationClient, settings) {
		_urlBuilder = urlBuilder;
		_fileSystem = fileSystem;
	}

	/// <inheritdoc />
	protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.UpdateTheme);

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(UpdateThemeOptions options) {
		if (!ThemeRequestBuilder.TryResolveCssContent(_fileSystem, options.CssContent, options.CssContentFile,
				out string cssContent, out string error)) {
			CommandSuccess = false;
			Logger.WriteError(error);
			return;
		}
		ThemeRequest request = new() {
			Id = options.Id,
			Caption = options.Caption,
			CssClassName = options.CssClassName,
			CssContent = cssContent
		};
		if (!ThemeRequestBuilder.TryValidateRequest(request, out error)) {
			CommandSuccess = false;
			Logger.WriteError(error);
			return;
		}
		string requestData = JsonSerializer.Serialize(request);
		string response = ApplicationClient.ExecutePostRequest(ServiceUri, requestData,
			options.TimeOut, options.MaxAttempts, options.RetryDelay);
		ProceedResponse(response, options);
	}

	/// <inheritdoc />
	protected override void ProceedResponse(string response, UpdateThemeOptions options) {
		if (ThemeServiceResponseParser.TryGetFailure(response, out string message)) {
			CommandSuccess = false;
			Logger.WriteError(ThemeServiceResponseParser.DescribeFailure("UpdateTheme", message));
		}
	}

}
