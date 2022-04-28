using System;
using System.IO;
using System.Text;
using System.Threading;
using Clio.WebApplication;
using Clio.Common;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Creatio.Client;

namespace Clio.Package
{

	#region Class: PackageInstaller

	public class PackageInstaller : IPackageInstaller
	{

		#region Constants: Private

		private const string InstallUrl = @"/ServiceModel/PackageInstallerService.svc/InstallPackage";
		private const string InstallWithOptionsUrl = @"/rest/ClioPackageInstallerService/Install";
		private const string InstallLogUrl = @"/ServiceModel/PackageInstallerService.svc/GetLogFile";
		private const string UploadUrl = @"/ServiceModel/PackageInstallerService.svc/UploadPackage";
		private const string DefLogFileName = "cliolog.txt";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IApplicationClient _applicationClient;
		private readonly IApplicationClient _applicationClientForLog;
		private readonly IPackageArchiver _packageArchiver;
		private readonly ISqlScriptExecutor _scriptExecutor;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly IFileSystem _fileSystem;
		private readonly bool _developerModeEnabled;
		private readonly ILogger _logger;

		private readonly IApplication _application;
		private string _reportPath;

		#endregion

		#region Constructors: Public

		public PackageInstaller(EnvironmentSettings environmentSettings,
				IApplicationClientFactory applicationClientFactory, IApplication application,
				IPackageArchiver packageArchiver, ISqlScriptExecutor scriptExecutor,
				IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem, ILogger logger)
		{
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			application.CheckArgumentNull(nameof(application));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			scriptExecutor.CheckArgumentNull(nameof(scriptExecutor));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
			_application = application;
			_packageArchiver = packageArchiver;
			_scriptExecutor = scriptExecutor;
			_serviceUrlBuilder = serviceUrlBuilder;
			_fileSystem = fileSystem;
			_logger = logger;
			_applicationClient = _applicationClientFactory.CreateClient(_environmentSettings);
			_applicationClientForLog = _applicationClientFactory.CreateClient(_environmentSettings);
			_developerModeEnabled = _environmentSettings.DeveloperModeEnabled.HasValue &&
									_environmentSettings.DeveloperModeEnabled.Value;
		}

		#endregion

		#region Methods: Private

		private string GetCompleteUrl(string url) => _serviceUrlBuilder.Build(url);

		private void UnlockMaintainerPackageInternal()
		{
			var script = $"UPDATE SysPackage SET InstallType = 0 WHERE Maintainer = '{_environmentSettings.Maintainer}'";
			_scriptExecutor.Execute(script, _applicationClient, _environmentSettings);
		}

		private void SaveLogFile(string logText, string reportPath)
		{
			if (reportPath != null && !string.IsNullOrWhiteSpace(logText))
			{
				if (File.Exists(reportPath))
				{
					File.Delete(reportPath);
				}
				else if (Directory.Exists(reportPath))
				{
					reportPath = Path.Combine(reportPath, DefLogFileName);
				}
				File.WriteAllText(reportPath, logText, Encoding.UTF8);
			}
		}

		private string UploadPackage(string filePath)
		{
			_logger.WriteLine("Uploading...");
			FileInfo fileInfo = new FileInfo(filePath);
			string packageName = fileInfo.Name;
			_applicationClient.UploadFile(GetCompleteUrl(UploadUrl), filePath);
			_logger.WriteLine("Uploaded");
			return packageName;
		}

		private string GetInstallLog()
		{
			try
			{
				return _applicationClientForLog.ExecuteGetRequest(GetCompleteUrl(InstallLogUrl));
			}
			catch (Exception ex)
			{
			}
			return String.Empty;
		}

		private string GetLogDiff(string currentLog, string completeLog)
		{
			return string.IsNullOrWhiteSpace(completeLog)
				? string.Empty
				: ((completeLog.Length > currentLog.Length) ? completeLog.Substring(currentLog.Length) : String.Empty);
		}

