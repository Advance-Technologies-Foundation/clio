using System;
using System.IO;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command.PackageCommand;

#region Class: GetPackageVersionOptions

[Verb("get-pkg-version", Aliases = new[]
{
    "getpkgversion"
}, HelpText = "Get package version")]
public class GetPackageVersionOptions
{

    #region Properties: Public

    [Value(0, MetaName = "PackagePath", Required = true, HelpText = "Package path")]
    public string PackagePath { get; set; }

    #endregion

}

#endregion

#region Class: GetPackageVersionCommand

public class GetPackageVersionCommand : Command<GetPackageVersionOptions>
{

    #region Fields: Private

    private readonly ILogger _logger;

    #endregion

    #region Fields: Protected

    protected readonly IJsonConverter _jsonConverter;

    #endregion

    #region Constructors: Public

    public GetPackageVersionCommand(IJsonConverter jsonConverter, ILogger logger)
    {
        jsonConverter.CheckArgumentNull(nameof(jsonConverter));
        _jsonConverter = jsonConverter;
        _logger = logger;
    }

    #endregion

    #region Methods: Public

    public override int Execute(GetPackageVersionOptions options)
    {
        string packageDescriptorPath = Path.Combine(options.PackagePath, CreatioPackage.DescriptorName);
        try
        {
            PackageDescriptorDto dto
                = _jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath);
            _logger.WriteInfo(dto.Descriptor.PackageVersion);
        }
        catch (FileNotFoundException)
        {
            throw new Exception($"Package descriptor not found by path: '{packageDescriptorPath}'");
        }
        return 0;
    }

    #endregion

}

#endregion
