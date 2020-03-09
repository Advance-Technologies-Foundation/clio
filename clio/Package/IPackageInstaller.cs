namespace Clio.Package
{
	public interface IPackageInstaller
	{
		void Install(string packagePath, string reportPath = null);
	}
}