using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Theming;
using CommandLine;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.Theming;

/// <summary>
/// Options for the <c>get-theme</c> command.
/// </summary>
[Verb("get-theme", HelpText = "Read the content (theme.css) and metadata of a custom Creatio theme from the target environment")]
[RequiresCreatioVersion(ThemeServiceRequirement.MinVersion)]
public class GetThemeOptions : RemoteCommandOptions
{
	/// <summary>Id of the theme to read (required).</summary>
	[Option("id", Required = true, HelpText = "Id of the theme to read")]
	public string Id { get; set; }

	/// <summary>
	/// Optional path to write the theme CSS to. When set, <c>cssContent</c> is omitted from the response.
	/// </summary>
	[Option("output-file", Required = false,
		HelpText = "Path to write the theme CSS to. When set, cssContent is omitted from the response.")]
	public string OutputFile { get; set; }
}

/// <summary>
/// Result envelope of the <c>get-theme</c> command, printed as JSON by the CLI and returned as the
/// structured result of the <c>get-theme</c> MCP tool.
/// </summary>
public sealed record GetThemeResponse
{
	/// <summary>Whether the theme content was read successfully.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The theme id as reported by the environment's theme catalog. Omitted on failure.</summary>
	[JsonPropertyName("id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Id { get; init; }

	/// <summary>The theme caption. Omitted on failure.</summary>
	[JsonPropertyName("caption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Caption { get; init; }

	/// <summary>The CSS class applied when the theme is active. Omitted on failure.</summary>
	[JsonPropertyName("cssClassName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string CssClassName { get; init; }

	/// <summary>Relative path the environment serves the theme CSS from. Omitted on failure.</summary>
	[JsonPropertyName("cssFilePath")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string CssFilePath { get; init; }

	/// <summary>
	/// The theme CSS content, byte-for-byte as served by the environment (an existing theme with empty
	/// content yields an empty string). Omitted when <c>output-file</c> was used or on failure.
	/// </summary>
	[JsonPropertyName("cssContent")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string CssContent { get; init; }

	/// <summary>Length of the CSS content in characters; reported even when the content itself was written to a file. Omitted on failure.</summary>
	[JsonPropertyName("cssContentLength")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? CssContentLength { get; init; }

	/// <summary>The failure message; omitted on success.</summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Creates a failure result carrying the diagnostic message.</summary>
	public static GetThemeResponse Failure(string error) {
		return new GetThemeResponse {
			Success = false,
			Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error
		};
	}
}

/// <summary>
/// Reads the content (<c>theme.css</c>) and metadata of a custom Creatio theme from the target
/// environment. The theme is resolved by id through the environment's theme catalog and its CSS is read
/// from the catalog-reported <c>cssFilePath</c>. The catalog is re-read on every call, so the returned
/// content always reflects the theme's current state. Requires the <c>CanCustomizeBranding</c> license;
/// callers without it see an empty catalog and therefore a not-found result.
/// </summary>
public class GetThemeCommand : Command<GetThemeOptions>
{
	private readonly IThemeCatalog _themeCatalog;
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _urlBuilder;
	private readonly IoFileSystem _ioFileSystem;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="GetThemeCommand"/> class.
	/// </summary>
	public GetThemeCommand(IThemeCatalog themeCatalog, IApplicationClient applicationClient,
		IServiceUrlBuilder urlBuilder, IoFileSystem ioFileSystem, ILogger logger) {
		_themeCatalog = themeCatalog;
		_applicationClient = applicationClient;
		_urlBuilder = urlBuilder;
		_ioFileSystem = ioFileSystem;
		_logger = logger;
	}

	/// <summary>
	/// Reads the theme's metadata and CSS content without writing to the logger, so a caller that needs the
	/// result as data (the MCP tool) gets the envelope with no log-channel noise.
	/// </summary>
	/// <param name="options">The command options carrying the theme id and the optional output file.</param>
	/// <param name="response">The result envelope; on failure it carries only <c>success:false</c> and <c>error</c>.</param>
	/// <returns><c>true</c> when the theme was found and its content read; otherwise <c>false</c>.</returns>
	public virtual bool TryGetTheme(GetThemeOptions options, out GetThemeResponse response) {
		try {
			if (!ThemeParameterValidator.TryValidateId(options.Id, out string idError)) {
				response = GetThemeResponse.Failure(idError);
				return false;
			}
			string resolvedOutputPath = null;
			if (!string.IsNullOrWhiteSpace(options.OutputFile)) {
				string pathError;
				(resolvedOutputPath, pathError) = OutputPathConfinement.Resolve(_ioFileSystem, options.OutputFile);
				if (pathError != null) {
					response = GetThemeResponse.Failure(pathError);
					return false;
				}
			}
			if (!TryResolveTheme(options, out ThemeDescriptor theme, out string resolveError)) {
				response = GetThemeResponse.Failure(resolveError);
				return false;
			}
			if (!TryFetchCss(options, theme, out string cssContent, out string fetchError)) {
				response = GetThemeResponse.Failure(fetchError);
				return false;
			}
			response = new GetThemeResponse {
				Success = true,
				Id = theme.Id,
				Caption = theme.Caption,
				CssClassName = theme.CssClassName,
				CssFilePath = TextUtilities.SanitizeForDisplay(theme.CssFilePath ?? string.Empty),
				CssContentLength = cssContent.Length
			};
			if (resolvedOutputPath != null) {
				OutputPathConfinement.WriteAtomic(_ioFileSystem, resolvedOutputPath, cssContent);
			} else {
				response = response with { CssContent = cssContent };
			}
			return true;
		}
		catch (Exception ex) {
			response = GetThemeResponse.Failure(ex.Message);
			return false;
		}
	}

	/// <inheritdoc />
	public override int Execute(GetThemeOptions options) {
		bool success = TryGetTheme(options, out GetThemeResponse response);
		_logger.WriteInfo(JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}

	private bool TryResolveTheme(GetThemeOptions options, out ThemeDescriptor theme, out string error) {
		theme = null;
		ListThemesOptions listOptions = ListThemesOptions.From(options);
		if (!_themeCatalog.TryGetAvailableThemes(listOptions, out IReadOnlyList<ThemeDescriptor> themes,
				out string catalogError)) {
			error = ThemeServiceResponseParser.DescribeFailure("GetAvailableThemes", catalogError);
			return false;
		}
		if (themes.Count == 0) {
			error = $"Theme '{options.Id}' was not found and no custom themes are listed on this environment. " +
				ThemeCatalogMessages.EmptyCatalogLicenseCaveat;
			return false;
		}
		List<ThemeDescriptor> matchingThemes = themes
			.Where(descriptor => string.Equals(descriptor.Id, options.Id, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (matchingThemes.Count > 1) {
			error = $"Theme id '{options.Id}' matches more than one theme on the environment; " +
				"the catalog is inconsistent. Run 'clio list-themes' to inspect it.";
			return false;
		}
		if (matchingThemes.Count == 0) {
			error = $"Theme '{options.Id}' was not found. Run 'clio list-themes' to see the available theme ids.";
			return false;
		}
		theme = matchingThemes[0];
		error = null;
		return true;
	}

	private bool TryFetchCss(GetThemeOptions options, ThemeDescriptor theme, out string cssContent,
		out string error) {
		cssContent = null;
		if (string.IsNullOrWhiteSpace(theme.CssFilePath)) {
			error = $"Theme '{theme.Id}' has no CSS file path in the theme catalog; there is no content to read.";
			return false;
		}
		string url = _urlBuilder.Build(theme.CssFilePath);
		string content = _applicationClient.ExecuteGetRequest(url, options.TimeOut, options.MaxAttempts,
			options.RetryDelay) ?? string.Empty;
		string trimmed = content.TrimStart().TrimStart('\uFEFF').TrimStart();
		if (trimmed.StartsWith('<')) {
			error = $"The environment returned an HTML page instead of the theme CSS for '{theme.Id}'. " +
				"The CSS file may be missing on the server or the request was redirected.";
			return false;
		}
		if (Encoding.UTF8.GetByteCount(content) > ThemeParameterValidator.MaxCssContentBytes) {
			error = $"The theme CSS for '{theme.Id}' exceeds the 1 MiB content limit and cannot be read. " +
				"Themes managed through clio are capped at 1 MiB; the served file is not a clio-managed theme CSS.";
			return false;
		}
		cssContent = content;
		error = null;
		return true;
	}
}
