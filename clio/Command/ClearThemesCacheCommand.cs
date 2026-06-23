using System.Text.Json;
using System.Text.Json.Serialization;
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
			[JsonPropertyName("success")]
			public bool? Success { get; init; }

			[JsonPropertyName("errorInfo")]
			public ThemeServiceErrorInfo ErrorInfo { get; init; }
		}

		private sealed record ThemeServiceErrorInfo
		{
			[JsonPropertyName("errorCode")]
			public string ErrorCode { get; init; }

			[JsonPropertyName("message")]
			public string Message { get; init; }
		}
	}
}
