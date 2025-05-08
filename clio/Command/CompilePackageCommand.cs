using System;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("compile-package", HelpText = "Build package command")]
public class CompilePackageOptions : EnvironmentNameOptions
{
    [Value(0, MetaName = "PackageName", Required = true, HelpText = "Specified package name")]
    public string PackageName { get; set; }

    public string[] PackageNames => PackageName.Split(',');
}

public class CompilePackageCommand(IPackageBuilder packageBuilder, ILogger logger) : Command<CompilePackageOptions>
{
    private readonly ILogger _logger = logger;
    private readonly IPackageBuilder _packageBuilder = packageBuilder;

    public override int Execute(CompilePackageOptions options)
    {
        try
        {
            _packageBuilder.Rebuild(options.PackageNames);
            _logger.WriteInfo("Done");
            return 0;
        }
        catch (Exception e)
        {
            _logger.WriteError(e.Message);
            return 1;
        }
    }
}
