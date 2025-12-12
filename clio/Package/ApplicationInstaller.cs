using System.IO;
using System.Text.Json;
using Clio.Requests;
using CreatioModel;

namespace Clio.Package
{
	using Clio.Common;
	using Clio.WebApplication;

	public class ApplicationInstaller : BasePackageInstaller, IApplicationInstaller
	{
		#region Fields: Private
		
		private bool? _checkCompilationErrors;
		
		#endregion

		#region Constructors: Public

		public ApplicationInstaller(IApplicationLogProvider applicationLogProvider, EnvironmentSettings environmentSettings,
			IApplicationClientFactory applicationClientFactory, IApplication application,
			IPackageArchiver packageArchiver, ISqlScriptExecutor scriptExecutor,
			IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem, ILogger logger, IPackageLockManager packageLockManager)
			: base(applicationLogProvider, environmentSettings, applicationClientFactory, application,
				packageArchiver, scriptExecutor, serviceUrlBuilder, fileSystem, logger, packageLockManager) { }

		#endregion

		#region Properties: Protected

		protected override string BackupUrl => @"/ServiceModel/PackageInstallerService.svc/CreatePackageBackup";

		protected override string InstallUrl => @"/ServiceModel/AppInstallerService.svc/InstallAppFromFile";

		protected string UnInstallUrl => @"/ServiceModel/AppInstallerService.svc/UninstallApp";

		#endregion

		#region Methods: Private

		private bool InternalUnInstall(SysInstalledApp appInfo, EnvironmentSettings environmentSettings, object o,
			string reportPath){
			IApplicationClient client = _applicationClientFactory.CreateClient(environmentSettings);
			string completeUrl = GetCompleteUrl(UnInstallUrl, environmentSettings);
			_logger.WriteInfo($"Uninstalling {appInfo.Code}");
			string result = client.ExecutePostRequest(completeUrl, "\"" + appInfo.Id + "\"");
			_logger.WriteInfo($"Application {appInfo.Code} uninstalled");
			return true;
		}

		#endregion

		#region Methods: Protected

		protected override string GetRequestData(string fileName, PackageInstallOptions packageInstallOptions)
		{
			string code = _fileSystem.GetFileNameWithoutExtension(new FileInfo(fileName));
			var request = new InstallAppRequest {
				Name = code,
				Code = code,
				ZipPackageName = fileName,
				LastUpdateString = 0,
				CheckCompilationErrors = _checkCompilationErrors
			};
			return JsonSerializer.Serialize(request);
		}

		#endregion

		#region Methods: Public

		public bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
			string reportPath = null, bool? checkCompilationErrors = null)
		{
			_checkCompilationErrors = checkCompilationErrors;
			return InternalInstall(packagePath, environmentSettings, null, reportPath);
		}

		public bool UnInstall(SysInstalledApp appInfo, EnvironmentSettings environmentSettings = null,
			string reportPath = null){
			return InternalUnInstall(appInfo, environmentSettings, null, reportPath);
		}

		#endregion

	}
}