using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;
using CreatioModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Command.TIDE
{
	internal class LinkWorkspaceWithTideRepositoryCommand : Command<LinkWorkspaceWithTideRepositoryOptions>
	{
		private readonly IDataProvider _dataProvider;
		private readonly ILogger _logger;
		private readonly IWorkspace _workspace;

		public LinkWorkspaceWithTideRepositoryCommand(IDataProvider dataProvider, ILogger logger, IWorkspace workspace) {
			_dataProvider = dataProvider;
			_logger = logger;
			_workspace = workspace;
		}

		public override int Execute(LinkWorkspaceWithTideRepositoryOptions options) {
			var appCode = _workspace.GetWorkspaceApplicationCode();
			var dataContext = AppDataContextFactory.GetAppDataContext(_dataProvider);
			var sysInstalledApp = dataContext.Models<SysInstalledApp>().Where(c => c.Code == appCode).FirstOrDefault();
			var atfRepository = dataContext.GetModel<AtfRepository>(Guid.Parse(options.TideRepositoryID));
			atfRepository.AtfApplicationId = sysInstalledApp.Id;
			dataContext.Save();
			return 0;
		}
	}

	internal class LinkWorkspaceWithTideRepositoryOptions : RemoteCommandOptions
	{
		[Option('r', "repository-id", Required = true, HelpText = "T.I.D.E repository ID")]
		public string TideRepositoryID { get; set; }
	}
}
