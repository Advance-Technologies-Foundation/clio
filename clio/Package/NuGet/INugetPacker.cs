namespace Clio.Project.NuGet;

public interface INugetPacker
{

    #region Methods: Public

    string GetNupkgFileName(PackageInfo packageInfo);

    void Pack(string nuspecFilePath, string destinationNupkgDirectory);

    #endregion

}
