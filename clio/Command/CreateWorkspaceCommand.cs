using System.Linq;

namespace Clio.Command
{
	using System;
	using System.Collections.Generic;
	using Clio.Common;
	using Clio.Workspaces;
	using CommandLine;

	#region Class: CreateWorkspaceCommandOptions

	[Verb("create-workspace", Aliases = new string[] { "createw" }, HelpText = "Create open project cmd file")]
	public class CreateWorkspaceCommandOptions : WorkspaceOptions
	{

		#region Properties: Public

		[Option('a', "AppCode", Required = false, HelpText = "Application code")]
		public string AppCode { get; set; }

		internal override bool RequiredEnvironment => false;

		#endregion

	}

	#endregion

	#region Class: CreateWorkspaceCommand

	public class CreateWorkspaceCommand : Command<CreateWorkspaceCommandOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public CreateWorkspaceCommand(IWorkspace workspace, ILogger logger) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
			_logger = logger;
		}

		#endregion

		#region Property: Private


		#endregion

		#region Methods: Public

		public override int Execute(CreateWorkspaceCommandOptions options) {
			try {
				if (options.Environment == null && string.IsNullOrEmpty(options.Uri)) {
					_workspace.Create(options.Environment);
				} else {
					var appCodeNotExists = options.AppCode != null ? false : true;
						_workspace.Create(options.Environment, appCodeNotExists);
					if (!appCodeNotExists) {
						IInstalledApplication installedApplication = Program.Resolve<IInstalledApplication>(options);
						InstalledAppInfo app = installedApplication.GetInstalledAppInfo(options.AppCode);
						if (app != null) {
							IEnumerable<string> packages = app.GetPackages();
							foreach (string package in packages) {
								_workspace.AddPackageIfNeeded(package);
							}
						}
					}
					_workspace.Restore(options);
				}
				_logger.WriteInfo("Done");
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}