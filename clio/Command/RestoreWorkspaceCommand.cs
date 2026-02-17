namespace Clio.Command
{
	using System;
	using System.IO;
	using Clio.Common;
	using Clio.Workspaces;
	using CommandLine;

	#region Class: RestoreWorkspaceOptions

	public class WorkspaceOptions : EnvironmentOptions
	{

		public WorkspaceOptions() {
			IsNugetRestore = true;
			IsCreateSolution = true;
			AddBuildProps = true;
		}

		[Option( "IsNugetRestore", Required = false, HelpText = "True if you need to restore nugget package SDK", Default = true)]
		public bool? IsNugetRestore { get; set; }


		[Option("IsCreateSolution", Required = false, HelpText = "True if you need to create the Solution", Default = true)]
		public bool? IsCreateSolution { get; set; }

		[Option('a', "AppCode", Required = false, HelpText = "Application code")]
		public string AppCode { get; set; }

		[Option("AddBuildProps", Required = false, Default =true, HelpText = "Add build props for dll paths in the project file")]
		public bool AddBuildProps { get; set; }

		
	}

	[Verb("restore-workspace", Aliases = ["restorew", "pullw", "pull-workspace"],
		HelpText = "Restore clio workspace")]
	public class RestoreWorkspaceOptions : WorkspaceOptions
	{

	}

	#endregion

	#region Class: RestoreWorkspaceCommand

	public class RestoreWorkspaceCommand : Command<RestoreWorkspaceOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;
		private readonly ILogger _logger;
		private readonly CreateWorkspaceCommand _createWorkspaceCommand;
		private readonly IClioGateway _clioGateway;

		#endregion

		#region Constructors: Public

		public RestoreWorkspaceCommand(IWorkspace workspace, ILogger logger, CreateWorkspaceCommand createWorkspaceCommand, 
			IClioGateway clioGateway) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
			_logger = logger;
			_createWorkspaceCommand = createWorkspaceCommand;
			_clioGateway = clioGateway;
		}

		#endregion

		#region Methods: Public

		public override int Execute(RestoreWorkspaceOptions options) {
			try {
				_clioGateway.CheckCompatibleVersion("2.0.0.0");
				_workspace.Restore(options);
				_logger.WriteInfo("Done");
				return 0;
			} catch (FileNotFoundException) {
				return _createWorkspaceCommand.Execute(CloneFromRestoreOptions(options));
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}

		private static CreateWorkspaceCommandOptions CloneFromRestoreOptions(RestoreWorkspaceOptions options) =>
			new() {
				IsNugetRestore = options.IsNugetRestore,
				IsCreateSolution = options.IsCreateSolution,
				AppCode = options.AppCode,
				Uri = options.Uri,
				Password = options.Password,
				Login = options.Login,
				IsNetCore = options.IsNetCore,
				Environment = options.Environment,
				Maintainer = options.Maintainer,
				DevMode = options.DevMode,
				WorkspacePathes = options.WorkspacePathes,
				DeveloperModeEnabled = options.DeveloperModeEnabled,
				Safe = options.Safe,
				ClientId = options.ClientId,
				ClientSecret = options.ClientSecret,
				AuthAppUri = options.AuthAppUri,
				IsSilent = options.IsSilent,
				RestartEnvironment = options.RestartEnvironment,
				DbServerUri = options.DbServerUri,
				DbUser = options.DbUser,
				DbPassword = options.DbPassword,
				BackUpFilePath = options.BackUpFilePath,
				DbWorknigFolder = options.DbWorknigFolder,
				DbName = options.DbName,
				Force = options.Force,
				CallbackProcess = options.CallbackProcess,
				AddBuildProps = options.AddBuildProps
			};

		#endregion

	}

	#endregion

}
