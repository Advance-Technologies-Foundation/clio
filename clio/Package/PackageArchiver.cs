﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Clio.Common;
using MicrosoftIO = System.IO.Abstractions;

namespace Clio;

#region Class: PackageArchiver

public class PackageArchiver : IPackageArchiver
{

    #region Constants: Public

    public const string GzExtension = "gz";
    public const string ZipExtension = "zip";

    #endregion

    #region Fields: Private

    private readonly IFileSystem _fileSystem;
    private readonly IPackageUtilities _packageUtilities;
    private readonly ICompressionUtilities _compressionUtilities;
    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
    private readonly ILogger _logger;
    private readonly MicrosoftIO.IFileSystem _msFileSystem;
    private readonly IZipFile _zipFile;

    #endregion

    #region Constructors: Public

    public PackageArchiver(IPackageUtilities packageUtilities, ICompressionUtilities compressionUtilities,
        IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem,
        ILogger logger, MicrosoftIO.IFileSystem msFileSystem, IZipFile zipFile)
    {
        packageUtilities.CheckArgumentNull(nameof(packageUtilities));
        compressionUtilities.CheckArgumentNull(nameof(compressionUtilities));
        workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
        fileSystem.CheckArgumentNull(nameof(fileSystem));
        logger.CheckArgumentNull(nameof(logger));
        _packageUtilities = packageUtilities;
        _compressionUtilities = compressionUtilities;
        _workingDirectoriesProvider = workingDirectoriesProvider;
        _fileSystem = fileSystem;
        _logger = logger;
        _msFileSystem = msFileSystem;
        _zipFile = zipFile;
    }

    #endregion

    #region Methods: Private

    private static void CheckPackArgument(string packagePath, string packedPackagePath)
    {
        packagePath.CheckArgumentNullOrWhiteSpace(nameof(packagePath));
        packedPackagePath.CheckArgumentNullOrWhiteSpace(nameof(packedPackagePath));
    }

    private static void CheckUnpackArgument(string packedPackagePath)
    {
        packedPackagePath.CheckArgumentNullOrWhiteSpace(nameof(packedPackagePath));
    }

    private static void CheckUnZipPackagesArgument(string zipFilePath)
    {
        zipFilePath.CheckArgumentNullOrWhiteSpace(nameof(zipFilePath));
    }

    private static void CheckZipPackagesArgument(string sourceGzipFilesFolderPaths,
        string destinationArchiveFileName)
    {
        sourceGzipFilesFolderPaths.CheckArgumentNullOrWhiteSpace(nameof(sourceGzipFilesFolderPaths));
        destinationArchiveFileName.CheckArgumentNullOrWhiteSpace(nameof(destinationArchiveFileName));
    }

    private IEnumerable<string> ApplyClioIgnore(IEnumerable<string> files, string packagePath)
    {
        MicrosoftIO.IFileInfo wsIgnoreFile = _msFileSystem.DirectoryInfo.New(packagePath)
                                                          .Parent?.Parent?
                                                          .GetDirectories(".clio")?
                                                          .FirstOrDefault()?
                                                          .GetFiles(CreatioPackage.IgnoreFileName)?
                                                          .FirstOrDefault();

        bool wsIgnoreFileMissing = wsIgnoreFile is not {Exists: true};
        IEnumerable<string> filesArray = files as string[] ?? files.ToArray();
        bool childIgnoreMissing
            = !filesArray.Any(f => f.EndsWith(CreatioPackage.IgnoreFileName, StringComparison.InvariantCulture));

        if (wsIgnoreFileMissing && childIgnoreMissing)
        {
            return filesArray;
        }

        List<string> filteredFiles = [];
        Ignore.Ignore ignore = new();
        ignore.OriginalRules.Clear();
        ignore.Add(_msFileSystem.File.ReadAllLines(wsIgnoreFile!.FullName));
        foreach (string item in filesArray)
        {
            Uri fUri = new(item);
            if (!ignore.IsIgnored(fUri.ToString()))
            {
                filteredFiles.Add(item);
            }
        }

        List<string> ignoreFiles = filesArray
                                   .Where(f => f.EndsWith(CreatioPackage.IgnoreFileName,
                                       StringComparison.InvariantCulture))
                                   .ToList();

        foreach (string ignoreFile in ignoreFiles)
        {
            MicrosoftIO.IFileInfo ignoreFi = _msFileSystem.FileInfo.New(ignoreFile);
            string ignoreDir = ignoreFi.Directory.FullName;
            List<string> filesToCheck = filesArray.Where(f => f.StartsWith(ignoreDir)).ToList();

            ignore.OriginalRules.Clear();
            string[] ignoreContent = _msFileSystem.File.ReadAllLines(ignoreFiles.FirstOrDefault());
            ignore.Add(ignoreContent);

            foreach (string item in filesToCheck)
            {
                Uri fUri = new(item);
                if (!ignore.IsIgnored(fUri.ToString()))
                {
                    filteredFiles.Add(item);
                }
            }
        }
        return filteredFiles;
    }

