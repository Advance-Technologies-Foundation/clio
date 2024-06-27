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

		#endregion

		#region Constructors: Public

		public CreateWorkspaceCommand(IWorkspace workspace) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
		}

		#endregion

		#region Property: Private


		#endregion

		#region Methods: Public

		public override int Execute(CreateWorkspaceCommandOptions options) {
			try {
				if (options.Environment == null) {
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
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}