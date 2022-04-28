using System.IO;
using Clio.Common;
using Clio.Package;
using Clio.Workspace;

namespace Clio.Command
{
	using System;
	using CommandLine;

	#region Class: PushWorkspaceCommandOptions

	[Verb("push-workspace", Aliases = new string[] { "pushw" }, HelpText = "Push workspace to selected environment")]
	public class PushWorkspaceCommandOptions : EnvironmentOptions
	{
	}

	#endregion

	#region Class: PushWorkspaceCommand

	public class PushWorkspaceCommand : Command<PushWorkspaceCommandOptions>
	{

		#region Constants: Private

		private const string PackagesFolderName = "packages";

		#endregion

		#region Fields: Private

		private EnvironmentSettings _environmentSettings;
		private readonly IPackageInstaller _packageInstaller;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IWorkspace _workspace;

		#endregion

		#region Constructors: Public

		public PushWorkspaceCommand(EnvironmentSettings environmentSettings, IPackageInstaller packageInstaller, 
				IPackageArchiver packageArchiver, IWorkingDirectoriesProvider workingDirectoriesProvider, IWorkspace workspace) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			packageInstaller.CheckArgumentNull(nameof(packageInstaller));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			workspace.CheckArgumentNull(nameof(workspace));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			_environmentSettings = environmentSettings;
			_packageInstaller = packageInstaller;
			_packageArchiver = packageArchiver;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_workspace = workspace;
		}

		#endregion

		#region Methods: Private

		#endregion

		#region Methods: Public

		public override int Execute(PushWorkspaceCommandOptions options) {
			try
			{
				WorkspaceSettings workspaceSettings = _workspace.WorkspaceSettings;
				_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
					string rootPackedPackagePath = Path.Combine(tempDirectory, workspaceSettings.Name);
					foreach (string packageName in workspaceSettings.Packages) {
						string packagePath = Path.Combine(workspaceSettings.RootPath, PackagesFolderName, packageName);
						string packedPackagePath = Path.Combine(rootPackedPackagePath, $"{packageName}.gz");
						_packageArchiver.Pack(packagePath, packedPackagePath, true, true);
					}
					string applicationZip = Path.Combine(tempDirectory, $"{workspaceSettings.Name}.zip");
					_packageArchiver.ZipPackages(rootPackedPackagePath, 
						applicationZip, true);
					_packageInstaller.Install(applicationZip);
				});
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