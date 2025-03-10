using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Workspaces;
using CommandLine;
using CreatioModel;
using System;
using System.Linq;
using System.Management.Automation;

namespace Clio.Command.TIDE;

[Verb("link-workspace-with-tide-repository", Aliases = ["linkw"], HelpText = "Link workspace with T.I.D.E. repository", Hidden = true)]
public class LinkWorkspaceWithTideRepositoryOptions : RemoteCommandOptions
{
	[Option('r', "repository-id", Required = true, HelpText = "T.I.D.E repository ID")]
	public string TideRepositoryId { get; set; }
}
	
public class LinkWorkspaceWithTideRepositoryCommand : Command<LinkWorkspaceWithTideRepositoryOptions>
{
	private readonly IDataProvider _dataProvider;
	private readonly IWorkspace _workspace;

	public LinkWorkspaceWithTideRepositoryCommand(IDataProvider dataProvider, IWorkspace workspace) {
		_dataProvider = dataProvider;
		_workspace = workspace;
	}

	public override int Execute(LinkWorkspaceWithTideRepositoryOptions options) {
		var appCode = _workspace.GetWorkspaceApplicationCode();
		var dataContext = AppDataContextFactory.GetAppDataContext(_dataProvider);
		var sysInstalledApp = dataContext.Models<SysInstalledApp>().FirstOrDefault(c => c.Code == appCode);
		var atfRepository = dataContext.GetModel<AtfRepository>(Guid.Parse(options.TideRepositoryId));
			
		if(sysInstalledApp != null && sysInstalledApp.Id!=Guid.Empty && atfRepository.Id!=Guid.Empty) {
			atfRepository.AtfApplicationId = sysInstalledApp.Id;
			dataContext.Save();
		}
		else {
			throw new ItemNotFoundException("SysInstalledApp or AtfRepository");
		}
		return 0;
	}
}