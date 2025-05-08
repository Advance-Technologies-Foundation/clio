using System.Collections.Generic;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio;

[Verb("compressApp", HelpText = "Compress application command")]
internal class CompressAppOptions
{

    #region Properties: Public

    [Option('d', "DestinationPath", Required = true, HelpText = "Destination folder path for gz files")]
    public string DestinationPath { get; set; }

    [Option('p', "Packages", Required = true)]
    public string Packages { get; set; }

    [Option('s', "SourcePath", Required = true, HelpText = "Folder path to package repository")]
    public string RepositoryFolderPath { get; set; }

    public IEnumerable<string> RootPackageNames => StringParser.ParseArray(Packages);

    [Option("SkipPdb", Required = false, Default = true)]
    public bool SkipPdb { get; set; }

    #endregion

}

internal class CompressAppCommand : Command<CompressAppOptions>
{

    #region Fields: Private

    private readonly IJsonConverter _jsonConverter;
    private readonly IPackageArchiver _packageArchiver;
    private readonly IPackageUtilities _packageUtilities;

    #endregion

    #region Constructors: Public

    public CompressAppCommand(IJsonConverter jsonConverter, IPackageArchiver packageArchiver,
        IPackageUtilities packageUtilities)
    {
        _jsonConverter = jsonConverter;
        _packageArchiver = packageArchiver;
        _packageUtilities = packageUtilities;
    }

    #endregion

    #region Methods: Public

    public override int Execute(CompressAppOptions options)
    {
        FolderPackageRepository folderPackageRepository
            = new(options.RepositoryFolderPath, _jsonConverter, _packageUtilities);
        IEnumerable<string> appPackageNames = folderPackageRepository.GetRelatedPackagesNames(options.RootPackageNames);
        string destinationPath = options.DestinationPath;
        if (!Directory.Exists(destinationPath))
        {
            Directory.CreateDirectory(destinationPath);
        }
        foreach (string appPackageName in appPackageNames)
        {
            string packageContentFolderPath = folderPackageRepository.GetPackageContentFolderPath(appPackageName);
            _packageArchiver.Pack(packageContentFolderPath,
                Path.Combine(options.DestinationPath, $"{appPackageName}.gz"), options.SkipPdb);
        }
        return 0;
    }

    #endregion

}

internal class FolderPackageRepository
{

    #region Fields: Private

    private readonly string _repositoryFolderPath;
    private readonly IJsonConverter _jsonConverter;
    private readonly IPackageUtilities _packageUtilities;

    #endregion

    #region Constructors: Public

    public FolderPackageRepository(string repositoryFolderPath, IJsonConverter jsonConverter,
        IPackageUtilities packageUtilities)
    {
        _jsonConverter = jsonConverter;
        _repositoryFolderPath = repositoryFolderPath;
        _packageUtilities = packageUtilities;
    }

    #endregion

    #region Methods: Private

    private PackageDescriptor GetPackageDescriptor(string packageName)
    {
        string packagePath = GetPackageContentFolderPath(packageName);
        string packageDescriptorPath = PackageUtilities.BuildPackageDescriptorPath(packagePath);
        PackageDescriptor packageDescriptor = _jsonConverter
                                              .DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath)
                                              .Descriptor;
        return packageDescriptor;
    }

    private Stack<PackageDescriptor> GetStackPackageDescriptors(IEnumerable<string> packageNames)
    {
        Stack<PackageDescriptor> processedPackages = new();
        foreach (string packageName in packageNames)
        {
            PackageDescriptor packageDescriptor = GetPackageDescriptor(packageName);
            processedPackages.Push(packageDescriptor);
        }
        return processedPackages;
    }

    #endregion

    #region Methods: Public

    public string GetPackageContentFolderPath(string packageName)
    {
        return _packageUtilities.GetPackageContentFolderPath(_repositoryFolderPath, packageName);
    }

    public IEnumerable<string> GetRelatedPackagesNames(IEnumerable<string> rootPackageNames)
    {
        HashSet<string> relatedPackageDescriptors = new();
        Stack<PackageDescriptor> candidatePackages = GetStackPackageDescriptors(rootPackageNames);
        while (candidatePackages.Count > 0)
        {
            PackageDescriptor candidatePackage = candidatePackages.Pop();
            if (relatedPackageDescriptors.Contains(candidatePackage.Name))
            {
                continue;
            }
            relatedPackageDescriptors.Add(candidatePackage.Name);
            foreach (PackageDependency parent in candidatePackage.DependsOn)
            {
                candidatePackages.Push(GetPackageDescriptor(parent.Name));
            }
        }
        return relatedPackageDescriptors;
    }

    #endregion

}
