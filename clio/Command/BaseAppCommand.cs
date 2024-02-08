using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using CreatioModel;
using OneOf.Types;

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

	private readonly IDataProvider _dataProvider;
	protected readonly ApplicationManager _applicationManager;

	public BaseAppCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings, 
		IDataProvider dataProvider, ApplicationManager applicationManager) 
		: base(applicationClient, environmentSettings){
		_dataProvider = dataProvider;
		_applicationManager = applicationManager;
	}

	protected List<SysInstalledApp> GetApplicationList() =>
		AppDataContextFactory.GetAppDataContext(_dataProvider)
			.Models<SysInstalledApp>()
			.ToList();

		
	protected SysInstalledApp GetAppFromAppName(string name){
		var app = GetApplicationList()
			.FirstOrDefault(a=> a.Name.ToUpper() == name.ToUpper() || a.Code.ToUpper() == name.ToUpper());
		if (app == null) {
			throw new ItemNotFoundException($"Application with name '{name}' not found.");
		}
		return app;
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