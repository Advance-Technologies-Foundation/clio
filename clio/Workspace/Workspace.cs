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
		private readonly IJsonConverter _jsonConverter;

		#endregion

		#region Constructors: Public

		public Workspace(EnvironmentSettings environmentSettings, IWorkspacePathBuilder workspacePathBuilder,
				IWorkspaceCreator workspaceCreator, IWorkspaceRestorer workspaceRestorer,
				IWorkspaceInstaller workspaceInstaller, IJsonConverter jsonConverter) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			workspaceCreator.CheckArgumentNull(nameof(workspaceCreator));
			workspaceRestorer.CheckArgumentNull(nameof(workspaceRestorer));
			workspaceInstaller.CheckArgumentNull(nameof(workspaceInstaller));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			_environmentSettings = environmentSettings;
			_workspacePathBuilder = workspacePathBuilder;
			_workspaceCreator = workspaceCreator;
			_workspaceRestorer = workspaceRestorer;
			_workspaceInstaller = workspaceInstaller;
			_jsonConverter = jsonConverter;
			_workspaceSettings = new Lazy<WorkspaceSettings>(ReadWorkspaceSettings);
		}

		#endregion

		#region Properties: Private

		private string WorkspaceSettingsPath => _workspacePathBuilder.WorkspaceSettingsPath;


		#endregion

		#region Properties: Public

		private readonly Lazy<WorkspaceSettings> _workspaceSettings;
		public WorkspaceSettings WorkspaceSettings => _workspaceSettings.Value;

		#endregion

		#region Methods: Private


		private WorkspaceSettings ReadWorkspaceSettings() =>
			_jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath);

		#endregion

		#region Methods: Public

		public void Create(bool isAddingPackageNames = false) {
			_workspaceCreator.Create(isAddingPackageNames);
		}

		public void Restore() {
			_workspaceRestorer.Restore(WorkspaceSettings, _environmentSettings);
		}

		public void Install(string creatioPackagesZipName = null) =>
			_workspaceInstaller.Install(WorkspaceSettings.Packages, creatioPackagesZipName);

		#endregion

	}

	#endregion

}