using System;
using System.IO;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command.PackageCommand;

[Verb("set-pkg-version", Aliases = new[] { "pkgversion" }, HelpText = "Set package version")]
public class SetPackageVersionOptions
{
    [Value(0, MetaName = "PackagePath", Required = true, HelpText = "Package path")]
    public string PackagePath { get; set; }

    [Option('v', "PackageVersion", Required = true, HelpText = "Package version")]
    public string PackageVersion { get; set; }
}

public class SetPackageVersionCommand : Command<SetPackageVersionOptions>
{
    protected readonly IJsonConverter _jsonConverter;

    public SetPackageVersionCommand(IJsonConverter jsonConverter)
    {
        jsonConverter.CheckArgumentNull(nameof(jsonConverter));
        _jsonConverter = jsonConverter;
    }

    public override int Execute(SetPackageVersionOptions options)
    {
        string packageDescriptorPath = Path.Combine(options.PackagePath, CreatioPackage.DescriptorName);
        try
        {
            PackageDescriptorDto dto =
                _jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath);
            dto.Descriptor.PackageVersion = options.PackageVersion;
            dto.Descriptor.ModifiedOnUtc = PackageDescriptor.ConvertToModifiedOnUtc(DateTime.Now);
            _jsonConverter.SerializeObjectToFile(dto, packageDescriptorPath);
        }
        catch (FileNotFoundException)
        {
            throw new Exception($"Package descriptor not found by path: '{packageDescriptorPath}'");
        }

        return 0;
    }
}
