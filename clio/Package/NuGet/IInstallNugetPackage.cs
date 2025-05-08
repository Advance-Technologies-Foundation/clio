using System.Collections.Generic;

namespace Clio.Project.NuGet;

#region Interface: IInstallNugetPackage

public interface IInstallNugetPackage
{

    #region Methods: Public

    void Install(IEnumerable<NugetPackageFullName> nugetPackageFullNames, string nugetSourceUrl);

    void Install(string packageName, string version, string nugetSourceUrl);

    #endregion

}

#endregion
