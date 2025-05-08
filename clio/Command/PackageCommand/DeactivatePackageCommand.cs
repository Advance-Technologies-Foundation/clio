using System;

using Clio.Common;
using Clio.Package;

using CommandLine;

namespace Clio.Command.PackageCommand;
[Verb("deactivate-pkg", Aliases = new[] { "dpkg", "deactivate-package", "disable-package" },
    HelpText = "Deactivate package from a web application. Will be available in 8.1.2")]
internal class DeactivatePkgOptions : RemoteCommandOptions
{
    [Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
    public string PackageName { get; set; }
}

internal class DeactivatePackageCommand(IPackageDeactivator packageDeactivator, IApplicationClient applicationClient,
    EnvironmentSettings environmentSettings): RemoteCommand<DeactivatePkgOptions>(applicationClient, environmentSettings)
{
    private readonly IPackageDeactivator _packageDeactivator = packageDeactivator;

    public override int Execute(DeactivatePkgOptions options)
    {
        try
        {
            string packageName = options.PackageName;
            Logger.WriteLine($"Start deactivation package: \"{packageName}\"");
            _packageDeactivator.Deactivate(packageName);
            Logger.WriteLine($"Package \"{packageName}\" successfully deactivated.");
            return 0;
        }
        catch (Exception e)
        {
            Logger.WriteLine(e.Message);
            return 1;
        }
    }
}
