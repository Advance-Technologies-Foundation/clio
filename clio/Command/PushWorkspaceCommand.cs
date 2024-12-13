namespace Clio.Command
{
	using System;
	using System.Linq;
	using Clio.Common;
	using Clio.Workspaces;
	using CommandLine;

	#region Class: PushWorkspaceCommandOptions

	[Verb("push-workspace", Aliases = new string[] { "pushw" }, HelpText = "Push workspace to selected environment")]
	public class PushWorkspaceCommandOptions : EnvironmentOptions
	{
		[Option("unlock", Required = false, HelpText = "Unlock workspace package after install workspace to the environment")]
		public bool NeedUnlockPackage { get; set; }
	}

	#endregion

	#region Class: PushWorkspaceCommand

	public class PushWorkspaceCommand : Command<PushWorkspaceCommandOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;
		private UnlockPackageCommand _unlockPackageCommand;

		#endregion

		#region Constructors: Public

		public PushWorkspaceCommand(IWorkspace workspace, UnlockPackageCommand unlockPackageCommand) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
			_unlockPackageCommand = unlockPackageCommand;
		}

		#endregion

		#region Methods: Public

		public override int Execute(PushWorkspaceCommandOptions options) {
			try
			{
				Console.WriteLine("Push workspace...");
				_workspace.Install();
				if (options.NeedUnlockPackage) {
					var unlockPackageCommandOptions = new UnlockPackageOptions();
					unlockPackageCommandOptions.CopyFromEnvironmentSettings(options);
					unlockPackageCommandOptions.Name = string.Join(',', _workspace.WorkspaceSettings.Packages);
					Console.WriteLine("Unlock packages...");
					_unlockPackageCommand.Execute(unlockPackageCommandOptions);
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