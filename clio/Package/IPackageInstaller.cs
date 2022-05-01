namespace Clio.Package
{

	#region Interface: IPackageDownloader

	public interface IPackageInstaller
	{

		#region Methods: Public

		bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
			PackageInstallOptions packageInstallOptions = null, string reportPath = null);

		#endregion

	}

	#endregion

}