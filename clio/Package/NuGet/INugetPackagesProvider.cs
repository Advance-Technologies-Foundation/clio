using System;
using System.Collections.Generic;

namespace Clio.Project.NuGet
{
	public interface INugetPackagesProvider
	{
		IEnumerable<NugetPackage> GetPackages(string nugetSourceUrl);
		NugetPackage GetLastVersionPackage(string packageName, string nugetSourceUrl);

	}
}