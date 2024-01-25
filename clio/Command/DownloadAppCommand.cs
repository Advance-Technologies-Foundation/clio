using System;
using System.IO;
using System.Text.Json;
using ATF.Repository.Providers;
using Clio.Command.PackageCommand;
using Clio.Common;
using CommandLine;

namespace Clio.Command;


[Verb("download-application", Aliases = new[] { "dapp", "download-app" }, HelpText = "Downloads app from environment")]
public class DownloadAppOptions: BaseAppCommandOptions
{

	[Option('f', "FilePath", Required = false, HelpText = "File path to save application")]
	public string FilePath { get; set; }

}

public class DownloadAppCommand : BaseAppCommand<DownloadAppOptions>
{
	protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/ExportApp";
	
	public DownloadAppCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings, ILogger logger, IDataProvider dataProvider,
			ApplicationManager applicationManager) 
		: base(applicationClient, environmentSettings, logger, dataProvider, applicationManager) { }
	
	protected override void ExecuteRemoteCommand(DownloadAppOptions options) {
		_logger.WriteInfo("Downloading application");
		_applicationManager.Download(options.Name, options.Environment, options.FilePath);
	}

}