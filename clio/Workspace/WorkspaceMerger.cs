namespace Clio.Workspaces
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Clio.Common;
	using Clio.Package;
	using Clio.Utilities;

	#region Interface: IWorkspaceMerger

	public interface IWorkspaceMerger
	{
		/// <summary>
		/// Merges packages from multiple workspaces into a single ZIP file and installs them into Creatio.
		/// </summary>
		/// <param name="workspacePaths">Array of paths to workspace folders.</param>
		/// <param name="zipFileName">Optional name for the resulting ZIP file. Default is "MergedCreatioPackages".</param>
		/// <param name="skipBackup">Whether to skip creating backup when installing packages.</param>
		void MergeAndInstall(string[] workspacePaths, string zipFileName = "MergedCreatioPackages", bool skipBackup = false);

		/// <summary>
		/// Merges packages from multiple workspaces into a single ZIP file.
		/// </summary>
		/// <param name="workspacePaths">Array of paths to workspace folders.</param>
		/// <param name="outputPath">Path where the output ZIP file should be saved.</param>
		/// <param name="zipFileName">Optional name for the resulting ZIP file. Default is "MergedCreatioPackages".</param>
		/// <returns>Full path to the created ZIP file.</returns>
		string MergeToZip(string[] workspacePaths, string outputPath, string zipFileName = "MergedCreatioPackages");
	}

	#endregion

	#region Class: WorkspaceMerger

	public class WorkspaceMerger : IWorkspaceMerger
	{
		#region Constants: Private

		private const string MergedPackagesZipName = "MergedCreatioPackages";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IPackageInstaller _packageInstaller;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public WorkspaceMerger(
			EnvironmentSettings environmentSettings,
			IWorkspacePathBuilder workspacePathBuilder,
			IPackageArchiver packageArchiver,
			IPackageInstaller packageInstaller,
			IWorkingDirectoriesProvider workingDirectoriesProvider,
			IFileSystem fileSystem,
			ILogger logger)
		{
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			packageInstaller.CheckArgumentNull(nameof(packageInstaller));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));

			_environmentSettings = environmentSettings;
			_workspacePathBuilder = workspacePathBuilder;
			_packageArchiver = packageArchiver;
			_packageInstaller = packageInstaller;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private string CreateRootPackedPackageDirectory(string tempDirectory, string zipName)
		{
			string rootPackedPackagePath = Path.Combine(tempDirectory, zipName);
			_fileSystem.CreateDirectory(rootPackedPackagePath);
			return rootPackedPackagePath;
		}

		private void PackPackage(string packagePath, string packageName, string rootPackedPackagePath)
		{
			string packedPackagePath = Path.Combine(rootPackedPackagePath, $"{packageName}.gz");
			_packageArchiver.Pack(packagePath, packedPackagePath, true, true);
		}

		private void GatherPackagesFromWorkspace(string workspacePath, string rootPackedPackagePath, HashSet<string> processedPackages)
		{
			_workspacePathBuilder.RootPath = workspacePath;
			string packagesPath = _workspacePathBuilder.PackagesFolderPath;
			
			if (!_fileSystem.ExistsDirectory(packagesPath))
			{
				_logger.WriteWarning($"Packages folder not found in workspace: {workspacePath}");
				return;
			}

			string[] packageDirs = _fileSystem.GetDirectories(packagesPath);
			foreach (string packageDir in packageDirs)
			{
				string packageName = new DirectoryInfo(packageDir).Name;
				
				// Skip if this package was already processed from another workspace
				if (processedPackages.Contains(packageName))
				{
					_logger.WriteWarning($"Package {packageName} already processed from another workspace. Skipping...");
					continue;
				}
				
				_logger.WriteInfo($"Adding package: {packageName} from workspace: {workspacePath}");
				PackPackage(packageDir, packageName, rootPackedPackagePath);
				processedPackages.Add(packageName);
			}
		}

		private string ZipPackages(string tempDirectory, string rootPackedPackagePath, string zipFileName)
		{
			string applicationZip = Path.Combine(tempDirectory, $"{zipFileName}.zip");
			_packageArchiver.ZipPackages(rootPackedPackagePath, applicationZip, true);
			return applicationZip;
		}

		private void InstallApplication(string applicationZip, bool skipBackup = false)
		{
			_logger.WriteInfo($"Installing application from {applicationZip}");
			PackageInstallOptions options = skipBackup ? new PackageInstallOptions { SkipBackup = true } : null;
			_packageInstaller.Install(applicationZip, _environmentSettings, options);
		}

		#endregion

		#region Methods: Public

		/// <summary>
		/// Merges packages from multiple workspaces into a single ZIP file and installs them into Creatio.
		/// </summary>
		/// <param name="workspacePaths">Array of paths to workspace folders.</param>
		/// <param name="zipFileName">Optional name for the resulting ZIP file. Default is "MergedCreatioPackages".</param>
		/// <param name="skipBackup">Whether to skip creating backup when installing packages.</param>
		public void MergeAndInstall(string[] workspacePaths, string zipFileName = MergedPackagesZipName, bool skipBackup = false)
		{
			if (workspacePaths == null || workspacePaths.Length == 0)
			{
				throw new ArgumentException("No workspace paths provided.", nameof(workspacePaths));
			}

			foreach (string workspace in workspacePaths)
			{
				if (!_fileSystem.ExistsDirectory(workspace))
				{
					throw new DirectoryNotFoundException($"Workspace directory not found: {workspace}");
				}
			}

			_logger.WriteInfo("Starting merge and install process...");
			
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				var rootPackedPackagePath = CreateRootPackedPackageDirectory(tempDirectory, zipFileName);
				var processedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				
				foreach (string workspacePath in workspacePaths)
				{
					_logger.WriteInfo($"Processing workspace: {workspacePath}");
					GatherPackagesFromWorkspace(workspacePath, rootPackedPackagePath, processedPackages);
				}
				
				if (processedPackages.Count == 0)
				{
					throw new InvalidOperationException("No packages found in the provided workspaces.");
				}
				
				_logger.WriteInfo($"Total unique packages gathered: {processedPackages.Count}");
				var applicationZip = ZipPackages(tempDirectory, rootPackedPackagePath, zipFileName);
				InstallApplication(applicationZip, skipBackup);
				_logger.WriteInfo("Installation completed successfully.");
			});
		}

		/// <summary>
		/// Merges packages from multiple workspaces into a single ZIP file.
		/// </summary>
		/// <param name="workspacePaths">Array of paths to workspace folders.</param>
		/// <param name="outputPath">Path where the output ZIP file should be saved.</param>
		/// <param name="zipFileName">Optional name for the resulting ZIP file. Default is "MergedCreatioPackages".</param>
		/// <returns>Full path to the created ZIP file.</returns>
		public string MergeToZip(string[] workspacePaths, string outputPath, string zipFileName = MergedPackagesZipName)
		{
			if (workspacePaths == null || workspacePaths.Length == 0)
			{
				throw new ArgumentException("No workspace paths provided.", nameof(workspacePaths));
			}

			foreach (string workspace in workspacePaths)
			{
				if (!_fileSystem.ExistsDirectory(workspace))
				{
					throw new DirectoryNotFoundException($"Workspace directory not found: {workspace}");
				}
			}

			if (string.IsNullOrEmpty(outputPath))
			{
				throw new ArgumentException("Output path cannot be null or empty.", nameof(outputPath));
			}

			// Ensure output directory exists
			_fileSystem.CreateDirectoryIfNotExists(outputPath);
			string resultZipPath = Path.Combine(outputPath, $"{zipFileName}.zip");
			
			_logger.WriteInfo("Starting merge process...");
			
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				var rootPackedPackagePath = CreateRootPackedPackageDirectory(tempDirectory, zipFileName);
				var processedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				
				foreach (string workspacePath in workspacePaths)
				{
					_logger.WriteInfo($"Processing workspace: {workspacePath}");
					GatherPackagesFromWorkspace(workspacePath, rootPackedPackagePath, processedPackages);
				}
				
				if (processedPackages.Count == 0)
				{
					throw new InvalidOperationException("No packages found in the provided workspaces.");
				}
				
				_logger.WriteInfo($"Total unique packages gathered: {processedPackages.Count}");
				var applicationZip = ZipPackages(tempDirectory, rootPackedPackagePath, zipFileName);
				_fileSystem.CopyFile(applicationZip, resultZipPath, true);
				_logger.WriteInfo($"Merged packages saved to: {resultZipPath}");
			});
			
			return resultZipPath;
		}

		#endregion
	}

	#endregion
}