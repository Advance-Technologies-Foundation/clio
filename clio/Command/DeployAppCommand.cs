using ATF.Repository.Providers;
using Clio.Command.PackageCommand;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("deploy-application", Aliases = new[] { "deploy-app" },
    HelpText = "Deploy app from current environment to destination environment")]
internal class DeployAppOptions : BaseAppCommandOptions
{
    public string SourceEnvironment
    {
        get => Environment;
        set => Environment = value;
    }

    [Option('d', "DestinationEnvironment", Required = true, HelpText = "Destination environment")]
    public string DestinationEnvironment { get; set; }
}

internal class DeployAppCommand(
    IApplicationClient applicationClient,
    EnvironmentSettings environmentSettings,
    IDataProvider dataProvider,
    ApplicationManager applicationManager) : BaseAppCommand<DeployAppOptions>(applicationClient,
    environmentSettings, dataProvider, applicationManager)
{
    private readonly ApplicationManager _applicationManager = applicationManager;

    protected override void ExecuteRemoteCommand(DeployAppOptions options)
    {
        Logger.WriteInfo("Start deploy application");
        _applicationManager.Deploy(options.Name, options.SourceEnvironment, options.DestinationEnvironment);
    }
}
