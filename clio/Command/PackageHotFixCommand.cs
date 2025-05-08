using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("pkg-hotfix", Aliases = new[] { "hotfix", "hf" }, HelpText = "Enable/disable hotfix state for package.")]
public class PackageHotFixCommandOptions : RemoteCommandOptions
{
    [Value(0, MetaName = "PackageName", Required = true, HelpText = "Package name")]
    public string PackageName { get; set; }

    [Value(1, MetaName = "HotFixState", Required = true, HelpText = "HotFix state")]
    public bool Enable { get; internal set; }
}

public class PackageHotFixCommand(IPackageEditableMutator packageEditableMutator,
    EnvironmentSettings environmentSettings): RemoteCommand<PackageHotFixCommandOptions>(environmentSettings)
{
    private readonly IPackageEditableMutator _packageEditableMutator = packageEditableMutator;

    public override int Execute(PackageHotFixCommandOptions commandOptions)
    {
        if (commandOptions.Enable)
        {
            Logger.WriteInfo($"Enable hotfix state for package: \"{commandOptions.PackageName}\"");
        }
        else
        {
            Logger.WriteInfo($"Disable hotfix state for package: \"{commandOptions.PackageName}\"");
        }

        _packageEditableMutator.SetPackageHotfix(commandOptions.PackageName, commandOptions.Enable);
        Logger.WriteInfo($"Done");
        return 0;
    }
}
