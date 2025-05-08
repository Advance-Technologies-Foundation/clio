using System;
using System.IO;

using Clio.Common;
using CommandLine;

namespace Clio.Command.PackageCommand;

[Verb("extract-pkg-zip", Aliases =["extract", "unzip"], HelpText = "Prepare an archive of creatio package")]
public class UnzipPkgOptions
{
    [Option('d', "DestinationPath", Required = false, HelpText = "Destination path for package folder")]
    public string DestinationPath { get; set; }

    [Value(0, MetaName = "Name", Required = false, HelpText = "Name of the compressed package")]
    public string Name { get; set; }
}

public class ExtractPackageCommand : Command<UnzipPkgOptions>
{
    private readonly IPackageArchiver _packageArchiver;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;

    public ExtractPackageCommand(IPackageArchiver packageArchiver, IFileSystem fileSystem, ILogger logger)
    {
        packageArchiver.CheckArgumentNull(nameof(packageArchiver));
        fileSystem.CheckArgumentNull(nameof(fileSystem));
        _packageArchiver = packageArchiver;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    private static bool CheckDirectory(string dir, out FileInfo[] files)
    {
        bool result = false;
        files = null;
        if (Directory.Exists(dir))
        {
            DirectoryInfo directoryInfo = new (dir);
            files = directoryInfo.GetFiles("*.gz");
            result = files.Length > 0;
        }

        return result;
    }

    private string GetCorrectedPackagePath(UnzipPkgOptions options)
    {
        string packagePath = options.Name;
        if (!_packageArchiver.IsGzArchive(packagePath) && !_packageArchiver.IsZipArchive(packagePath))
        {
            packagePath += ".gz";
        }

        return packagePath;
    }

    private void Unpack(string destinationPath, string packagePath, bool isShowDialogOverwrite = false)
    {
        string destinationDirectory = _fileSystem.GetCurrentDirectoryIfEmpty(destinationPath);
        string packageName = _fileSystem.ExtractFileNameFromPath(packagePath);
        _logger.WriteInfo($"Start unzip package ({packageName}).");
        if (_packageArchiver.IsZipArchive(packagePath))
        {
            _packageArchiver.UnZipPackages(packagePath, true, true, true,
                isShowDialogOverwrite, destinationDirectory);
        }
        else
        {
            _packageArchiver.Unpack(packagePath, true, isShowDialogOverwrite, destinationDirectory);
        }

        _logger.WriteInfo($"Unzip package ({packageName}) completed.");
    }

    public override int Execute(UnzipPkgOptions options)
    {
        try
        {
            if (CheckDirectory(options.Name, out FileInfo[] pkgFiles))
            {
                foreach (FileInfo item in pkgFiles)
                {
                    Unpack(options.DestinationPath, item.FullName, true);
                }
            }
            else
            {
                string packagePath = GetCorrectedPackagePath(options);
                Unpack(options.DestinationPath, packagePath, true);
            }

            return 0;
        }
        catch (Exception e)
        {
            _logger.WriteError(e.Message);
            return 1;
        }
    }
}
