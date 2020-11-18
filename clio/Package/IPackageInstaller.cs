using Clio.Common;

namespace Clio.Package
{
	public interface IPackageInstaller
	{
		bool Install(string packagePath, string reportPath = null);
	}
}