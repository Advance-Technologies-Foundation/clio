using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Package;
using Clio.Package.Responses;
using CommandLine;

namespace Clio.Command.PackageCommand;

[Verb("activate-pkg", Aliases = new[] { "apkg", "activate-package", "enable-package" },
    HelpText = "Activate package in a web application. Will be available in 8.1.2")]
internal class ActivatePkgOptions : RemoteCommandOptions
{
    [Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
    public string PackageName { get; init; }
}

internal class ActivatePackageCommand(
    IPackageActivator packageActivator,
    IApplicationClient applicationClient,
    EnvironmentSettings environmentSettings,
    ILogger logger) : RemoteCommand<ActivatePkgOptions>(applicationClient, environmentSettings)
{
    private readonly ILogger _logger = logger;
    private readonly IPackageActivator _packageActivator = packageActivator;

    public override int Execute(ActivatePkgOptions options)
    {
        try
        {
            string packageName = options.PackageName;
            _logger.WriteLine($"Start activation package: \"{packageName}\"");
            IEnumerable<PackageActivationResultDto> activationResults = _packageActivator.Activate(packageName);
            foreach (PackageActivationResultDto activationResult in activationResults)
            {
                string message = activationResult.Success switch
                {
                    true when string.IsNullOrEmpty(activationResult.Message) =>
                        $"Package \"{activationResult.PackageName}\" successfully activated.",
                    true when !string.IsNullOrEmpty(activationResult.Message) =>
                        $"Package \"{activationResult.PackageName}\" was activated with errors.",
                    _ => $"Package \"{activationResult.PackageName}\" was not activated."
                };
                _logger.WriteLine(message);
            }

            return 0;
        }
        catch (Exception e)
        {
            _logger.WriteLine(e.Message);
            return 1;
        }
    }
}
