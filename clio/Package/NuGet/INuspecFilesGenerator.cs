using System.Collections.Generic;

namespace Clio.Project.NuGet;

public interface INuspecFilesGenerator
{

    #region Methods: Public

    void Create(PackageInfo packageInfo, IEnumerable<PackageDependency> dependencies, string compressedPackagePath,
        string nuspecFilePath);

    string GetNuspecFileName(PackageInfo packageInfo);

    #endregion

}
