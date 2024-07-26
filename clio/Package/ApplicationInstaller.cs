using System;
using System.IO;
using System.Management.Automation;
using ATF.Repository.Providers;
using CreatioModel;

namespace Clio.Package
{
	using Clio.Common;
	using Clio.WebApplication;

	public class ApplicationInstaller : BasePackageInstaller, IApplicationInstaller
	{

		#region Constructors: Public

		public ApplicationInstaller(IApplicationLogProvider applicationLogProvider, EnvironmentSettings environmentSettings,
			IApplicationClientFactory applicationClientFactory, IApplication application,
			IPackageArchiver packageArchiver, ISqlScriptExecutor scriptExecutor,
			IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem, ILogger logger)
			: base(applicationLogProvider, environmentSettings, applicationClientFactory, application,
				packageArchiver, scriptExecutor, serviceUrlBuilder, fileSystem, logger){ }

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

		protected override string GetRequestData(string fileName, PackageInstallOptions packageInstallOptions){
			string code = _fileSystem.GetFileNameWithoutExtension(new FileInfo(fileName));
			return
				$"{{\"Name\":\"{code}\",\"Code\":\"{code}\",\"ZipPackageName\":\"{fileName}\",\"LastUpdateString\":0}}";
		}

		#endregion

		#region Methods: Public

		public bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
			string reportPath = null){
			return InternalInstall(packagePath, environmentSettings, null, reportPath);
		}

		public bool UnInstall(SysInstalledApp appInfo, EnvironmentSettings environmentSettings = null,
			string reportPath = null){
			return InternalUnInstall(appInfo, environmentSettings, null, reportPath);
		}

		#endregion

	}
}