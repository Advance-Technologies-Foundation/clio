using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("restart-web-app", Aliases = new[] { "restart" }, HelpText = "Restart a web application")]
public class RestartOptions : RemoteCommandOptions
{
}

public class RestartCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
    : RemoteCommand<RestartOptions>(applicationClient, settings)
{
    protected override string ServicePath => EnvironmentSettings.IsNetCore
        ? @"/ServiceModel/AppInstallerService.svc/RestartApp"
        : @"/ServiceModel/AppInstallerService.svc/UnloadAppDomain";
}
