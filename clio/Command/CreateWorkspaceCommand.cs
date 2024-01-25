namespace Clio.Command
{
	using System;
	using System.Collections.Generic;
	using Clio.Common;
	using Clio.Workspace;
	using CommandLine;

	#region Class: CreateWorkspaceCommandOptions

	[Verb("create-workspace", Aliases = new string[] { "createw" }, HelpText = "Create open project cmd file")]
	public class CreateWorkspaceCommandOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Option('a', "AppCode", Required = false, HelpText = "Application code")]
		public string AppCode { get; set; }

		#endregion

	}

	#endregion

	#region Class: CreateWorkspaceCommand

	public class CreateWorkspaceCommand : Command<CreateWorkspaceCommandOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;
		private readonly IInstalledApplication _installedApplication;

		#endregion

		#region Constructors: Public

		public CreateWorkspaceCommand(IWorkspace workspace, IInstalledApplication installedApplication) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
			_installedApplication = installedApplication;
		}

		#endregion

		#region Methods: Private

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
						InstalledAppInfo app = _installedApplication.GetInstalledAppInfo(options.AppCode);
						if (app != null) {
							IEnumerable<string> packages = app.GetPackages();
							foreach (string package in packages) {
								_workspace.AddPackageIfNeeded(options.AppCode);
							}
						}
					}
					_workspace.Restore();
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