using System.Collections.Generic;

namespace Clio.Project.NuGet;

public interface INugetPackagesProvider
{

    #region Methods: Public

    IEnumerable<LastVersionNugetPackages> GetLastVersionPackages(IEnumerable<string> packagesNames,
        string nugetSourceUrl);

    LastVersionNugetPackages GetLastVersionPackages(string packageName, string nugetSourceUrl);

    #endregion

}
