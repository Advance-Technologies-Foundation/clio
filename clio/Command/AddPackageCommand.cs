using System;

using CommandLine;
using Common;
using Package;

namespace Clio.Command;

[Verb("add-package", Aliases = new string[] { "ap" }, HelpText = "Add package to workspace or local folder")]
public class AddPackageOptions : EnvironmentOptions
{
    [Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
    public string Name { get; set; }

    [Option('a', "asApp", Required = false,
        HelpText = "Create application in package", Default = false)]
    public bool AsApp { get; set; }
}

public class AddPackageCommand(IPackageCreator packageCreator, ILogger logger): Command<AddPackageOptions>
{
    private readonly IPackageCreator _packageCreator = packageCreator;
    private readonly ILogger _logger = logger;

    public override int Execute(AddPackageOptions options)
    {
        _packageCreator.Create(options.Name, options.AsApp);
        _logger.WriteInfo("Done");
        return 0;
    }
}
