using System;
using System.Linq;
using System.Management.Automation;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Workspaces;
using CommandLine;
using CreatioModel;

namespace Clio.Command.TIDE;

[Verb("link-workspace-with-tide-repository", Aliases = ["linkw"], HelpText = "Link workspace with T.I.D.E. repository",
    Hidden = true)]
public class LinkWorkspaceWithTideRepositoryOptions : RemoteCommandOptions
{

    #region Properties: Public

    [Option('r', "repository-id", Required = true, HelpText = "T.I.D.E repository ID")]
    public string TideRepositoryId { get; set; }

    #endregion

}

public class LinkWorkspaceWithTideRepositoryCommand : Command<LinkWorkspaceWithTideRepositoryOptions>
{

    #region Fields: Private

    private readonly IDataProvider _dataProvider;
    private readonly IWorkspace _workspace;

    #endregion

    #region Constructors: Public

    public LinkWorkspaceWithTideRepositoryCommand(IDataProvider dataProvider, IWorkspace workspace)
    {
        _dataProvider = dataProvider;
        _workspace = workspace;
    }

    #endregion

    #region Methods: Public

    public override int Execute(LinkWorkspaceWithTideRepositoryOptions options)
    {
        string appCode = _workspace.GetWorkspaceApplicationCode();
        IAppDataContext dataContext = AppDataContextFactory.GetAppDataContext(_dataProvider);
        SysInstalledApp sysInstalledApp = dataContext.Models<SysInstalledApp>().FirstOrDefault(c => c.Code == appCode);
        AtfRepository atfRepository = dataContext.GetModel<AtfRepository>(Guid.Parse(options.TideRepositoryId));

        if (sysInstalledApp != null && sysInstalledApp.Id != Guid.Empty && atfRepository.Id != Guid.Empty)
        {
            atfRepository.AtfApplicationId = sysInstalledApp.Id;
            dataContext.Save();
        }
        else
        {
            throw new ItemNotFoundException("SysInstalledApp or AtfRepository");
        }
        return 0;
    }

    #endregion

}
