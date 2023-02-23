using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using Clio.Utilities;

namespace Clio.Workspace
{

	#region Interface: IWorkspaceCreator

	public interface IWorkspaceCreator
	{

		#region Methods: Public

		void Create(string environmentName, bool isAddingPackageNames = false);

		#endregion

	}

	#endregion

	#region Class: WorkspaceCreator

	public class WorkspaceCreator : IWorkspaceCreator
	{

		#region Fields: Private

		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly ICreatioSdk _creatioSdk;
		private readonly ITemplateProvider _templateProvider;
		private readonly IJsonConverter _jsonConverter;
		private readonly IFileSystem _fileSystem;
		private readonly IApplicationPackageListProvider _applicationPackageListProvider;
		private readonly IExecutablePermissionsActualizer _executablePermissionsActualizer;
		private readonly IOSPlatformChecker _osPlatformChecker;

		#endregion

		#region Constructors: Public

		public WorkspaceCreator(IWorkspacePathBuilder workspacePathBuilder, ICreatioSdk creatioSdk,
				ITemplateProvider templateProvider, IJsonConverter jsonConverter, IFileSystem fileSystem,
				IApplicationPackageListProvider applicationPackageListProvider, 
				IExecutablePermissionsActualizer executablePermissionsActualizer,
				IOSPlatformChecker osPlatformChecker) {
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			creatioSdk.CheckArgumentNull(nameof(creatioSdk));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			executablePermissionsActualizer.CheckArgumentNull(nameof(executablePermissionsActualizer));
			osPlatformChecker.CheckArgumentNull(nameof(osPlatformChecker));
			_workspacePathBuilder = workspacePathBuilder;
			_creatioSdk = creatioSdk;
			_templateProvider = templateProvider;
			_jsonConverter = jsonConverter;
			_fileSystem = fileSystem;
			_applicationPackageListProvider = applicationPackageListProvider;
			_executablePermissionsActualizer = executablePermissionsActualizer;
			_osPlatformChecker = osPlatformChecker;
		}

		#endregion

		#region Properties: Private

		private string RootPath => _workspacePathBuilder.RootPath;
		private string WorkspaceSettingsPath => _workspacePathBuilder.WorkspaceSettingsPath;
		private string WorkspaceEnvironmentSettingsPath => _workspacePathBuilder.WorkspaceEnvironmentSettingsPath;
		private bool IsWorkspace => _workspacePathBuilder.IsWorkspace;
		private bool ExistsWorkspaceSettingsFile => _fileSystem.ExistsFile(WorkspaceSettingsPath);

		#endregion

		#region Methods: Private

		private WorkspaceSettings CreateDefaultWorkspaceSettings(string[] packages) {
			Version lv = _creatioSdk.LastVersion;
			WorkspaceSettings workspaceSettings = new WorkspaceSettings {
				ApplicationVersion = new Version(lv.Major, lv.Minor, lv.Build),
				Packages = packages
			};
			return workspaceSettings;
		}

		private void CreateWorkspaceSettingsFile(bool isAddingPackageNames = false) {
			string[] packages = new string[] { };
			if (isAddingPackageNames) {
				IEnumerable<PackageInfo> packagesInfo =
					_applicationPackageListProvider.GetPackages("{\"isCustomer\": \"true\"}");
				packages = packagesInfo.Select(s => s.Descriptor.Name).ToArray();
			}
			WorkspaceSettings defaultWorkspaceSettings = CreateDefaultWorkspaceSettings(packages);
			_jsonConverter.SerializeObjectToFile(defaultWorkspaceSettings, WorkspaceSettingsPath);
		}

		private void ActualizeExecutablePermissions() {
			_executablePermissionsActualizer.Actualize(_workspacePathBuilder.SolutionFolderPath);
			_executablePermissionsActualizer.Actualize(_workspacePathBuilder.TasksFolderPath);
		}

		private void ValidateNotExistingWorkspace() {
			if (IsWorkspace) {
				throw new InvalidOperationException("This operation can not execute inside existing workspace!");
			}
		}

		private void ValidateEmptyDirectory() {
			if (!_fileSystem.IsEmptyDirectory()) {
				throw new InvalidOperationException("This operation requires empty folder!");
			}
		}

		private void CreateWorkspaceEnvironmentSettingsFile(string environmentName) {
			var defaultWorkspaceSettings = new WorkspaceEnvironmentSettings {
				Environment = environmentName ?? string.Empty
			};
			_jsonConverter.SerializeObjectToFile(defaultWorkspaceSettings, WorkspaceEnvironmentSettingsPath);
		}

		#endregion

		#region Methods: Public

		public void Create(string environmentName, bool isAddingPackageNames = false) {
			ValidateNotExistingWorkspace();
			ValidateEmptyDirectory();
			_templateProvider.CopyTemplateFolder("workspace", RootPath);
			if (!ExistsWorkspaceSettingsFile) {
				CreateWorkspaceSettingsFile(isAddingPackageNames);
				CreateWorkspaceEnvironmentSettingsFile(environmentName);
			}
			if (_osPlatformChecker.IsWindowsEnvironment) {
				return;
			}
			ActualizeExecutablePermissions();
		}

		#endregion

	}

	#endregion

}