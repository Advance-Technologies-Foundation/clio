﻿namespace Clio.Package
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

	public abstract class BasePackageInstaller {

		#region Constants: Private

		private const string InstallWithOptionsUrl = @"/rest/ClioPackageInstallerService/Install";
		private const string UploadUrl = @"/ServiceModel/PackageInstallerService.svc/UploadPackage";
		private const string DefLogFileName = "cliolog.txt";
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

		private IApplicationClient CreateApplicationClient(EnvironmentSettings environmentSettings) =>
			_applicationClientFactory.CreateClient(environmentSettings);

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
			IApplicationClient applicationClient = CreateApplicationClient(environmentSettings);
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
				IApplicationClient applicationClient = CreateApplicationClient(environmentSettings);
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
			return string.IsNullOrWhiteSpace(completeLog)
				? string.Empty
				: ((completeLog.Length > currentLog.Length) ? completeLog.Substring(currentLog.Length) : String.Empty);
		}

		private string ListenForLogs(object cancellationTokenObject, EnvironmentSettings environmentSettings) {
			var cancellationToken = (CancellationToken)cancellationTokenObject;
			var currentLogContent = string.Empty;
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
					Thread.Sleep(3000);
				} catch {}
			}
			return currentLogContent;
		}

		protected abstract string GetRequestData(string fileName, PackageInstallOptions packageInstallOptions);

		private string InstallPackageOnServer(string fileName, EnvironmentSettings environmentSettings,
			PackageInstallOptions packageInstallOptions) {
			string installUrl = packageInstallOptions == null
				? InstallUrl
				: InstallWithOptionsUrl;
			IApplicationClient applicationClient = CreateApplicationClient(environmentSettings);
			return applicationClient.ExecutePostRequest(GetCompleteUrl(installUrl, environmentSettings),
				GetRequestData(fileName, packageInstallOptions), Timeout.Infinite);
		}

		private (bool, string) InstallPackageOnServerWithLogListener(string fileName,
			EnvironmentSettings environmentSettings, PackageInstallOptions packageInstallOptions) {
			_logger.WriteLine($"Install {fileName} ...");
			_logger.WriteLine("Installation log:");
			var cancellationTokenSource = new CancellationTokenSource();
			var log = string.Empty;
			var task = Task.Factory.StartNew(
				(cancellationToken) =>
					log = ListenForLogs(cancellationToken, environmentSettings), cancellationTokenSource.Token);
			string result = InstallPackageOnServer(fileName, environmentSettings, packageInstallOptions);
			BaseResponse response = JsonConvert.DeserializeObject<BaseResponse>(result);
			cancellationTokenSource.Cancel();
			task.Wait();
			var completeInstallLog = GetInstallLog(environmentSettings);
			bool successLog = true;
			if (CheckLogsOnSuccessMessage) {
				successLog = completeInstallLog.ToLower().Contains("application installed successfully");
			}
			_logger.Write(GetLogDiff(log, completeInstallLog));
			var success = (response != null && response.Success || response == null) && successLog;
			return (success, completeInstallLog);
		}

		private (bool, string) InstallPackedPackage(string filePath, EnvironmentSettings environmentSettings,
			PackageInstallOptions packageInstallOptions) {
			string packageName = UploadPackage(filePath, environmentSettings);
			string packageCode = packageName.Split('.')[0];
			_logger.WriteInfo($"{environmentSettings.Uri}");
			if (!CreateBackupPackage(packageCode, filePath, environmentSettings)) {
				return (false, "Dont created backup.");
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
			EnvironmentSettings environmentSettings, PackageInstallOptions packageInstallOptions){
			var packedFilePath = $"{packageFolderPath}.gz";
			_packageArchiver.Pack(packageFolderPath, packedFilePath, false, true);
			bool success = false;
			string logText;
			try {
				(success, logText) = InstallPackedPackage(packedFilePath, environmentSettings, packageInstallOptions);
			} finally {
				_fileSystem.DeleteFile(packedFilePath);
			}
			return (success, logText);
		}

		private (bool, string) InstallPackage(string packagePackedFileOrFolderPath,
			EnvironmentSettings environmentSettings, PackageInstallOptions packageInstallOptions) {
			bool success = false;
			string logText = null;
			if (_fileSystem.ExistsFile(packagePackedFileOrFolderPath)) {
				(success, logText) =
					InstallPackedPackage(packagePackedFileOrFolderPath, environmentSettings, packageInstallOptions);
			} else if (_fileSystem.ExistsDirectory(packagePackedFileOrFolderPath)) {
				(success, logText) = InstallPackageFromFolder(packageFolderPath: packagePackedFileOrFolderPath,
					environmentSettings, packageInstallOptions);
			} else {
				_logger.WriteLine($"Specified package not found by path {packagePackedFileOrFolderPath}");
			}
			return (success, logText);
		}

		#endregion

		#region Methods: Protected

		protected bool InternalInstall(string packagePath, EnvironmentSettings environmentSettings = null,
			PackageInstallOptions packageInstallOptions = null, string reportPath = null){
			environmentSettings ??= _environmentSettings;
			packagePath = _fileSystem.GetCurrentDirectoryIfEmpty(packagePath);
			_reportPath = reportPath;
			(bool success, string logText) = InstallPackage(packagePath, environmentSettings, packageInstallOptions);
			SaveLogFile(logText, reportPath);
			return success;
		}

		#endregion

	}
}