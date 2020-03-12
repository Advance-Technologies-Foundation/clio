using System;
using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INugetPackagesProvider
	{
		IEnumerable<NugetPackage> GetPackages(string nugetSourceUrl);
		LastVersionNugetPackages GetLastVersionPackages(string packageName, IEnumerable<NugetPackage> nugetPackages);
		LastVersionNugetPackages GetLastVersionPackages(string packageName, string nugetSourceUrl);

	}
}