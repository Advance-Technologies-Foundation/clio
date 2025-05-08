using System.Collections.Generic;

namespace Clio.Project.NuGet;

public interface INugetPackagesProvider
{
    IEnumerable<LastVersionNugetPackages> GetLastVersionPackages(
        IEnumerable<string> packagesNames,
        string nugetSourceUrl);

    LastVersionNugetPackages GetLastVersionPackages(string packageName, string nugetSourceUrl);
}
