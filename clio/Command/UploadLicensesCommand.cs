namespace Clio.Command.PackageCommand
{
	using System;
	using System.IO;
	using System.Text.Json;
	using Clio.Common;

	public class UploadLicensesCommand : RemoteCommand<UploadLicensesOptions>
	{

		public UploadLicensesCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/ServiceModel/LicenseService.svc/UploadLicenses";

		protected override string GetRequestData(UploadLicensesOptions options) {
			string fileBody = File.ReadAllText(options.FilePath);
			fileBody = fileBody.Replace("\"", "\\\"");
			return "{\"licData\":\"" + fileBody + "\"}";
		}

		protected override void ProceedResponse(string response, UploadLicensesOptions options) {
			var json = JsonDocument.Parse(response);
			bool isSuccess = json.RootElement.TryGetProperty("success", out var successProperty) &&
				successProperty.GetBoolean();
			if (isSuccess) {
				base.ProceedResponse(response, options);
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
					$"License not installed. ErrorCode: {errorCode}, Message: {errorMessage}");
			}
			if (response.ToLower().Contains("authentication failed")) {
				throw new LicenseInstallationException("License not installed: Authentication failed.");
			}
			throw new LicenseInstallationException("License not installed: Unknown error details");
				
		}
	}

	public class LicenseInstallationException : Exception
	{
		public LicenseInstallationException(string message) : base(message) { }
	}

}