    private void DeletePackedPackages(string[] packedPackagesPaths)
    {
        foreach (string packedPackagePath in packedPackagesPaths)
        {
            _msFileSystem.File.Delete(packedPackagePath);
        }
    }

    private void ExtractPackages(string zipFilePath, bool overwrite, bool deleteGzFiles,
        bool unpackIsSameFolder, bool isShowDialogOverwrite, string destinationPath, Func<string> getDefaultDirectory)
    {
        CheckUnZipPackagesArgument(zipFilePath);
        destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
        CheckPackedPackageExistsAndNotEmpty(zipFilePath);
        string targetDirectoryPath = unpackIsSameFolder
            ? destinationPath
            : getDefaultDirectory();
        if (deleteGzFiles)
        {
            _workingDirectoriesProvider.CreateTempDirectory(tempPath =>
            {
                string[] packedPackagesPaths = ExtractPackedPackages(zipFilePath, tempPath);
                Unpack(packedPackagesPaths, overwrite, isShowDialogOverwrite, targetDirectoryPath);
            });
        }
        else
        {
            string[] packedPackagesPaths = ExtractPackedPackages(zipFilePath, targetDirectoryPath);
            Unpack(packedPackagesPaths, overwrite, isShowDialogOverwrite, targetDirectoryPath);
        }
    }

    private string[] ExtractPackedPackages(string zipFilePath, string targetDirectoryPath)
    {
        _zipFile.ExtractToDirectory(zipFilePath, targetDirectoryPath);
        string[] packedPackagesPaths = _msFileSystem.Directory.GetFiles(targetDirectoryPath, "*.gz");
        return packedPackagesPaths;
    }

    private IEnumerable<string> GetAllFiles(string tempPath, bool skipPdb, string packagePath)
    {
        IEnumerable<string> files = _msFileSystem.Directory
                                                 .GetFiles(tempPath, "*.*", SearchOption.AllDirectories)
                                                 .Where(name =>
                                                     !name.EndsWith(".pdb", true, CultureInfo.InvariantCulture) ||
                                                     !skipPdb);
        return ApplyClioIgnore(files, packagePath);
    }

    private bool ShowDialogOverwriteDestinationPackageDir(string destinationPackagePath)
    {
        bool overwrite = true;
        if (_msFileSystem.Directory.Exists(destinationPackagePath))
        {
            Console.Write($"Directory {destinationPackagePath} already exist. Do you want replace it (y/n)? ");
            ConsoleKeyInfo key = Console.ReadKey();
            Console.WriteLine();
            overwrite = key.KeyChar == 'y';
        }
        else
        {
            _msFileSystem.Directory.CreateDirectory(destinationPackagePath);
        }
        return overwrite;
    }

    #endregion

    #region Methods: Public

    public void CheckPackedPackageExistsAndNotEmpty(string packedPackagePath)
    {
        if (!_fileSystem.ExistsFile(packedPackagePath))
        {
            throw new Exception($"Package archive {packedPackagePath} not found");
        }
        MicrosoftIO.IFileInfoFactory fileInfoFactory = _msFileSystem.FileInfo;
        MicrosoftIO.IFileInfo fileInfo = fileInfoFactory.New(packedPackagePath);
        if (fileInfo.Length == 0)
        {
            throw new Exception($"Package archive {packedPackagePath} is empty");
        }
    }

