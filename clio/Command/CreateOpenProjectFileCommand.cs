namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Project.NuGet;
	using Clio.Workspace;
	using CommandLine;

	#region Class: CreateOpenProjectFileOptions

	[Verb("create-open-project-file", Aliases = new string[] { "open" }, HelpText = "Create open project cmd file")]
	public class CreateOpenProjectFileOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "PackagePath", Required = true, HelpText = "Path of package folder")]
		public string PackagePath { get; set; }

		[Option('v', "Version", Required = false, HelpText = "Version application", 
			Default = PackageVersion.LastVersion)]
		public string Version { get; set; }

		#endregion

	}

	#endregion

	#region Class: CreateOpenProjectFileOptions

	public class CreateOpenProjectFileCommand : Command<CreateOpenProjectFileOptions>
	{

		#region Fields: Private

		private readonly IWorkspaceRestorer _workspaceRestorer;

		#endregion

		#region Constructors: Public

		public CreateOpenProjectFileCommand(IWorkspaceRestorer workspaceRestorer) {
			workspaceRestorer.CheckArgumentNull(nameof(workspaceRestorer));
			_workspaceRestorer = workspaceRestorer;
		}

		#endregion

		#region Methods: Public

		public override int Execute(CreateOpenProjectFileOptions options) {
			try {
				string nugetCreatioSdkVersion = options.Version;
				_workspaceRestorer.Restore(nugetCreatioSdkVersion);
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