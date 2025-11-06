using System.Threading;

namespace Clio.Workspaces
{
	using System;
	using System.IO;
	using Clio.Command;
	using Clio.Common;
	using Clio.ComposableApplication;
	using Clio.Package;
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

		private void UpdatePackagesVersion(string packagesFolderPath, string appVersion) {
			if (string.IsNullOrWhiteSpace(packagesFolderPath) || !Directory.Exists(packagesFolderPath)) {
				Console.WriteLine($"Warning: Packages folder path is invalid or does not exist: {packagesFolderPath}");
				return;
			}
			foreach (string packageDirectory in Directory.GetDirectories(packagesFolderPath)) {
				string descriptorPath = Path.Combine(packageDirectory, CreatioPackage.DescriptorName);
				if (!File.Exists(descriptorPath)) {
					Console.WriteLine($"Warning: Package descriptor not found: {descriptorPath}");
					continue;
				}
				try {
					var descriptorDto = _jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath);
					if (descriptorDto?.Descriptor == null) {
						Console.WriteLine($"Warning: Invalid or empty package descriptor: {descriptorPath}");
						continue;
					}
					descriptorDto.Descriptor.PackageVersion = appVersion;
					descriptorDto.Descriptor.ModifiedOnUtc = PackageDescriptor.ConvertToModifiedOnUtc(DateTime.Now);
					_jsonConverter.SerializeObjectToFile(descriptorDto, descriptorPath);
				} catch (Exception ex) {
					Console.WriteLine($"Warning: Failed to update package version for {descriptorPath}: {ex.Message}");
				}
			}
		}

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

		public void Install(string creatioPackagesZipName = null, bool useApplicationInstaller = false) =>
			_workspaceInstaller.Install(WorkspaceSettings.Packages, creatioPackagesZipName, useApplicationInstaller);
			
		public void InstallUsingApplicationInstaller(string creatioPackagesZipName = null) =>
			Install(creatioPackagesZipName, true);

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



		public string PublishToFile(string workspacePath, string filePath, string appVersion) {
			if (string.IsNullOrWhiteSpace(workspacePath)) {
				throw new ArgumentNullException(nameof(workspacePath));
			}
			if (string.IsNullOrWhiteSpace(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}
			if (string.IsNullOrWhiteSpace(appVersion)) {
				throw new ArgumentNullException(nameof(appVersion));
			}
			_workspacePathBuilder.RootPath = workspacePath;
			var packagesFolderPath = _workspacePathBuilder.PackagesFolderPath;
			_composableApplicationManager.TrySetVersion(workspacePath, appVersion);
			UpdatePackagesVersion(packagesFolderPath, appVersion);
			string destinationFolderPath = Path.GetDirectoryName(filePath);
			if (string.IsNullOrWhiteSpace(destinationFolderPath)) {
				destinationFolderPath = Directory.GetCurrentDirectory();
			}
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
			if (string.IsNullOrWhiteSpace(fileNameWithoutExtension)) {
				throw new ArgumentException("File path must include a file name.", nameof(filePath));
			}
			string expectedFileName = Path.GetFileName(filePath);
			string sanitizeFileName = GetSanitizeFileNameFromString(fileNameWithoutExtension);
			string resultPath = _workspaceInstaller.PublishToFolder(workspacePath, sanitizeFileName, destinationFolderPath, true);
			string expectedPath = Path.GetFullPath(Path.Combine(destinationFolderPath, expectedFileName));
			
			// Rename the file to match the expected name if necessary
			if (!string.Equals(Path.GetFullPath(resultPath), expectedPath, StringComparison.OrdinalIgnoreCase)) {
				if (File.Exists(expectedPath)) {
					File.Delete(expectedPath);
				}
				File.Move(resultPath, expectedPath);
				resultPath = expectedPath;
			}
			return Path.GetFullPath(resultPath);
		}


		public string PublishToFolder(string workspacePath, string appStorePath, string appName, string appVersion, string branch = null) {
			var hasBranch = !string.IsNullOrEmpty(branch);
			var branchFolderName = hasBranch ? GetSanitizeFileNameFromString(branch) : null;
			_workspacePathBuilder.RootPath = workspacePath;
			var packagesFolderPath = _workspacePathBuilder.PackagesFolderPath;
			_composableApplicationManager.TrySetVersion(workspacePath, appVersion);
			UpdatePackagesVersion(packagesFolderPath, appVersion);
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

		public string GetWorkspaceApplicationCode() {
			return _composableApplicationManager.GetCode(_workspacePathBuilder.PackagesFolderPath);
		}
	}

	#endregion

}