    public void ExtractPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true,
        bool unpackIsSameFolder = false, bool isShowDialogOverwrite = false, string destinationPath = null)
    {
        ExtractPackages(zipFilePath, overwrite, deleteGzFiles, unpackIsSameFolder, isShowDialogOverwrite,
            destinationPath, () => Environment.CurrentDirectory);
    }

    public IEnumerable<string> FindGzipPackedPackagesFiles(string searchDirectory)
    {
        return _msFileSystem.Directory.EnumerateFiles(searchDirectory, $"*.{GzExtension}",
            SearchOption.AllDirectories);
    }

    public string GetPackedGroupPackagesFileName(string groupPackagesName) => $"{groupPackagesName}.{ZipExtension}";

    public string GetPackedPackageFileName(string packageName) => $"{packageName}.{GzExtension}";

    public bool IsGzArchive(string filePath)
    {
        string fileExtension = _fileSystem.ExtractFileExtensionFromPath(filePath);
        return string.Compare(fileExtension, $".{GzExtension}", StringComparison.OrdinalIgnoreCase) == 0;
    }

    public bool IsZipArchive(string filePath)
    {
        string fileExtension = _fileSystem.ExtractFileExtensionFromPath(filePath);
        return string.Compare(fileExtension, $".{ZipExtension}", StringComparison.OrdinalIgnoreCase) == 0;
    }

    public void Pack(string packagePath, string packedPackagePath, bool skipPdb, bool overwrite = true)
    {
        CheckPackArgument(packagePath, packedPackagePath);
        _fileSystem.CheckOrDeleteExistsFile(packedPackagePath, overwrite);
        _workingDirectoriesProvider.CreateTempDirectory(tempPath =>
        {
            _packageUtilities.CopyPackageElements(packagePath, tempPath, overwrite);
            IEnumerable<string> files = GetAllFiles(tempPath, skipPdb, packagePath);
            _compressionUtilities.PackToGZip(files, tempPath, packedPackagePath);
        });
    }

    public void Pack(string sourcePath, string destinationPath, IEnumerable<string> names, bool skipPdb,
        bool overwrite = true)
    {
        _workingDirectoriesProvider.CreateTempDirectory(tempPath =>
        {
            sourcePath ??= Environment.CurrentDirectory;
            foreach (string name in names)
            {
                string currentSourcePath = Path.Combine(sourcePath, name);
                string currentDestinationPath = Path.Combine(tempPath, name + ".gz");
                Pack(currentSourcePath, currentDestinationPath, skipPdb, overwrite);
            }
            ZipFile.CreateFromDirectory(tempPath, destinationPath);
        });
    }

    public void Unpack(string packedPackagePath, bool overwrite, bool isShowDialogOverwrite = false,
        string destinationPath = null)
    {
        CheckUnpackArgument(packedPackagePath);
        CheckPackedPackageExistsAndNotEmpty(packedPackagePath);
        destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
        string destinationPackageDirectory =
            _fileSystem.GetDestinationFileDirectory(packedPackagePath, destinationPath);
        if (isShowDialogOverwrite)
        {
            overwrite = ShowDialogOverwriteDestinationPackageDir(destinationPackageDirectory);
        }
        if (!overwrite)
        {
            return;
        }
        _fileSystem.CreateOrOverwriteExistsDirectoryIfNeeded(destinationPackageDirectory, overwrite);
        _compressionUtilities.UnpackFromGZip(packedPackagePath, destinationPackageDirectory);
    }

    public void Unpack(IEnumerable<string> packedPackagesPaths, bool overwrite, bool isShowDialogOverwrite = false,
        string destinationPath = null)
    {
        packedPackagesPaths.CheckArgumentNull(nameof(packedPackagesPaths));
        destinationPath = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
        foreach (string packedPackagePath in packedPackagesPaths)
        {
            string packageName = _fileSystem.ExtractFileNameFromPath(packedPackagePath);
            _logger.WriteLine($"Start unzip package ({packageName}).");
            Unpack(packedPackagePath, overwrite, isShowDialogOverwrite, destinationPath);
            _logger.WriteLine($"Unzip package ({packageName}) completed.");
        }
    }

    /// <inheritdoc cref="System.IO.Compression.ZipFile.ExtractToDirectory(string, string)" />
    public void UnZip(string zipFilePath, bool overwrite, string destinationPath = null)
    {
        CheckUnZipPackagesArgument(zipFilePath);
        CheckPackedPackageExistsAndNotEmpty(zipFilePath);
        _zipFile.ExtractToDirectory(zipFilePath, destinationPath ?? Environment.CurrentDirectory);
    }

    public void UnZipPackages(string zipFilePath, bool overwrite, bool deleteGzFiles = true,
        bool unpackIsSameFolder = false, bool isShowDialogOverwrite = false, string destinationPath = null)
    {
        ExtractPackages(zipFilePath, overwrite, deleteGzFiles, unpackIsSameFolder, isShowDialogOverwrite,
            destinationPath, () => _fileSystem.GetDestinationFileDirectory(zipFilePath, destinationPath));
    }

    public void ZipPackages(string sourceGzipFilesFolderPaths, string destinationArchiveFileName, bool overwrite)
    {
        CheckZipPackagesArgument(sourceGzipFilesFolderPaths, destinationArchiveFileName);
        _fileSystem.CheckOrDeleteExistsFile(destinationArchiveFileName, overwrite);
        _zipFile.CreateFromDirectory(sourceGzipFilesFolderPaths, destinationArchiveFileName);
    }

    #endregion

}

#endregion
