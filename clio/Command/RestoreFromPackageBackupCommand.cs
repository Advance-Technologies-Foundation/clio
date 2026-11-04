namespace Clio.Command
{
	using System;
	using System.Text.Json;
	using Clio.Common;
	using Clio.Package;
	using CommandLine;

	#region Class: RestoreFromPackageBackupOptions

	[Verb("restore-configuration", Aliases = new string[] { "restore", "rc" }, HelpText = "Restore configuration from last backup")]
	public class RestoreFromPackageBackupOptions : RemoteCommandOptions
	{

		#region Properties: Public

		/// <summary>
		/// Gets or sets whether the restore also installs the data bound to the packages in the backup.
		/// </summary>
		/// <remarks>
		/// The option keeps its historical name <c>skip-rollback-data</c>, but the service rolls the data back
		/// with or without it.
		/// </remarks>
		[Option('d', "skip-rollback-data", Required = false,
			HelpText = "Also install the data bound to the packages in the backup. Data is rolled back with or without this flag",
			Default = false)]
		public bool InstallPackageData {
			get; set;
		}

		[Option('f', "force", Required = false,
			HelpText = "Restore configuration without sql backward compatibility check", Default = false)]
		public bool IgnoreSqlScriptBackwardCompatibilityCheck {
			get; set;
		}

		#endregion

	}

	#endregion


	#region Class: RestoreFromPackageBackupCommand

	/// <summary>
	/// Restores the configuration from the last package backup and reports the outcome the
	/// <c>RestoreFromPackageBackup</c> service returns.
	/// </summary>
	/// <remarks>
	/// The service answers a failed restore with HTTP 200, <c>success:false</c> and a generic message, so the
	/// reason is only in the installation log; the command prints the part of that log written by this run.
	/// The request is sent once: a repeated restore of the same backup answers <c>success:true</c>, so a retry
	/// would report a failed restore as done.
	/// </remarks>
	internal class RestoreFromPackageBackupCommand : RemoteCommand<RestoreFromPackageBackupOptions>
	{

		private readonly IApplicationLogProvider _applicationLogProvider;
		private string _initialInstallationLog;

		public RestoreFromPackageBackupCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
			IApplicationLogProvider applicationLogProvider, ILogger logger)
			: base(applicationClient, settings) {
			_applicationLogProvider = applicationLogProvider;
			Logger = logger;
		}

		protected override string ServicePath => @"/ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup";

		protected override string GetRequestData(RestoreFromPackageBackupOptions options) {
			return "{\"installPackageData\": " + options.InstallPackageData.ToString().ToLower() +
				", \"ignoreSqlScriptBackwardCompatibilityCheck\": " +
				options.IgnoreSqlScriptBackwardCompatibilityCheck.ToString().ToLower() + "}";
		}

		protected override void ExecuteRemoteCommand(RestoreFromPackageBackupOptions options) {
			MaxAttempts = 1;
			_initialInstallationLog = ReadInstallationLog();
			base.ExecuteRemoteCommand(options);
		}

		protected override void ProceedResponse(string response, RestoreFromPackageBackupOptions options) {
			WriteInstallationLogOfThisRun();
			string errorMessage = GetErrorMessage(response);
			if (errorMessage != null) {
				Logger.WriteError($"Configuration restore from backup failed: {errorMessage}");
				CommandSuccess = false;
			}
		}

		private string ReadInstallationLog() =>
			_applicationLogProvider.GetInstallationLog(ApplicationClient, EnvironmentSettings) ?? string.Empty;

		// The log keeps every installation since the node started, so only the part after the log read before
		// the restore is this run's. When the log does not continue that read (the read failed, or the node
		// restarted), it is printed in full.
		private void WriteInstallationLogOfThisRun() {
			string completeLog = ReadInstallationLog();
			if (string.IsNullOrWhiteSpace(completeLog)) {
				Logger.WriteWarning("The installation log could not be read.");
				return;
			}
			bool isLogOfThisRun = _initialInstallationLog.Length > 0
				&& completeLog.StartsWith(_initialInstallationLog, StringComparison.Ordinal);
			string logOfThisRun = isLogOfThisRun ? completeLog.Substring(_initialInstallationLog.Length) : completeLog;
			if (!isLogOfThisRun) {
				Logger.WriteWarning("The installation log could not be attributed to this run, so it is printed in full.");
			} else if (string.IsNullOrWhiteSpace(logOfThisRun)) {
				Logger.WriteWarning("The installation log has no entries for this run.");
				return;
			}
			Logger.WriteLine("Installation log:");
			Logger.Write(logOfThisRun);
		}

		// Returns null only when the response confirms the restore with success:true.
		private static string GetErrorMessage(string response) {
			if (string.IsNullOrWhiteSpace(response)) {
				return "the server returned an empty response";
			}
			try {
				using JsonDocument json = JsonDocument.Parse(response);
				JsonElement root = json.RootElement;
				if (root.ValueKind != JsonValueKind.Object) {
					return $"unexpected response from server: {TextUtilities.SanitizeForDisplay(response)}";
				}
				if (root.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.True) {
					return null;
				}
				if (root.TryGetProperty("errorInfo", out JsonElement errorInfo)
					&& errorInfo.ValueKind == JsonValueKind.Object
					&& errorInfo.TryGetProperty("message", out JsonElement message)
					&& message.ValueKind == JsonValueKind.String
					&& !string.IsNullOrWhiteSpace(message.GetString())) {
					return TextUtilities.SanitizeForDisplay(message.GetString());
				}
				return "Unknown error";
			} catch (JsonException) {
				return $"unexpected response from server: {TextUtilities.SanitizeForDisplay(response)}";
			}
		}

	}

	#endregion

}
