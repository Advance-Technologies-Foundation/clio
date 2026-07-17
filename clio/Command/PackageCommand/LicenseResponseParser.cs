namespace Clio.Command.PackageCommand
{
	using System;
	using System.Text.Json;

	/// <summary>
	/// Shared success/failure parsing for Creatio license-service JSON responses
	/// (LicenseService.svc/UploadLicenses, LicenseManagerProxyService.svc/SaveLicenseData).
	/// </summary>
	internal static class LicenseResponseParser
	{
		/// <summary>
		/// Throws <see cref="LicenseInstallationException"/> when the response is not a success.
		/// Checks the raw "authentication failed" marker before attempting JSON parsing, since an
		/// expired session redirects to an HTML/plain-text login page rather than returning JSON.
		/// </summary>
		public static void EnsureSuccess(string response, string failureMessagePrefix) {
			if (response.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)) {
				throw new LicenseInstallationException($"{failureMessagePrefix}: Authentication failed.");
			}

			using JsonDocument json = ParseOrThrow(response, failureMessagePrefix);

			bool isSuccess = json.RootElement.TryGetProperty("success", out var successProperty) &&
				successProperty.GetBoolean();
			if (isSuccess) {
				return;
			}

			if (json.RootElement.TryGetProperty("errorInfo", out var errorInfo)) {
				var errorMessage = errorInfo.TryGetProperty("message", out var messageProperty)
					? messageProperty.GetString()
					: "Unknown error message";
				var errorCode = errorInfo.TryGetProperty("errorCode", out var codeProperty)
					? codeProperty.GetString()
					: "UNKNOWN_CODE";
				throw new LicenseInstallationException(
					$"{failureMessagePrefix}. ErrorCode: {errorCode}, Message: {errorMessage}");
			}

			throw new LicenseInstallationException($"{failureMessagePrefix}: Unknown error details");
		}

		private static JsonDocument ParseOrThrow(string response, string failureMessagePrefix) {
			try {
				return JsonDocument.Parse(response);
			}
			catch (JsonException) {
				throw new LicenseInstallationException($"{failureMessagePrefix}: Unknown error details");
			}
		}
	}
}
