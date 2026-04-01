namespace Clio.Command.PackageCommand
{
	using System;
	using System.Linq;
	using System.Text.Json;
	using Clio.Common;
	using Clio.Package;

	public class DeletePackageCommand : RemoteCommand<DeletePkgOptions>
	{
		private readonly IApplicationPackageListProvider _packageListProvider;

		public DeletePackageCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
			IApplicationPackageListProvider packageListProvider = null)
			: base(applicationClient, settings) {
			_packageListProvider = packageListProvider;
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

				string errorMessage = "Unknown error";
				if (!isSuccess && json.RootElement.TryGetProperty("errorInfo", out var errorInfo)
					&& errorInfo.ValueKind != JsonValueKind.Null
					&& errorInfo.TryGetProperty("message", out var messageProperty)) {
					errorMessage = messageProperty.GetString() ?? "Unknown error";
				}

				// Verify actual deletion by checking if package still exists
				bool packageStillExists = VerifyPackageRemoval(options.Name);

				if (!packageStillExists) {
					// Package was successfully deleted, regardless of server response
					CommandSuccess = true;
					if (isSuccess) {
						Logger.WriteInfo($"Package \"{options.Name}\" deleted successfully.");
					} else {
						Logger.WriteWarning($"Package \"{options.Name}\" deleted successfully (server reported: {errorMessage}).");
					}
					return;
				}

				// Package still exists after deletion attempt
				if (isSuccess) {
					// Server claims success but package still exists
					Logger.WriteError($"Server reported success but package \"{options.Name}\" still exists.");
				} else {
					// Both server and verification indicate failure
					Logger.WriteError($"Failed to delete package \"{options.Name}\": {errorMessage}");
				}
				CommandSuccess = false;

			} catch (JsonException) {
				Logger.WriteError($"Unexpected response from server: {response}");
				CommandSuccess = false;
			}
		}

		private bool VerifyPackageRemoval(string packageName) {
			if (_packageListProvider == null) {
				// Cannot verify - assume server response is correct
				return true;
			}

			try {
				var packages = _packageListProvider.GetPackages();
				bool exists = packages.Any(p =>
					string.Equals(p.Descriptor?.Name, packageName, StringComparison.OrdinalIgnoreCase));
				return exists;
			} catch (Exception ex) {
				Logger.WriteWarning($"Unable to verify package removal: {ex.Message}");
				// If verification fails, assume package still exists to be safe
				return true;
			}
		}

	}
}
