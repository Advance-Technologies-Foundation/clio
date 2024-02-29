namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Workspaces;
	using CommandLine;

	#region Class: PushWorkspaceCommandOptions

	[Verb("publish-workspace", Aliases = new string[] { "publishw" }, HelpText = "Publish workspace to zip file")]
	public class PublishWorkspaceCommandOptions
	{
		[Option('f', "zipFileName", Required = true,
			HelpText = "Path to package repository folder", Default = null)]
		public string ZipFileName { get; internal set; }

		[Option('r', "repoPath", Required = true,
			HelpText = "Path to package repository folder", Default = null)]
		public string DestionationFolderPath { get; internal set; }

		[Option("overwrite", Required = false,
			HelpText = "Path to package repository folder", Default = false)]
		public bool Overwrite { get; internal set; }
	}

	#endregion

	#region Class: PushWorkspaceCommand

	public class PublishWorkspaceCommand : Command<PublishWorkspaceCommandOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;

		#endregion

		#region Constructors: Public

		public PublishWorkspaceCommand(IWorkspace workspace) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
		}

		#endregion

		#region Methods: Public

		public override int Execute(PublishWorkspaceCommandOptions options) {
			try {
				_workspace.PublishZipToFolder(options.ZipFileName, options.DestionationFolderPath, options.Overwrite);
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