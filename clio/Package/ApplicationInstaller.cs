namespace Clio.Package
{
	using Clio.Common;
	using Clio.WebApplication;

	public class ApplicationInstaller : BasePackageInstaller, IApplicationInstaller {

		#region Constructors: Public

		public ApplicationInstaller(EnvironmentSettings environmentSettings,
				IApplicationClientFactory applicationClientFactory, IApplication application,
				IPackageArchiver packageArchiver, ISqlScriptExecutor scriptExecutor,
				IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem, ILogger logger) 
			: base( environmentSettings, applicationClientFactory,  application,
				 packageArchiver,  scriptExecutor, serviceUrlBuilder,  fileSystem, logger) {
		}

		#endregion

		#region Properties: Protected

		protected override string InstallUrl => @"/ServiceModel/AppInstallerService.svc/InstallAppFromFile";

		#endregion

		#region Methods: Protected

		protected override string GetRequestData(string fileName, PackageInstallOptions packageInstallOptions) {
			string code = _fileSystem.GetFileNameWithoutExtension(new System.IO.FileInfo(fileName));
			return
			$"{{\"Name\":\"{code}\",\"Code\":\"{code}\",\"ZipPackageName\":\"{fileName}\",\"LastUpdateString\":0}}";
		}

		#endregion

		#region Methods: Public

		public bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
				string reportPath = null) {
			return InternalInstall(packagePath, environmentSettings, null, reportPath);
		}

		#endregion


	}
}
