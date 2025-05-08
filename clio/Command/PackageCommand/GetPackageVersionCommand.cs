using System;
using System.IO;

using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command.PackageCommand;

[Verb("get-pkg-version", Aliases = new string[] { "getpkgversion" }, HelpText = "Get package version")]
public class GetPackageVersionOptions
{
    [Value(0, MetaName = "PackagePath", Required = true, HelpText = "Package path")]
    public string PackagePath { get; set; }
}

public class GetPackageVersionCommand : Command<GetPackageVersionOptions>
{
    protected readonly IJsonConverter _jsonConverter;
    private readonly ILogger _logger;

    public GetPackageVersionCommand(IJsonConverter jsonConverter, ILogger logger)
    {
        jsonConverter.CheckArgumentNull(nameof(jsonConverter));
        _jsonConverter = jsonConverter;
        _logger = logger;
    }

    public override int Execute(GetPackageVersionOptions options)
    {
        string packageDescriptorPath = Path.Combine(options.PackagePath, CreatioPackage.DescriptorName);
        try
        {
            PackageDescriptorDto dto =
                _jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath);
            _logger.WriteInfo(dto.Descriptor.PackageVersion);
        }
        catch (FileNotFoundException)
        {
            throw new Exception($"Package descriptor not found by path: '{packageDescriptorPath}'");
        }

        return 0;
    }
}
