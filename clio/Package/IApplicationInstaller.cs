namespace Clio.Package
{

	#region Interface: IPackageDownloader

	public interface IApplicationInstaller
	{

		#region Methods: Public

		bool Install(string packagePath, EnvironmentSettings environmentSettings = null,
			string reportPath = null);

		#endregion

	}

	#endregion

}