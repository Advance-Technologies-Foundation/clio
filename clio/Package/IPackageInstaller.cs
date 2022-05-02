namespace Clio.Package
{
	public interface IPackageInstaller
	{
		bool Install(string packagePath, PackageInstallOptions packageInstallOptions = null, string reportPath = null);

	}
}