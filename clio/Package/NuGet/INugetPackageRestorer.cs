namespace Clio.Project.NuGet;

public interface INugetPackageRestorer
{

    #region Methods: Public

    void RestoreToDirectory(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
        string destinationDirectory, bool overwrite);

    void RestoreToNugetFileStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
        string destinationDirectory);

    void RestoreToPackageStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
        string destinationDirectory, bool overwrite);

    #endregion

}
