using System.Collections.Generic;
using System.IO;
using System.Linq;

using Clio.Command;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio;

[Verb("compressApp", HelpText = "Compress application command")]
internal class CompressAppOptions
{
    [Option('s', "SourcePath", Required = true, HelpText = "Folder path to package repository")]
    public string RepositoryFolderPath { get; set; }

    [Option('p', "Packages", Required = true)]
    public string Packages { get; set; }

    [Option('d', "DestinationPath", Required = true, HelpText = "Destination folder path for gz files")]
    public string DestinationPath { get; set; }

    [Option("SkipPdb", Required = false, Default = true)]
    public bool SkipPdb { get; set; }

    public IEnumerable<string> RootPackageNames => StringParser.ParseArray(Packages);
}

internal class CompressAppCommand(IJsonConverter jsonConverter, IPackageArchiver packageArchiver,
    IPackageUtilities packageUtilities): Command<CompressAppOptions>
{
    private readonly IJsonConverter _jsonConverter = jsonConverter;
    private readonly IPackageArchiver _packageArchiver = packageArchiver;
    private readonly IPackageUtilities _packageUtilities = packageUtilities;

    public override int Execute(CompressAppOptions options)
    {
        FolderPackageRepository folderPackageRepository =
            new (options.RepositoryFolderPath, _jsonConverter, _packageUtilities);
        IEnumerable<string> appPackageNames = folderPackageRepository.GetRelatedPackagesNames(options.RootPackageNames);
        string destinationPath = options.DestinationPath;
        if (!Directory.Exists(destinationPath))
        {
            Directory.CreateDirectory(destinationPath);
        }

        foreach (string appPackageName in appPackageNames)
        {
            string packageContentFolderPath = folderPackageRepository.GetPackageContentFolderPath(appPackageName);
            _packageArchiver.Pack(
                packageContentFolderPath,
                Path.Combine(options.DestinationPath, $"{appPackageName}.gz"), options.SkipPdb);
        }

        return 0;
    }
}

internal class FolderPackageRepository(string repositoryFolderPath, IJsonConverter jsonConverter,
    IPackageUtilities packageUtilities)
{
    private readonly string _repositoryFolderPath = repositoryFolderPath;
    private readonly IJsonConverter _jsonConverter = jsonConverter;
    private readonly IPackageUtilities _packageUtilities = packageUtilities;

    public IEnumerable<string> GetRelatedPackagesNames(IEnumerable<string> rootPackageNames)
    {
        HashSet<string> relatedPackageDescriptors = [];
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

    private Stack<PackageDescriptor> GetStackPackageDescriptors(IEnumerable<string> packageNames)
    {
        Stack<PackageDescriptor> processedPackages = new ();
        foreach (string packageName in packageNames)
        {
            PackageDescriptor packageDescriptor = GetPackageDescriptor(packageName);
            processedPackages.Push(packageDescriptor);
        }

        return processedPackages;
    }

    public string GetPackageContentFolderPath(string packageName) =>
        _packageUtilities.GetPackageContentFolderPath(_repositoryFolderPath, packageName);

    private PackageDescriptor GetPackageDescriptor(string packageName)
    {
        string packagePath = GetPackageContentFolderPath(packageName);
        string packageDescriptorPath = PackageUtilities.BuildPackageDescriptorPath(packagePath);
        PackageDescriptor packageDescriptor = _jsonConverter
            .DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath)
            .Descriptor;
        return packageDescriptor;
    }
}
