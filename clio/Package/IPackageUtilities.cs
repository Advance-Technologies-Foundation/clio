using System.Collections.Generic;

namespace Clio.Common;

public interface IPackageUtilities
{
    void CopyPackageElements(string sourcePath, string destinationPath, bool overwrite);

    string GetPackageContentFolderPath(string repositoryPackageFolderPath);

    string GetPackageContentFolderPath(string repositoryFolderPath, string packageName);
}
