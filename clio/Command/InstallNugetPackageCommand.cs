using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Project.NuGet;
using CommandLine;

namespace Clio.Command;

[Verb("install-nuget-pkg", Aliases = new[] { "installng" },
    HelpText = "Install NuGet package to a web application (website)")]
public class InstallNugetPkgOptions : EnvironmentOptions
{
    [Value(0, MetaName = "Names", Required = true, HelpText = "Packages names")]
    public string Names { get; set; }

    [Option('s', "Source", Required = false, HelpText = "Specifies the server URL",
        Default = "")]
    public string SourceUrl { get; set; }
}

public class InstallNugetPackageCommand : Command<InstallNugetPkgOptions>
{
    private readonly IInstallNugetPackage _installNugetPackage;

    public InstallNugetPackageCommand(IInstallNugetPackage installNugetPackage)
    {
        installNugetPackage.CheckArgumentNull(nameof(installNugetPackage));
        _installNugetPackage = installNugetPackage;
    }

    private IEnumerable<NugetPackageFullName> ParseNugetPackageFullNames(string fullNamesDescription) =>
        fullNamesDescription.Split(',').Select(fullName => new NugetPackageFullName(fullName));

    public override int Execute(InstallNugetPkgOptions options)
    {
        try
        {
            _installNugetPackage.Install(ParseNugetPackageFullNames(options.Names), options.SourceUrl);
            Console.WriteLine("Done");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
    }
}
