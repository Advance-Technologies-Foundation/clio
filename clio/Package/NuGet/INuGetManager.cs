using System.Collections.Generic;

namespace Clio.Project.NuGet;

public interface INuGetManager
{

    #region Methods: Public

    IEnumerable<PackageForUpdate> GetPackagesForUpdate(string nugetSourceUrl);

    void Pack(string packagePath, IEnumerable<PackageDependency> dependencies, bool skipPdb,
        string destinationNupkgDirectory);

    void Push(string nupkgFilePath, string apiKey, string nugetSourceUrl);

    void RestoreToDirectory(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
        string destinationDirectory, bool overwrite);

    void RestoreToNugetFileStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
        string destinationDirectory);

    void RestoreToPackageStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
        string destinationDirectory, bool overwrite);

    #endregion

}
