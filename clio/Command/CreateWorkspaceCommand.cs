using System.IO;
using Clio.Workspace;

namespace Clio.Command
{
	using System;
	using Clio.Common;
	using Clio.Project.NuGet;
	using CommandLine;

	#region Class: CreateOpenProjectFileOptions

	[Verb("create-workspace", Aliases = new string[] { "createw" }, HelpText = "Create open project cmd file")]
	public class CreateWorkspaceCommandOptions : EnvironmentOptions
	{

		
	}

	#endregion

	#region Class: CreateOpenProjectFileOptions

	public class CreateWorkspaceCommand : Command<CreateWorkspaceCommandOptions>
	{
		#region Constants: Private

		

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IFileSystem _fileSystem;
		private readonly IJsonConverter _jsonConverter;

		#endregion

		#region Constructors: Public

		public CreateWorkspaceCommand(EnvironmentSettings environmentSettings, IFileSystem fileSystem, 
				IJsonConverter jsonConverter) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			_environmentSettings = environmentSettings;
			_fileSystem = fileSystem;
			_jsonConverter = jsonConverter;
		}

		#endregion

		#region Methods: Private

		#endregion

		#region Methods: Public

		public override int Execute(CreateWorkspaceCommandOptions options) {
			try
			{
				string rootPath = _fileSystem.GetCurrentDirectory();
				WorkspaceSettings workspaceSettings = new WorkspaceSettings() {
					Name = "workspaceName",
				};
				workspaceSettings.Environments.Add("Name", new WorkspaceEnvironment());
				string jsonPath = Path.Combine(rootPath, $"workspaceSettings.json");
				_jsonConverter.SerializeObjectToFile(workspaceSettings, jsonPath);
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