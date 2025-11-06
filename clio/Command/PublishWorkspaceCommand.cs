namespace Clio.Command
{
	using System;
	using System.IO;
	using Clio.Common;
	using Clio.Workspaces;
	using CommandLine;

	#region Class: PushWorkspaceCommandOptions

	[Verb("publish-app", Aliases = new string[] { "publishw", "publish-hub", "ph", "publish-workspace" }, HelpText = "Publish workspace to zip file")]
	public class PublishWorkspaceCommandOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "WorkspacePath", Required = false,
			HelpText = "Path to application workspace folder")]
		public string PositionalWorkspacePath { get; set; }

		[Option('b', "branch", Required = false,
			HelpText = "Branch name", Default = null)]
		public string Branch { get; set; }

		[Option('h', "app-hub", Required = false,
			HelpText = "Path to application hub", Default = null)]
		public string AppHupPath { get; internal set; }

		[Option('r', "repo-path", Required = false,
			HelpText = "Path to application workspace folder", Default = null)]
		public string WorkspaceFolderPath { get; internal set; }

		[Option('v', "app-version", Required = false,
			HelpText = "Application version", Default = null)]
		public string AppVersion { get; internal set; }

		[Option('a', "app-name", Required = false, HelpText = "Application name", Default = null)]
		public string AppName { get; internal set; }

		[Option('f', "file", Required = false, HelpText = "Target zip file path for published workspace")]
		public string FilePath { get; set; }

		internal string ResolveWorkspacePath() =>
			string.IsNullOrWhiteSpace(WorkspaceFolderPath) ? PositionalWorkspacePath : WorkspaceFolderPath;
	}

	#endregion

	#region Class: PushWorkspaceCommand

	public class PublishWorkspaceCommand : Command<PublishWorkspaceCommandOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;

		#endregion

		#region Constructors: Public

		public PublishWorkspaceCommand(IWorkspace workspace)
		{
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
		}

		#endregion

		#region Methods: Public

		public override int Execute(PublishWorkspaceCommandOptions options)
		{
			try
			{
				var workspacePath = options.ResolveWorkspacePath();
				if (string.IsNullOrWhiteSpace(workspacePath))
				{
					throw new ArgumentException("Workspace path is required. Specify it as the first argument or via --repo-path.");
				}
				workspacePath = Path.GetFullPath(workspacePath);
				bool useFileMode = !string.IsNullOrWhiteSpace(options.FilePath);
				if (useFileMode)
				{
					var version = options.AppVersion;
					var targetPath = Path.GetFullPath(options.FilePath);
					_workspace.PublishToFile(workspacePath, targetPath, version);
				}
				else
				{
					if (string.IsNullOrWhiteSpace(options.AppHupPath))
					{
						throw new ArgumentException("App hub path is required when --file is not specified. Use --app-hub option.");
					}
					if (string.IsNullOrWhiteSpace(options.AppName))
					{
						throw new ArgumentException("Application name is required when --file is not specified. Use --app-name option.");
					}
					_workspace.PublishToFolder(workspacePath, options.AppHupPath, options.AppName, options.AppVersion, options.Branch);
				}
				Console.WriteLine("Done");
				return 0;
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
