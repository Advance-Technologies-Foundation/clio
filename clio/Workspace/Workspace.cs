using System;
using System.IO;
using Clio.Common;

namespace Clio.Workspace
{

	#region Class: Workspace

	public class Workspace : IWorkspace
	{

		#region Constants: Private

		private const string ClioDirectoryName = ".clio";
		private const string WorkspaceSettingsJson = "workspaceSettings.json";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IWorkspaceRestorer _workspaceRestorer;
		private readonly IFileSystem _fileSystem;
		private readonly IJsonConverter _jsonConverter;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly string _rootPath;

		#endregion

		#region Constructors: Public

		public Workspace(EnvironmentSettings environmentSettings, IWorkspaceRestorer workspaceRestorer,
				IJsonConverter jsonConverter, IWorkingDirectoriesProvider workingDirectoriesProvider, 
				IFileSystem fileSystem) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspaceRestorer.CheckArgumentNull(nameof(workspaceRestorer));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_environmentSettings = environmentSettings;
			_workspaceRestorer = workspaceRestorer;
			_jsonConverter = jsonConverter;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_rootPath = _workingDirectoriesProvider.CurrentDirectory;
			_workspaceSettings = new Lazy<WorkspaceSettings>(ReadWorkspaceSettings);
		}

		#endregion

		#region Properties: Private

		private string WorkspaceSettingsPath => Path.Combine(_rootPath, ClioDirectoryName, WorkspaceSettingsJson); 

		#endregion

		#region Properties: Public

		private readonly Lazy<WorkspaceSettings> _workspaceSettings;
		public WorkspaceSettings WorkspaceSettings => _workspaceSettings.Value;

		#endregion

		#region Methods: Private

		private WorkspaceSettings ReadWorkspaceSettings() =>
			_jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath);

		private WorkspaceSettings CreateDefaultWorkspaceSettings() {
			WorkspaceSettings workspaceSettings = new WorkspaceSettings() {
				Name = "",
				ApplicationVersion = ""
			};
			workspaceSettings.Environments.Add("", new WorkspaceEnvironment());
			return workspaceSettings;
		}

		private void CreateClioDirectory() {
			string clioDirectoryPath = Path.Combine(_rootPath, ClioDirectoryName);
			if (Directory.Exists(clioDirectoryPath)) {
				return;
			}
			Directory.CreateDirectory(clioDirectoryPath);
		}

		private void CreateWorkspaceSettingsFile() {
			WorkspaceSettings defaultWorkspaceSettings = CreateDefaultWorkspaceSettings();
			_jsonConverter.SerializeObjectToFile(defaultWorkspaceSettings, WorkspaceSettingsPath);
		}

		#endregion

		#region Methods: Public

		public void Create() {
			CreateClioDirectory();
			CreateWorkspaceSettingsFile();
		}

		public void Restore(string workspaceEnvironmentName) {
			//_workspaceRestorer.Restore(WorkspaceSettings.ApplicationVersion);
		}

		#endregion

	}

	#endregion

}