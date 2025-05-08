using ATF.Repository.Providers;
using Clio.Command.PackageCommand;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("download-application", Aliases = new[]
{
    "dapp", "download-app"
}, HelpText = "Downloads app from environment")]
public class DownloadAppOptions : BaseAppCommandOptions
{

    #region Properties: Public

    [Option('f', "FilePath", Required = false, HelpText = "File path to save application")]
    public string FilePath { get; set; }

    #endregion

}

public class DownloadAppCommand : BaseAppCommand<DownloadAppOptions>
{

    #region Constructors: Public

    public DownloadAppCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings,
        IDataProvider dataProvider,
        ApplicationManager applicationManager)
        : base(applicationClient, environmentSettings, dataProvider, applicationManager)
    { }

    #endregion

    #region Properties: Protected

    protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/ExportApp";

    #endregion

    #region Methods: Protected

    protected override void ExecuteRemoteCommand(DownloadAppOptions options)
    {
        Logger.WriteInfo("Downloading application");
        _applicationManager.Download(options.Name, options.Environment, options.FilePath);
    }

    #endregion

}
