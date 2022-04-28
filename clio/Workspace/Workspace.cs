using System.IO;
using Clio.Common;

namespace Clio.Workspace
{

	#region Class: Workspace

	public class Workspace : IWorkspace
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IFileSystem _fileSystem;
		private readonly IJsonConverter _jsonConverter;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly string _rootPath;

		#endregion

		#region Constructors: Public

		public Workspace(EnvironmentSettings environmentSettings, IJsonConverter jsonConverter, 
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_environmentSettings = environmentSettings;
			_jsonConverter = jsonConverter;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_rootPath = _workingDirectoriesProvider.CurrentDirectory;
		}

		#endregion

		#region Properties: Private



		#endregion

		#region Properties: Public

		public WorkspaceSettings WorkspaceSettings { get; }

		#endregion

		#region Methods: Public

		private WorkspaceSettings CreateDefaultWorkspaceSettings() {
			WorkspaceSettings workspaceSettings = new WorkspaceSettings() {
				Name = "",
			};
			workspaceSettings.Environments.Add("", new WorkspaceEnvironment());
			return workspaceSettings;
		}

		private void CreateClioDirectory() {
		}

		#endregion

		#region Methods: Public

		public void Create() {
			WorkspaceSettings defaultWorkspaceSettings = CreateDefaultWorkspaceSettings();
			string jsonPath = Path.Combine(_rootPath, $"workspaceSettings.json");
			_jsonConverter.SerializeObjectToFile(defaultWorkspaceSettings, jsonPath);
		}

		#endregion

	}

	#endregion

}