using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Package;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio;

#region Class: InstallNugetPackage

public class PackageInfoProvider : IPackageInfoProvider
{

    #region Fields: Private

    private readonly IFileSystem _fileSystem;

    #endregion

    #region Fields: Protected

    protected readonly IJsonConverter _jsonConverter;

    #endregion

    #region Constructors: Public

    public PackageInfoProvider(IJsonConverter jsonConverter, IFileSystem fileSystem)
    {
        jsonConverter.CheckArgumentNull(nameof(jsonConverter));
        _jsonConverter = jsonConverter;
        _fileSystem = fileSystem;
    }

    #endregion

    #region Methods: Public

    public PackageInfo GetPackageInfo(string packagePath)
    {
        packagePath.CheckArgumentNullOrWhiteSpace(nameof(packagePath));
        string packageDescriptorPath = PackageUtilities.BuildPackageDescriptorPath(packagePath);
        if (!_fileSystem.File.Exists(packageDescriptorPath))
        {
            throw new Exception($"Package descriptor not found by path: '{packageDescriptorPath}'");
        }
        try
        {
            PackageDescriptorDto packageDescriptorDto =
                _jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath);
            IEnumerable<string> filePaths = _fileSystem.Directory
                                                       .EnumerateFiles(packagePath, "*.*",
                                                           SearchOption.AllDirectories);
            return new PackageInfo(packageDescriptorDto.Descriptor, packagePath, filePaths);
        }
        catch (Exception ex)
        {
            throw new Exception($"Package descriptor is wrong: '{ex.Message}'");
        }
    }

    #endregion

}

#endregion
