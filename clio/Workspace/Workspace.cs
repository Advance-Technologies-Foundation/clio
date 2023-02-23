using System.Threading;

namespace Clio.Workspace
{
	using System;
    using Clio.Common;

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

		#endregion

		#region Constructors: Public

		public Workspace(EnvironmentSettings environmentSettings, IWorkspacePathBuilder workspacePathBuilder,
				IWorkspaceCreator workspaceCreator, IWorkspaceRestorer workspaceRestorer,
				IWorkspaceInstaller workspaceInstaller, IWorkspaceSolutionCreator workspaceSolutionCreator,
				IJsonConverter jsonConverter) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
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
			ResetLazyWorkspaceSettings();
		}

		#endregion

		#region Properties: Private

		private string WorkspaceSettingsPath => _workspacePathBuilder.WorkspaceSettingsPath;

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

		#endregion

		#region Methods: Public

		public void SaveWorkspaceSettings() {
			_jsonConverter.SerializeObjectToFile(WorkspaceSettings, WorkspaceSettingsPath);
			ResetLazyWorkspaceSettings();
		}

		public void Create(string environmentName, bool isAddingPackageNames = false) {
			_workspaceCreator.Create(environmentName, isAddingPackageNames);
		}

		public void Restore() {
			_workspaceRestorer.Restore(WorkspaceSettings, _environmentSettings);
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


		#endregion

	}

	#endregion

}