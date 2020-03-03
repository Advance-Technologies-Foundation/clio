using System.IO;
using System.Text;
using System.Threading;
using Clio.WebApplication;
using Clio.Command;
using Clio.Common;
using System.Threading.Tasks;

namespace Clio.Package
{

	#region Class: PackageInstaller

	public class PackageInstaller : IPackageInstaller
	{

		#region Constants: Private

		private const string InstallUrl = @"/ServiceModel/PackageInstallerService.svc/InstallPackage";
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

		#endregion

		#region Constructors: Public

		public PackageInstaller(EnvironmentSettings environmentSettings, 
				IApplicationClientFactory applicationClientFactory, IApplication application, 
				IPackageArchiver packageArchiver, ISqlScriptExecutor scriptExecutor, 
				IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem, ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			application.CheckArgumentNull(nameof(application));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			scriptExecutor.CheckArgumentNull(nameof(scriptExecutor));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
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

		private void UnlockMaintainerPackageInternal() {
			var script = $"UPDATE SysPackage SET InstallType = 0 WHERE Maintainer = '{_environmentSettings.Maintainer}'";
			_scriptExecutor.Execute(script, _applicationClient, _environmentSettings);
		}

		private void SaveLogFile(string logText, string reportPath) {
			if (File.Exists(reportPath)) {
				File.Delete(reportPath);
			} else if (Directory.Exists(reportPath)) {
				reportPath = Path.Combine(reportPath, DefLogFileName);
			}
			File.WriteAllText(reportPath, logText, Encoding.UTF8);
		}

		private string UploadPackage(string filePath) {
			_logger.WriteLine("Uploading...");
			FileInfo fileInfo = new FileInfo(filePath);
			string packageName = fileInfo.Name;
			_applicationClient.UploadFile(GetCompleteUrl(UploadUrl), filePath);
			_logger.WriteLine("Uploaded");
			return packageName;
		}

		private string GetInstallLog() {
			return _applicationClientForLog.ExecuteGetRequest(GetCompleteUrl(InstallLogUrl), Timeout.Infinite);
		}

		private string GetLogDiff(string currentLog, string completeLog) {
			return completeLog.Substring(currentLog.Length);
		}

		private string ListenForLogs(object cancellationTokenObject) {
			var cancellationToken = (CancellationToken)cancellationTokenObject;
			var currentLogContent = string.Empty;
			while (!cancellationToken.IsCancellationRequested) {
				try {
					var completeLog = GetInstallLog();
					var output = GetLogDiff(currentLogContent, completeLog);
					if (!string.IsNullOrWhiteSpace(output)) {
						_logger.Write(output);
						currentLogContent = completeLog;
					}
					Thread.Sleep(500);
				} catch (System.Exception e) {
					_logger.WriteLine(e.ToString());
				}
			}
			return currentLogContent;
		}
		
		private void InstallPackageOnServer(string fileName) {
			_applicationClient.ExecutePostRequest(GetCompleteUrl(InstallUrl), $"\"{fileName}\"", Timeout.Infinite);
		}
		
		private string InstallPackageOnServerWithLogListener(string fileName) {
			_logger.WriteLine($"Install {fileName} ...");
			_logger.WriteLine("Installation log:");
			var cancellationTokenSource = new CancellationTokenSource();
			var log = string.Empty;
			var task = Task.Factory.StartNew(
				(cancellationToken) => log = ListenForLogs(cancellationToken), cancellationTokenSource.Token);
			InstallPackageOnServer(fileName);
			cancellationTokenSource.Cancel();
			task.Wait();
			var completeInstallLog = GetInstallLog();
			_logger.Write(GetLogDiff(log, completeInstallLog));
			return completeInstallLog;
		}

		private string InstallPackedPackage(string filePath) {
			string packageName = UploadPackage(filePath);
			string logText = InstallPackageOnServerWithLogListener(packageName);
			if (_developerModeEnabled) {
				UnlockMaintainerPackageInternal();
				_application.Restart();
			}
			return logText;
		}

		private string InstallPackageFromFolder(string packageFolderPath) {
			var packedFilePath = $"{packageFolderPath}.gz";
			_packageArchiver.Pack(packageFolderPath, packedFilePath, false, true);
			string logText;
			try {
				logText = InstallPackedPackage(packedFilePath);
			} finally {
				File.Delete(packedFilePath);
			}
			return logText;
		}

		private string InstallPackage(string packagePackedFileOrFolderPath) {
			string logText = null;
			if (File.Exists(packagePackedFileOrFolderPath)) {
				logText = InstallPackedPackage(packagePackedFileOrFolderPath);
			} else if (Directory.Exists(packagePackedFileOrFolderPath)) {
				logText = InstallPackageFromFolder(packageFolderPath: packagePackedFileOrFolderPath);
			} else {
				_logger.WriteLine($"Specified package not found by path {packagePackedFileOrFolderPath}");
			}
			return logText;
		}

		#endregion

		#region Methods: Public

		public void Install(string packagePath, string reportPath = null) {
			packagePath = _fileSystem.GetCurrentDirectoryIfEmpty(packagePath);
			string logText = InstallPackage(packagePath);
			if (string.IsNullOrWhiteSpace(reportPath) || string.IsNullOrWhiteSpace(logText)) {
				return;
			}
			SaveLogFile(logText, reportPath);
		}

		#endregion

	}

	#endregion

}