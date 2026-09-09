namespace Clio.Package
{
	using Clio.Common;
	using Clio.Common.Responses;
	using Clio.WebApplication;
	using Newtonsoft.Json;
	using System.IO;
	using System.Text;
	using System.Threading.Tasks;
	using System.Threading;
	using System;
	using System.Linq;
	using System.Collections.Generic;

	public abstract class BasePackageInstaller {

		#region Constants: Private

		private const string InstallWithOptionsUrl = @"/rest/ClioPackageInstallerService/Install";
		private const string UploadUrl = @"/ServiceModel/PackageInstallerService.svc/UploadPackage";
		private const string DefLogFileName = "cliolog.txt";
		private const string InvalidGZipArchiveExceptionTypeName = "InvalidGZipArchiveException";
		private readonly IApplicationLogProvider _applicationLogProvider;

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		protected readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IPackageArchiver _packageArchiver;
		private readonly ISqlScriptExecutor _scriptExecutor;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly IPackageLockManager _packageLockManager;
		protected readonly ILogger _logger;
		private readonly IApplication _application;
		private string _reportPath;

		#endregion

		#region Fields: Protected

		protected readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public BasePackageInstaller(IApplicationLogProvider applicationLogProvider, EnvironmentSettings environmentSettings,
			IApplicationClientFactory applicationClientFactory, IApplication application,
			IPackageArchiver packageArchiver, ISqlScriptExecutor scriptExecutor,
			IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem, ILogger logger, IPackageLockManager packageLockManager) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			application.CheckArgumentNull(nameof(application));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			scriptExecutor.CheckArgumentNull(nameof(scriptExecutor));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_applicationLogProvider = applicationLogProvider;
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
			_application = application;
			_packageArchiver = packageArchiver;
			_scriptExecutor = scriptExecutor;
			_serviceUrlBuilder = serviceUrlBuilder;
			_fileSystem = fileSystem;
			_logger = logger;
			_packageLockManager = packageLockManager;
		}

		#endregion

		#region Properties: Protected

		protected abstract string InstallUrl { get; }

		protected abstract string BackupUrl { get; }

		/// <summary>
		/// Gets whether invalid GZip archive failures should be surfaced as a dedicated exception.
		/// </summary>
		protected virtual bool ThrowInvalidGZipArchiveInstallException => false;

		public bool CheckLogsOnSuccessMessage {
			get {
				return GlobalContext.FailOnError;
			}
		}


		#endregion

		#region Methods: Private

		protected string GetCompleteUrl(string url, EnvironmentSettings environmentSettings) =>
			_serviceUrlBuilder.Build(url, environmentSettings);

		private bool DeveloperModeEnabled(EnvironmentSettings environmentSettings) =>
			environmentSettings.DeveloperModeEnabled.HasValue && environmentSettings.DeveloperModeEnabled.Value;

		private IOwnedApplicationClient CreateApplicationClient(EnvironmentSettings environmentSettings) =>
			_applicationClientFactory.CreateOwnedClient(environmentSettings);

		private void UnlockMaintainerPackageInternal(EnvironmentSettings environmentSettings) {
			_packageLockManager.Unlock(Enumerable.Empty<string>());
		}

		private void SaveLogFile(string logText, string reportPath) {
			if (reportPath != null && !string.IsNullOrWhiteSpace(logText)) {
				if (_fileSystem.ExistsFile(reportPath)) {
					_fileSystem.DeleteFile(reportPath);
				} else if (_fileSystem.ExistsDirectory(reportPath)) {
					reportPath = Path.Combine(reportPath, DefLogFileName);
				}
				_fileSystem.WriteAllTextToFile(reportPath, logText, Encoding.UTF8);
			}
		}

		private string UploadPackage(string filePath, EnvironmentSettings environmentSettings) {
			_logger.WriteLine("Uploading...");
			FileInfo fileInfo = new FileInfo(filePath);
			string packageName = fileInfo.Name;
			using IOwnedApplicationClient applicationClient = CreateApplicationClient(environmentSettings);
			applicationClient.UploadFile(GetCompleteUrl(UploadUrl, environmentSettings), filePath);
			_logger.WriteLine("Uploaded");
			return packageName;
		}

		private bool CreateBackupPackage(string packageCode, string filePath,
			EnvironmentSettings environmentSettings) {
			try {
				_logger.WriteLine("Backup process...");
				FileInfo fileInfo = new FileInfo(filePath);
				string zipPackageName = fileInfo.Name;
				using IOwnedApplicationClient applicationClient = CreateApplicationClient(environmentSettings);
				applicationClient.ExecutePostRequest(GetCompleteUrl(BackupUrl, environmentSettings), "{\"Name\":\"" + packageCode +
						"\",\"Code\":\"" + packageCode +
						"\",\"ZipPackageName\":\"" + zipPackageName +
						"\",\"LastUpdate\":0}")
					;
				_logger.WriteLine("Backup completed");
				return true;
			} catch {
				return false;
			}
		}

		protected virtual string GetInstallLog(EnvironmentSettings environmentSettings) {
			return _applicationLogProvider.GetInstallationLog(environmentSettings);
		}

		private string GetLogDiff(string currentLog, string completeLog) {
			currentLog ??= string.Empty;
			return string.IsNullOrWhiteSpace(completeLog)
				? string.Empty
				: ((completeLog.Length > currentLog.Length) ? completeLog.Substring(currentLog.Length) : String.Empty);
		}

		private string ListenForLogs(CancellationToken cancellationToken, EnvironmentSettings environmentSettings,
			string initialLogContent) {
			var currentLogContent = initialLogContent ?? string.Empty;
			while (!cancellationToken.IsCancellationRequested) {
				try {
					var completeLog = GetInstallLog(environmentSettings);
					var output = GetLogDiff(currentLogContent, completeLog);
					if (!string.IsNullOrWhiteSpace(output)) {
						_logger.Write(output);
						currentLogContent = completeLog;
						if (!string.IsNullOrWhiteSpace(_reportPath))
							SaveLogFile(currentLogContent, _reportPath);
					}
					cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(3));
				} catch {}
			}
			return currentLogContent;
		}

		/// <summary>
		/// Logs every schema the platform skipped because it was edited on the environment, plus a closing
		/// summary. These are warnings: the installation itself ran, the element was simply left alone.
		/// </summary>
		/// <param name="currentInstallLog">Installation log produced by the current run.</param>
		private void ReportLocallyModifiedSchemas(string currentInstallLog) {
			IReadOnlyList<string> lines = InstallLogAnalyzer.GetLocallyModifiedSchemaLines(currentInstallLog);
			if (lines.Count == 0) {
				return;
			}
			foreach (string line in lines) {
				_logger.WriteWarning(line);
			}
			IReadOnlyList<string> names = InstallLogAnalyzer.GetLocallyModifiedSchemaNames(currentInstallLog);
			string schemaList = names.Count > 0 ? string.Join(", ", names) : "see the messages above";
			_logger.WriteWarning(
				$"{lines.Count} schema(s) skipped because they were modified locally: {schemaList}. " +
				"Resolve the conflict on the environment and mark the elements as unchanged to install them.");
		}

		/// <summary>
		/// Determines whether the log window handed to the classification really is this run's output.
		/// </summary>
		/// <param name="initialInstallLog">Log read from the environment before the installation started.</param>
		/// <param name="completeInstallLog">Log read from the environment after the installation returned.</param>
		/// <returns><c>true</c> when the final log demonstrably extends the initial one.</returns>
		/// <remarks>
		/// <see cref="GetLogDiff"/> is a length subtraction, so it only yields this run's output when the
		/// final log actually starts with the initial one. Two things break that. First,
		/// <see cref="IApplicationLogProvider"/> swallows every failure of the log request and answers with
		/// an empty string, and an empty initial log makes the subtraction return the environment's WHOLE
		/// shared history. Second, the endpoint sometimes hands back an HTML error page ("500 - Internal
		/// Server Error") in place of the log, and that body reaches clio as log content rather than as a
		/// failure - observed repeatedly on Creatio 10.1.725 while measuring GH-1299, including during the
		/// runs that verified this fix. Either shape makes the classification read a completion marker and
		/// a skip line that this run never wrote. Refusing the downgrade then keeps the pre-classification
		/// outcome, which is the safe direction.
		/// </remarks>
		private static bool IsLogWindowOfThisRun(string initialInstallLog, string completeInstallLog) =>
			!string.IsNullOrEmpty(initialInstallLog)
			&& completeInstallLog != null
			&& completeInstallLog.StartsWith(initialInstallLog, StringComparison.Ordinal);

		/// <summary>
		/// Decides whether a failure reported by the installation service is in fact a completed
		/// installation that carries no evidence of anything failing beyond a locally modified schema.
		/// </summary>
		/// <param name="response">Deserialized service response; may be <c>null</c>.</param>
		/// <param name="currentInstallLog">Installation log produced by the current run.</param>
		/// <param name="logWindowIsOfThisRun">
		/// Whether <paramref name="currentInstallLog"/> was proven to be this run's own output; see
		/// <see cref="IsLogWindowOfThisRun"/>. The downgrade is refused when it was not.
		/// </param>
		/// <returns><c>true</c> when the run must be treated as a success with warnings.</returns>
		/// <remarks>
		/// The platform answers <c>success:false</c> with the generic message "Packages installation failed"
		/// for a run that only skipped locally modified schemas, which used to make clio exit non-zero after
		/// an installation that actually finished. The decision itself lives in
		/// <see cref="InstallLogAnalyzer.ShouldTreatAsSuccess"/> so that it is testable without a server; an
		/// invalid archive is excluded here as well, because that failure is reported through the log rather
		/// than through <c>errorInfo</c> and must keep its dedicated exit code.
		/// </remarks>
		private bool TryDowngradeReportedFailure(BaseResponse response, string currentInstallLog,
			bool logWindowIsOfThisRun) {
			if (!logWindowIsOfThisRun) {
				_logger.WriteWarning(
					"The installation log could not be attributed to this run, so the reported failure is "
					+ "kept. Re-run the command to get a usable log.");
				return false;
			}
			if (IsInvalidGZipArchiveFailure(response, currentInstallLog)
				|| !InstallLogAnalyzer.ShouldTreatAsSuccess(response, currentInstallLog, CheckLogsOnSuccessMessage)) {
				return false;
			}
			_logger.WriteWarning(
				"The installation service reported a failure, but the installation finished and the only "
				+ "problem reported in this run's log was a locally modified schema. Treating the "
				+ "installation as successful.");
			return true;
		}

		protected abstract string GetRequestData(string fileName, PackageInstallOptions packageInstallOptions);

		private string InstallPackageOnServer(string fileName, EnvironmentSettings environmentSettings,
			PackageInstallOptions packageInstallOptions) {
			string installUrl = packageInstallOptions == null
				? InstallUrl
				: InstallWithOptionsUrl;
			using IOwnedApplicationClient applicationClient = CreateApplicationClient(environmentSettings);
			return applicationClient.ExecutePostRequest(GetCompleteUrl(installUrl, environmentSettings),
				GetRequestData(fileName, packageInstallOptions), Timeout.Infinite);
		}

		private static bool ContainsInvalidGZipArchiveException(string value) =>
			!string.IsNullOrWhiteSpace(value)
			&& value.IndexOf(InvalidGZipArchiveExceptionTypeName, StringComparison.OrdinalIgnoreCase) >= 0;

		private static bool IsInvalidGZipArchiveFailure(BaseResponse response, string installLog) =>
			ContainsInvalidGZipArchiveException(response?.ErrorInfo?.ErrorCode)
			|| ContainsInvalidGZipArchiveException(response?.ErrorInfo?.Message)
			|| ContainsInvalidGZipArchiveException(response?.ErrorInfo?.StackTrace)
			|| ContainsInvalidGZipArchiveException(installLog);

		private static string GetInvalidGZipArchiveMessage(BaseResponse response, string installLog) =>
			response?.ErrorInfo?.Message
			?? GetInvalidGZipArchiveLogLine(installLog)
			?? "The package archive is invalid or corrupted.";

		private static string GetInvalidGZipArchiveLogLine(string installLog) {
			if (string.IsNullOrWhiteSpace(installLog)) {
				return null;
			}
			using var reader = new StringReader(installLog);
			string line;
			while ((line = reader.ReadLine()) != null) {
				if (ContainsInvalidGZipArchiveException(line)) {
					return line.Trim();
				}
			}
			return null;
		}

		private (bool, string) InstallPackageOnServerWithLogListener(string fileName,
			EnvironmentSettings environmentSettings, PackageInstallOptions packageInstallOptions) {
			_logger.WriteLine($"Install {fileName} ...");
			_logger.WriteLine("Installation log:");
			var initialInstallLog = GetInstallLog(environmentSettings) ?? string.Empty;
			using var cancellationTokenSource = new CancellationTokenSource();
			var log = initialInstallLog;
			var task = Task.Factory.StartNew(
				() => log = ListenForLogs(cancellationTokenSource.Token, environmentSettings, initialInstallLog));
			string result;
			try {
				result = InstallPackageOnServer(fileName, environmentSettings, packageInstallOptions);
			}
			finally {
				cancellationTokenSource.Cancel();
				task.GetAwaiter().GetResult();
			}
			BaseResponse response = JsonConvert.DeserializeObject<BaseResponse>(result);
			var completeInstallLog = GetInstallLog(environmentSettings) ?? string.Empty;
			var currentInstallLog = GetLogDiff(initialInstallLog, completeInstallLog);
			bool successLog = true;
			if (CheckLogsOnSuccessMessage) {
				successLog = InstallLogAnalyzer.IsSuccessMessagePresent(completeInstallLog);
			}
			_logger.Write(GetLogDiff(log, completeInstallLog));
			var success = (response != null && response.Success || response == null) && successLog;
			ReportLocallyModifiedSchemas(currentInstallLog);
			if (ThrowInvalidGZipArchiveInstallException && !success
				&& IsInvalidGZipArchiveFailure(response, currentInstallLog)) {
				SaveLogFile(completeInstallLog, _reportPath);
				throw new InvalidGZipArchiveInstallException(
					GetInvalidGZipArchiveMessage(response, currentInstallLog));
			}
			if (!success) {
				success = TryDowngradeReportedFailure(response, currentInstallLog,
					IsLogWindowOfThisRun(initialInstallLog, completeInstallLog));
			}
			if (!success) {
				_logger.WriteError("Package installation failed: "
					+ InstallLogAnalyzer.DescribeFailure(response, currentInstallLog, successLog));
			}
			return (success, completeInstallLog);
		}

		private (bool, string) InstallPackedPackage(string filePath, EnvironmentSettings environmentSettings,
			PackageInstallOptions packageInstallOptions, bool createBackup) {
			string packageName = UploadPackage(filePath, environmentSettings);
			string packageCode = packageName.Split('.')[0];
			_logger.WriteInfo($"{environmentSettings.Uri}");
			if (createBackup && !CreateBackupPackage(packageCode, filePath, environmentSettings)) {
				return (false, "Dont created backup.");
			}
			if (!createBackup) {
				_logger.WriteInfo("Package backup skipped.");
			}
			(bool success, string logText) =
				InstallPackageOnServerWithLogListener(packageName, environmentSettings, packageInstallOptions);
			if (DeveloperModeEnabled(environmentSettings)) {
				UnlockMaintainerPackageInternal(environmentSettings);
			}
			if (DeveloperModeEnabled(environmentSettings) || environmentSettings.IsNetCore) {
				try {
					_application.Restart();
				} catch (Exception ex) {
					_logger.WriteLine($"Error while restarting application: {ex.Message}");
				}
			}
			return (success, logText);
		}

		private (bool, string) InstallPackageFromFolder(string packageFolderPath,
			EnvironmentSettings environmentSettings, PackageInstallOptions packageInstallOptions, bool createBackup){
			var packedFilePath = $"{packageFolderPath}.gz";
			_packageArchiver.Pack(packageFolderPath, packedFilePath, false, true);
			bool success = false;
			string logText;
			try {
				(success, logText) = InstallPackedPackage(packedFilePath, environmentSettings, packageInstallOptions,
					createBackup);
			} finally {
				_fileSystem.DeleteFile(packedFilePath);
			}
			return (success, logText);
		}

		private (bool, string) InstallPackage(string packagePackedFileOrFolderPath,
			EnvironmentSettings environmentSettings, PackageInstallOptions packageInstallOptions, bool createBackup) {
			bool success = false;
			string logText = null;
			if (_fileSystem.ExistsFile(packagePackedFileOrFolderPath)) {
				(success, logText) =
					InstallPackedPackage(packagePackedFileOrFolderPath, environmentSettings, packageInstallOptions,
						createBackup);
			} else if (_fileSystem.ExistsDirectory(packagePackedFileOrFolderPath)) {
				(success, logText) = InstallPackageFromFolder(packageFolderPath: packagePackedFileOrFolderPath,
					environmentSettings, packageInstallOptions, createBackup);
			} else {
				_logger.WriteLine($"Specified package not found by path {packagePackedFileOrFolderPath}");
			}
			return (success, logText);
		}

		#endregion

		#region Methods: Protected

		protected bool InternalInstall(string packagePath, EnvironmentSettings environmentSettings = null,
			PackageInstallOptions packageInstallOptions = null, string reportPath = null, bool createBackup = true){
			environmentSettings ??= _environmentSettings;
			packagePath = _fileSystem.GetCurrentDirectoryIfEmpty(packagePath);
			_reportPath = reportPath;
			(bool success, string logText) = InstallPackage(packagePath, environmentSettings, packageInstallOptions,
				createBackup);
			SaveLogFile(logText, reportPath);
			return success;
		}

		#endregion

	}
}
