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

    #region Properties: Public

    [Value(0, MetaName = "Name", Required = true, HelpText = "Application name")]
    public string Name { get; set; }

    #endregion

}

public class BaseAppCommand<T> : RemoteCommand<T> where T : BaseAppCommandOptions
{

    #region Fields: Private

    private readonly IDataProvider _dataProvider;

    #endregion

    #region Fields: Protected

    protected readonly ApplicationManager _applicationManager;

    #endregion

    #region Constructors: Public

    public BaseAppCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings,
        IDataProvider dataProvider, ApplicationManager applicationManager)
        : base(applicationClient, environmentSettings)
    {
        _dataProvider = dataProvider;
        _applicationManager = applicationManager;
    }

    #endregion

    #region Methods: Protected

    protected override string GetRequestData(T options)
    {
        if (Guid.TryParse(options.Name, out Guid appid))
        {
            return "\"" + appid + "\"";
        }
        return "\"" + GetAppIdFromAppName(options.Name) + "\"";
    }

    protected SysInstalledApp GetAppFromAppName(string name)
    {
        SysInstalledApp app = GetApplicationList()
            .FirstOrDefault(a => a.Name.ToUpper() == name.ToUpper() || a.Code.ToUpper() == name.ToUpper());
        if (app == null)
        {
            throw new ItemNotFoundException($"Application with name '{name}' not found.");
        }
        return app;
    }

    protected Guid GetAppIdFromAppName(string name) => GetAppFromAppName(name).Id;

    protected List<SysInstalledApp> GetApplicationList() =>
        AppDataContextFactory.GetAppDataContext(_dataProvider)
                             .Models<SysInstalledApp>()
                             .ToList();

    #endregion

}
