using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using CreatioModel;

namespace Clio.Command.PackageCommand;

public class BaseAppCommandOptions : EnvironmentOptions
{

	[Value(0, MetaName = "Name", Required = true, HelpText = "Application name")]
	public string Name
	{
		get; set;
	}
}

public class BaseAppCommand<T>: RemoteCommand<T> where T : BaseAppCommandOptions 
{

	protected readonly ILogger _logger;
	private readonly IDataProvider _dataProvider;

	public BaseAppCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings, 
		ILogger logger, IDataProvider dataProvider) 
		: base(applicationClient, environmentSettings){
		_logger = logger;
		_dataProvider = dataProvider;
	}

	protected List<SysInstalledApp> GetApplicationList() =>
		AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysInstalledApp>()
			.ToList();

		
	protected SysInstalledApp GetAppFromAppName(string name){
		return GetApplicationList()
			.FirstOrDefault(a=> a.Name.ToUpper() == name.ToUpper() || a.Code.ToUpper() == name.ToUpper());
	}
		
	protected Guid GetAppIdFromAppName(string name) => GetAppFromAppName(name).Id;

	protected override string GetRequestData(T options) {
		if (Guid.TryParse(options.Name, out Guid appid)) {
			return "\"" + appid + "\"";
		} else {
			return "\"" + GetAppIdFromAppName(options.Name) + "\"";
		}
	}

}