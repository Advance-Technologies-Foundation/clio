using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;

namespace Clio.Workspace
{

	#region Interface: IWorkspaceCreator

	public interface IWorkspaceCreator
	{

		#region Methods: Public

		void Create(bool isAddingPackageNames = false);

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

		#endregion

		#region Constructors: Public

		public WorkspaceCreator(IWorkspacePathBuilder workspacePathBuilder, ICreatioSdk creatioSdk,
				ITemplateProvider templateProvider, IJsonConverter jsonConverter, IFileSystem fileSystem,
				IApplicationPackageListProvider applicationPackageListProvider) {
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			creatioSdk.CheckArgumentNull(nameof(creatioSdk));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			_workspacePathBuilder = workspacePathBuilder;
			_creatioSdk = creatioSdk;
			_templateProvider = templateProvider;
			_jsonConverter = jsonConverter;
			_fileSystem = fileSystem;
			_applicationPackageListProvider = applicationPackageListProvider;
		}

		#endregion

		#region Properties: Private

		private string RootPath => _workspacePathBuilder.RootPath;
		private string WorkspaceSettingsPath => _workspacePathBuilder.WorkspaceSettingsPath;
		private string ClioDirectoryPath => _workspacePathBuilder.ClioDirectoryPath;

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
			if (_fileSystem.ExistsFile(WorkspaceSettingsPath)) {
				return;
			}
			string[] packages = new string[] { };
			if (isAddingPackageNames) {
				IEnumerable<PackageInfo> packagesInfo =
					_applicationPackageListProvider.GetPackages("{\"isCustomer\": \"true\"}");
				packages = packagesInfo.Select(s => s.Descriptor.Name).ToArray();
			}
			WorkspaceSettings defaultWorkspaceSettings = CreateDefaultWorkspaceSettings(packages);
			_jsonConverter.SerializeObjectToFile(defaultWorkspaceSettings, WorkspaceSettingsPath);
		}

		#endregion

		#region Methods: Public

		public void Create(bool isAddingPackageNames = false) {
			_templateProvider.CopyTemplateFolder("workspace", RootPath);
			CreateWorkspaceSettingsFile(isAddingPackageNames);

		}

		#endregion

	}

	#endregion

}