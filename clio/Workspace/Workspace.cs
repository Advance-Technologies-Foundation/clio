using System.Threading;

namespace Clio.Workspaces
{
	using System;
	using System.IO;
	using Clio.Command;
	using Clio.Common;
	using Clio.ComposableApplication;
	using Clio.UserEnvironment;


	#region Class: Workspace

	public class Workspace : IWorkspace
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;

		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IWorkspaceCreator _workspaceCreator;
		private readonly IWorkspaceRestorer _workspaceRestorer;
		private readonly IWorkspaceInstaller _workspaceInstaller;
		private readonly IWorkspaceSolutionCreator _workspaceSolutionCreator;
		private readonly IJsonConverter _jsonConverter;
		private IComposableApplicationManager _composableApplicationManager;

		#endregion

		#region Constructors: Public

		public Workspace(EnvironmentSettings environmentSettings, IWorkspacePathBuilder workspacePathBuilder,
				IWorkspaceCreator workspaceCreator, IWorkspaceRestorer workspaceRestorer,
				IWorkspaceInstaller workspaceInstaller, IWorkspaceSolutionCreator workspaceSolutionCreator,
				IJsonConverter jsonConverter, IComposableApplicationManager composableApplicationManager) {
			//environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			workspaceCreator.CheckArgumentNull(nameof(workspaceCreator));
			workspaceRestorer.CheckArgumentNull(nameof(workspaceRestorer));
			workspaceInstaller.CheckArgumentNull(nameof(workspaceInstaller));
			workspaceSolutionCreator.CheckArgumentNull(nameof(workspaceSolutionCreator));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			_environmentSettings = environmentSettings;
			_workspacePathBuilder = workspacePathBuilder;
			_workspaceCreator = workspaceCreator;
			_workspaceRestorer = workspaceRestorer;
			_workspaceInstaller = workspaceInstaller;
			_workspaceSolutionCreator = workspaceSolutionCreator;
			_jsonConverter = jsonConverter;
			_composableApplicationManager = composableApplicationManager;
			ResetLazyWorkspaceSettings();
		}

		#endregion

		#region Properties: Private

		private string WorkspaceSettingsPath => _workspacePathBuilder.WorkspaceSettingsPath;

		private string WorkspaceEnvironmentSettingsPath => _workspacePathBuilder.WorkspaceEnvironmentSettingsPath;

		#endregion

		#region Properties: Public

		private Lazy<WorkspaceSettings> _workspaceSettings;
		public WorkspaceSettings WorkspaceSettings => _workspaceSettings.Value;
		public bool IsWorkspace => _workspacePathBuilder.IsWorkspace;

		#endregion

		#region Methods: Private

		private void ResetLazyWorkspaceSettings() {
			_workspaceSettings = new Lazy<WorkspaceSettings>(ReadWorkspaceSettings);
		}

		private WorkspaceSettings ReadWorkspaceSettings() =>
			_jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath);

		private WorkspaceEnvironmentSettings ReadWorkspaceEnvironmentSettings() =>
			_jsonConverter.DeserializeObjectFromFile<WorkspaceEnvironmentSettings>(WorkspaceEnvironmentSettingsPath);

		#endregion

		#region Methods: Public

		public void SaveWorkspaceEnvironment(string environmentName) {
			_workspaceCreator.SaveWorkspaceEnvironmentSettings(environmentName);
		}

		public void SaveWorkspaceSettings() {
			_jsonConverter.SerializeObjectToFile(WorkspaceSettings, WorkspaceSettingsPath);
			ResetLazyWorkspaceSettings();
		}

		public void Create(string environmentName, bool isAddingPackageNames = false) {
			_workspaceCreator.Create(environmentName, isAddingPackageNames);
		}

		public void Restore(WorkspaceOptions restoreWorkspaceOptions) {
			_workspaceRestorer.Restore(WorkspaceSettings, _environmentSettings, restoreWorkspaceOptions);
		}

		public void Install(string creatioPackagesZipName = null) =>
			_workspaceInstaller.Install(WorkspaceSettings.Packages, creatioPackagesZipName);

		public void AddPackageIfNeeded(string packageName) {
			if (!IsWorkspace) {
				return;
			}
			var workspacePackages = WorkspaceSettings.Packages;
			if (workspacePackages.Contains(packageName)) {
				return;
			}
			workspacePackages.Add(packageName);
			SaveWorkspaceSettings();
			_workspaceSolutionCreator.Create();
		}

		public void PublishZipToFolder(string zipFileName, string destionationFolderPath, bool overrideFile) {
			_workspaceInstaller.Publish(WorkspaceSettings.Packages, zipFileName, destionationFolderPath, overrideFile);
		}

	

		public string PublishToFolder(string workspacePath, string appStorePath, string appName, string appVersion, string branch = null) {
			var hasBranch = !string.IsNullOrEmpty(branch);
			var branchFolderName = hasBranch ? GetSanitizeFileNameFromString(branch) : null;
			_workspacePathBuilder.RootPath = workspacePath;
			var packagesFolderPath = _workspacePathBuilder.PackagesFolderPath;
			_composableApplicationManager.TrySetVersion(workspacePath, appVersion);
			string zipFileName = $"{appName}_{appVersion}";
			if (hasBranch) {
				zipFileName = $"{appName}_{branch}_{appVersion}";
			}
			string destinationFolderPath = hasBranch ? Path.Combine(appStorePath, appName, branchFolderName)
				: Path.Combine(appStorePath, appName, appVersion);
			string sanitizeFileName = GetSanitizeFileNameFromString(zipFileName);
			return _workspaceInstaller.PublishToFolder(workspacePath, sanitizeFileName, destinationFolderPath, false);
		}


		#endregion

		public static string GetSanitizeFileNameFromString(string fileName) {
			return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
		}
	}

	#endregion

}