		private string ListenForLogs(object cancellationTokenObject)
		{
			var cancellationToken = (CancellationToken)cancellationTokenObject;
			var currentLogContent = string.Empty;
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					var completeLog = GetInstallLog();
					var output = GetLogDiff(currentLogContent, completeLog);
					if (!string.IsNullOrWhiteSpace(output))
					{
						_logger.Write(output);
						currentLogContent = completeLog;
						if (!string.IsNullOrWhiteSpace(_reportPath)) SaveLogFile(currentLogContent, _reportPath);
					}
					Thread.Sleep(3000);
				}
				catch (System.Exception e)
				{
					_logger.WriteLine(e.ToString());
				}
			}
			return currentLogContent;
		}

		private string GetRequestData(string fileName, PackageInstallOptions packageInstallOptions) =>
			packageInstallOptions == null
				? $"\"{fileName}\""
				: $" {{ \"zipPackageName\": \"{fileName}\", " +
				  "\"packageInstallOptions\": { " +
				  $"\"installSqlScript\": \"{packageInstallOptions.InstallSqlScript.ToString().ToLower()}\", " +
				  $"\"installPackageData\": \"{packageInstallOptions.InstallPackageData.ToString().ToLower()}\", " +
				  $"\"continueIfError\": \"{packageInstallOptions.ContinueIfError.ToString().ToLower()}\", " +
				  $"\"skipConstraints\": \"{packageInstallOptions.SkipConstraints.ToString().ToLower()}\", " +
				  $"\"skipValidateActions\": \"{packageInstallOptions.SkipValidateActions.ToString().ToLower()}\", " +
				  $"\"executeValidateActions\": \"{packageInstallOptions.ExecuteValidateActions.ToString().ToLower()}\", " +
				  $"\"isForceUpdateAllColumns\": \"{packageInstallOptions.IsForceUpdateAllColumns.ToString().ToLower()}\"  " +
				  " } }";

		private string InstallPackageOnServer(string fileName, PackageInstallOptions packageInstallOptions) {
			string installUrl = packageInstallOptions == null 
				? InstallUrl 
				: InstallWithOptionsUrl;
			return _applicationClient.ExecutePostRequest(GetCompleteUrl(installUrl), 
				GetRequestData(fileName, packageInstallOptions), Timeout.Infinite);
		}

		private (bool, string) InstallPackageOnServerWithLogListener(string fileName, 
				PackageInstallOptions packageInstallOptions) {
			_logger.WriteLine($"Install {fileName} ...");
			_logger.WriteLine("Installation log:");
			var cancellationTokenSource = new CancellationTokenSource();
			var log = string.Empty;
			var task = Task.Factory.StartNew(
				(cancellationToken) => log = ListenForLogs(cancellationToken), cancellationTokenSource.Token);

			string result = InstallPackageOnServer(fileName, packageInstallOptions);
			BaseResponse response = JsonConvert.DeserializeObject<BaseResponse>(result);
			cancellationTokenSource.Cancel();
			task.Wait();
			var completeInstallLog = GetInstallLog();
			_logger.Write(GetLogDiff(log, completeInstallLog));
			return (response != null && response.Success || response == null, completeInstallLog);
		}

		private (bool, string) InstallPackedPackage(string filePath, PackageInstallOptions packageInstallOptions) {
			string packageName = UploadPackage(filePath);
			(bool success, string logText) = InstallPackageOnServerWithLogListener(packageName, packageInstallOptions);
			if (_developerModeEnabled) {
				UnlockMaintainerPackageInternal();
				_application.Restart();
			}
			return (success, logText);
		}

		private (bool, string) InstallPackageFromFolder(string packageFolderPath, 
			PackageInstallOptions packageInstallOptions) {
			var packedFilePath = $"{packageFolderPath}.gz";
			_packageArchiver.Pack(packageFolderPath, packedFilePath, false, true);
			bool success = false;
			string logText;
			try {
				(success, logText) = InstallPackedPackage(packedFilePath, packageInstallOptions);
			}
			finally {
				File.Delete(packedFilePath);
			}
			return (success, logText);
		}


		private (bool, string) InstallPackage(string packagePackedFileOrFolderPath, 
			PackageInstallOptions packageInstallOptions) {
			bool success = false;
			string logText = null;
			if (File.Exists(packagePackedFileOrFolderPath)) {
				(success, logText) = InstallPackedPackage(packagePackedFileOrFolderPath, packageInstallOptions);
			}
			else if (Directory.Exists(packagePackedFileOrFolderPath)) {
				(success, logText) = InstallPackageFromFolder(packageFolderPath: packagePackedFileOrFolderPath,
					packageInstallOptions);
			}
			else {
				_logger.WriteLine($"Specified package not found by path {packagePackedFileOrFolderPath}");
			}
			return (success, logText);
		}

		#endregion

		#region Methods: Public

		public bool Install(string packagePath, PackageInstallOptions packageInstallOptions = null, 
				string reportPath = null) {
			packagePath = _fileSystem.GetCurrentDirectoryIfEmpty(packagePath);
			_reportPath = reportPath;
			(bool success, string logText) = InstallPackage(packagePath, packageInstallOptions);
			SaveLogFile(logText, reportPath);
			return success;
		}

		#endregion

	}

	#endregion

}