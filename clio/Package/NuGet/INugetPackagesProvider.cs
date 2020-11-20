using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Clio.Project.NuGet
{
	public interface INugetPackagesProvider
	{
		IEnumerable<LastVersionNugetPackages> GetLastVersionPackages(IEnumerable<string> packagesNames,
			string nugetSourceUrl);
		LastVersionNugetPackages GetLastVersionPackages(string packageName, string nugetSourceUrl);

	}
}