namespace Clio.Command.PackageCommand
{
	using System;
	using System.Text.Json;
	using Clio.Common;

	public class DeletePackageCommand : RemoteCommand<DeletePkgOptions>
	{

		public DeletePackageCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/DeletePackage";

		protected override string GetRequestData(DeletePkgOptions options) {
			return "\"" + options.Name + "\"";
		}

		protected override void ProceedResponse(string response, DeletePkgOptions options) {
			if (string.IsNullOrWhiteSpace(response)) {
				Logger.WriteError("Empty response received from server.");
				CommandSuccess = false;
				return;
			}
			try {
				using var json = JsonDocument.Parse(response);
				bool isSuccess = json.RootElement.TryGetProperty("success", out var successProperty)
					&& successProperty.GetBoolean();
				if (isSuccess) {
					CommandSuccess = true;
					Logger.WriteInfo($"Package \"{options.Name}\" deleted successfully.");
					return;
				}
				string errorMessage = "Unknown error";
				if (json.RootElement.TryGetProperty("errorInfo", out var errorInfo)
					&& errorInfo.ValueKind != JsonValueKind.Null
					&& errorInfo.TryGetProperty("message", out var messageProperty)) {
					errorMessage = messageProperty.GetString() ?? "Unknown error";
				}
				Logger.WriteError($"Failed to delete package \"{options.Name}\": {errorMessage}");
				CommandSuccess = false;
			} catch (JsonException) {
				Logger.WriteError($"Unexpected response from server: {response}");
				CommandSuccess = false;
			}
		}

	}
}
