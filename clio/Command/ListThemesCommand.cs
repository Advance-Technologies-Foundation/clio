using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	/// <summary>
	/// Options for the <c>list-themes</c> command.
	/// </summary>
	[Verb("list-themes", Aliases = ["get-themes"],
		HelpText = "List the custom Creatio themes available on the target environment")]
	public class ListThemesOptions : RemoteCommandOptions
	{
	}

	/// <summary>
	/// Lists the custom Creatio themes available on the target environment by calling the native
	/// <c>ThemeService.svc/GetAvailableThemes</c> endpoint. Requires the <c>CanCustomizeBranding</c>
	/// license; callers without it receive an empty list rather than an error.
	/// </summary>
	public class ListThemesCommand : RemoteCommand<ListThemesOptions>
	{
		private static readonly JsonSerializerOptions ResponseJsonOptions = new() {
			PropertyNameCaseInsensitive = true
		};

		private readonly IServiceUrlBuilder _urlBuilder;

		/// <summary>
		/// Initializes a new instance of the <see cref="ListThemesCommand"/> class.
		/// </summary>
		public ListThemesCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
			IServiceUrlBuilder urlBuilder)
			: base(applicationClient, settings) {
			_urlBuilder = urlBuilder;
		}

		/// <inheritdoc />
		protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetAvailableThemes);

		/// <summary>
		/// The themes parsed from the most recent execution. Empty until the command has run successfully.
		/// </summary>
		public IReadOnlyList<ThemeDescriptor> Themes { get; private set; } = Array.Empty<ThemeDescriptor>();

		/// <summary>
		/// Fetches the available themes from the target environment.
		/// </summary>
		/// <param name="options">Command options carrying the connection and timeout settings.</param>
		/// <param name="themes">On success, the themes returned by the environment (possibly empty).</param>
		/// <param name="errorMessage">On failure, the server-provided message, if any.</param>
		/// <returns><c>true</c> when the catalog was read; <c>false</c> when the service reported a failure.</returns>
		public virtual bool TryGetAvailableThemes(ListThemesOptions options,
			out IReadOnlyList<ThemeDescriptor> themes, out string errorMessage) {
			string response = ApplicationClient.ExecutePostRequest(ServiceUri, GetRequestData(options),
				options.TimeOut, options.MaxAttempts, options.RetryDelay);
			return TryParseThemes(response, out themes, out errorMessage);
		}

		/// <inheritdoc />
		protected override void ExecuteRemoteCommand(ListThemesOptions options) {
			if (TryGetAvailableThemes(options, out IReadOnlyList<ThemeDescriptor> themes, out string errorMessage)) {
				Themes = themes;
				PrintThemes(themes);
				return;
			}
			CommandSuccess = false;
			Themes = Array.Empty<ThemeDescriptor>();
			Logger.WriteError(string.IsNullOrWhiteSpace(errorMessage)
				? "GetAvailableThemes returned success=false. Check the Creatio application logs for details."
				: $"GetAvailableThemes failed: {errorMessage}");
		}

		private void PrintThemes(IReadOnlyList<ThemeDescriptor> themes) {
			if (themes.Count == 0) {
				Logger.WriteInfo("No custom themes are available on this environment.");
				return;
			}
			IList<string[]> table = new List<string[]> {
				new[] { "Id", "Caption", "CssClassName", "CssFilePath" },
				new[] { string.Empty, string.Empty, string.Empty, string.Empty }
			};
			foreach (ThemeDescriptor theme in themes) {
				table.Add(new[] { theme.Id, theme.Caption, theme.CssClassName, theme.CssFilePath });
			}
			Logger.WriteLine();
			Logger.WriteInfo(TextUtilities.ConvertTableToString(table));
			Logger.WriteLine();
			Logger.WriteInfo($"Found {themes.Count} theme(s).");
		}

		private static bool TryParseThemes(string response, out IReadOnlyList<ThemeDescriptor> themes,
			out string errorMessage) {
			themes = Array.Empty<ThemeDescriptor>();
			errorMessage = null;
			if (string.IsNullOrWhiteSpace(response)) {
				return true;
			}
			ListThemesResponse parsed;
			try {
				parsed = JsonSerializer.Deserialize<ListThemesResponse>(response, ResponseJsonOptions);
			}
			catch (JsonException) {
				return true;
			}
			if (parsed is null) {
				return true;
			}
			if (parsed.Success == false) {
				errorMessage = parsed.ErrorInfo?.Message;
				return false;
			}
			themes = (parsed.Values ?? new List<ThemeDescriptor>())
				.Where(theme => theme is not null)
				.ToList();
			return true;
		}

		private sealed record ListThemesResponse
		{
			[JsonPropertyName("success")]
			public bool? Success { get; init; }

			[JsonPropertyName("errorInfo")]
			public ThemeServiceErrorInfo ErrorInfo { get; init; }

			[JsonPropertyName("values")]
			public List<ThemeDescriptor> Values { get; init; }
		}
	}

	/// <summary>
	/// A custom Creatio theme descriptor returned by the <c>list-themes</c> command.
	/// </summary>
	public sealed record ThemeDescriptor
	{
		/// <summary>Stable theme identifier.</summary>
		public string Id { get; init; }

		/// <summary>Human-readable theme caption.</summary>
		public string Caption { get; init; }

		/// <summary>CSS class name applied when the theme is active.</summary>
		public string CssClassName { get; init; }

		/// <summary>Relative path to the theme's compiled CSS file.</summary>
		public string CssFilePath { get; init; }
	}
}
