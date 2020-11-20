using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface IInstallNugetPackage
	{

		void Install(IEnumerable<NugetPackageFullName> nugetPackageFullNames, string nugetSourceUrl);
		void Install(string packageName, string version, string nugetSourceUrl);

	}
}