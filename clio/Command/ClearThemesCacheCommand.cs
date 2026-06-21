using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	/// <summary>
	/// Options for the <c>clear-themes-cache</c> command.
	/// </summary>
	[Verb("clear-themes-cache", Aliases = ["flush-themes"],
		HelpText = "Refresh the Creatio theme catalog cache")]
	public class ClearThemesCacheOptions : RemoteCommandOptions
	{
	}

	/// <summary>
	/// Refreshes the Creatio theme catalog cache on the target environment by calling the native
	/// <c>ThemeService.svc/ClearThemesCache</c> endpoint. Requires the <c>CanCustomizeBranding</c> license
	/// and the <c>CanManageThemes</c> system operation on the caller.
	/// </summary>
	public class ClearThemesCacheCommand : RemoteCommand<ClearThemesCacheOptions>
	{
		private static readonly JsonSerializerOptions ResponseJsonOptions = new() {
			PropertyNameCaseInsensitive = true
		};

		private readonly IServiceUrlBuilder _urlBuilder;

		/// <summary>
		/// Initializes a new instance of the <see cref="ClearThemesCacheCommand"/> class.
		/// </summary>
		public ClearThemesCacheCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
			IServiceUrlBuilder urlBuilder)
			: base(applicationClient, settings) {
			_urlBuilder = urlBuilder;
		}

		/// <inheritdoc />
		protected override string ServicePath => _urlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearThemesCache);

		/// <inheritdoc />
		protected override void ProceedResponse(string response, ClearThemesCacheOptions options) {
			// ThemeService.ClearThemesCache returns a BaseResponse: { "success": bool, "errorInfo": {...} }.
			// Treat an explicit success=false as a failure (and surface the message); tolerate an empty or
			// non-JSON body as success so the command does not report a false negative if the contract evolves.
			if (string.IsNullOrWhiteSpace(response)) {
				return;
			}
			ClearThemesCacheResponse parsed;
			try {
				parsed = JsonSerializer.Deserialize<ClearThemesCacheResponse>(response, ResponseJsonOptions);
			}
			catch (JsonException) {
				return;
			}
			if (parsed?.Success == false) {
				CommandSuccess = false;
				string message = parsed.ErrorInfo?.Message;
				Logger.WriteError(string.IsNullOrWhiteSpace(message)
					? "ClearThemesCache returned success=false. Check the Creatio application logs for details."
					: $"ClearThemesCache failed: {message}");
			}
		}

		private sealed record ClearThemesCacheResponse
		{
			public bool? Success { get; init; }

			public ThemeServiceErrorInfo ErrorInfo { get; init; }
		}

		private sealed record ThemeServiceErrorInfo
		{
			public string ErrorCode { get; init; }

			public string Message { get; init; }
		}
	}
}
