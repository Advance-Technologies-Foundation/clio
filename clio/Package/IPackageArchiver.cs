using System.Collections.Generic;

namespace Clio;

public interface IPackageArchiver
{

    #region Methods: Public

    void CheckPackedPackageExistsAndNotEmpty(string packedPackagePath);

    void ExtractPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true,
        bool unpackIsSameFolder = false, bool isShowDialogOverwrite = false, string destinationPath = null);

    IEnumerable<string> FindGzipPackedPackagesFiles(string searchDirectory);

    string GetPackedGroupPackagesFileName(string groupPackagesName);

    string GetPackedPackageFileName(string packageName);

    public bool IsGzArchive(string filePath);

    public bool IsZipArchive(string filePath);

    void Pack(string packagePath, string packedPackagePath, bool skipPdb, bool overwrite = true);

    void Pack(string sourcePath, string destinationPath, IEnumerable<string> names, bool skipPdb,
        bool overwrite = true);

    void Unpack(string packedPackagePath, bool overwrite, bool isShowDialogOverwrite = false,
        string destinationPath = null);

    void Unpack(IEnumerable<string> packedPackagesPaths, bool overwrite, bool isShowDialogOverwrite = false,
        string destinationPath = null);

    /// <inheritdoc cref="System.IO.Compression.ZipFile.ExtractToDirectory(string, string)" />
    void UnZip(string zipFilePath, bool overwrite, string destinationPath = null);

    void UnZipPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true,
        bool unpackIsSameFolder = false, bool isShowDialogOverwrite = false, string destinationPath = null);

    void ZipPackages(string sourceGzipFilesFolderPaths, string destinationArchiveFileName, bool overwrite);

    #endregion

}
