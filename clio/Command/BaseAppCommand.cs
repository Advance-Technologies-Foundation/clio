using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using CreatioModel;

namespace Clio.Command.PackageCommand;

public class BaseAppCommandOptions : RemoteCommandOptions
{
    [Value(0, MetaName = "Name", Required = true, HelpText = "Application name")]
    public string Name { get; set; }
}

public class BaseAppCommand<T>(
    IApplicationClient applicationClient,
    EnvironmentSettings environmentSettings,
    IDataProvider dataProvider,
    ApplicationManager applicationManager) : RemoteCommand<T>(applicationClient, environmentSettings)
    where T : BaseAppCommandOptions
{
    protected readonly ApplicationManager _applicationManager = applicationManager;
    private readonly IDataProvider _dataProvider = dataProvider;

    protected List<SysInstalledApp> GetApplicationList() =>
        AppDataContextFactory.GetAppDataContext(_dataProvider)
            .Models<SysInstalledApp>()
            .ToList();

    protected SysInstalledApp GetAppFromAppName(string name)
    {
        SysInstalledApp? app = GetApplicationList()
                                   .FirstOrDefault(a =>
                                       a.Name.ToUpper() == name.ToUpper() || a.Code.ToUpper() == name.ToUpper()) ??
                               throw new ItemNotFoundException($"Application with name '{name}' not found.");
        return app;
    }

    protected Guid GetAppIdFromAppName(string name) => GetAppFromAppName(name).Id;

    protected override string GetRequestData(T options)
    {
        if (Guid.TryParse(options.Name, out Guid appid))
        {
            return "\"" + appid + "\"";
        }

        return "\"" + GetAppIdFromAppName(options.Name) + "\"";
    }
}
