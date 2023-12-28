using System;
using System.IO;
using System.Text.Json;
using ATF.Repository.Providers;
using Clio.Command.PackageCommand;
using Clio.Common;
using CommandLine;

namespace Clio.Command;


[Verb("download-application", Aliases = new[] { "dapp" }, HelpText = "Downloads app from environment")]
public class DownloadAppOptions: BaseAppCommandOptions
{

	[Option('f', "FilePath", Required = false, HelpText = "File path to save application")]
	public string FilePath { get; set; }

}

public class DownloadAppCommand : BaseAppCommand<DownloadAppOptions>
{
	protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/ExportApp";
	
	public DownloadAppCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings, ILogger logger, IDataProvider dataProvider) 
		: base(applicationClient, environmentSettings, logger, dataProvider){ }
	
	protected override void ExecuteRemoteCommand(DownloadAppOptions options) {
		_logger.WriteInfo("Downloading application");
		
		var appInfo = GetAppFromAppName(options.Name);
		var data = new {
			appId =  appInfo.Id
		};
		var dataStr = JsonSerializer.Serialize(data);
		string zipFilePath = string.IsNullOrWhiteSpace(options.FilePath) 
			? Path.Combine(Environment.CurrentDirectory, $"{appInfo.Code}_{appInfo.Version}_{DateTime.UtcNow:dd-MMM-yyy_HH-mm}.zip") 
			: options.FilePath;
		
		ApplicationClient.DownloadFile(ServiceUri, zipFilePath, dataStr);
	}

}