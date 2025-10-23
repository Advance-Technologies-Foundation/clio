using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Workspaces
{

	#region Interface: IZipBasedApplicationDownloader

	public interface IZipBasedApplicationDownloader
	{

		#region Methods: Public

		/// <summary>
		/// Downloads configuration from Creatio zip file to workspace .application folder.
		/// </summary>
		/// <param name="zipFilePath">Path to Creatio zip file</param>
		void DownloadFromZip(string zipFilePath);

		#endregion

	}

	#endregion

	#region Class: ZipBasedApplicationDownloader

	public class ZipBasedApplicationDownloader : IZipBasedApplicationDownloader
	{

		#region Constants: Private

		private const string NetFrameworkMarkerFolder = "Terrasoft.WebApp";
		private const string TerrasoftConfigurationDll = "Terrasoft.Configuration.dll";
		private const string TerrasoftConfigurationODataDll = "Terrasoft.Configuration.ODataEntities.dll";

		#endregion

		#region Fields: Private

		private readonly ICompressionUtilities _compressionUtilities;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public ZipBasedApplicationDownloader(
			ICompressionUtilities compressionUtilities,
			IWorkspacePathBuilder workspacePathBuilder,
			IWorkingDirectoriesProvider workingDirectoriesProvider,
			IFileSystem fileSystem,
			ILogger logger)
		{
			compressionUtilities.CheckArgumentNull(nameof(compressionUtilities));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));

			_compressionUtilities = compressionUtilities;
			_workspacePathBuilder = workspacePathBuilder;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private bool IsNetFrameworkCreatio(string extractedPath)
		{
			string netFrameworkMarkerPath = Path.Combine(extractedPath, NetFrameworkMarkerFolder);
			return _fileSystem.ExistsDirectory(netFrameworkMarkerPath);
		}

		private void DownloadNetFrameworkConfiguration(string extractedPath)
		{
			_logger.WriteInfo("Detected NetFramework Creatio");
			string terrasoftWebAppPath = Path.Combine(extractedPath, NetFrameworkMarkerFolder);

			// 1. Copy Terrasoft.WebApp/bin/* to .application/net-framework/core-bin
			CopyCoreBinFiles(terrasoftWebAppPath);

			// 2. Copy Terrasoft.WebApp/Terrasoft.Configuration/Lib/* to .application/net-framework/bin
			CopyLibFiles(terrasoftWebAppPath);

			// 3. Copy latest conf/bin/{NUMBER} files to .application/net-framework/bin
			CopyConfigurationBinFiles(terrasoftWebAppPath);

			// 4. Copy packages with Files/bin to .application/net-framework/packages
			CopyPackages(terrasoftWebAppPath);

			_logger.WriteInfo("NetFramework configuration downloaded successfully");
		}

		private void CopyCoreBinFiles(string terrasoftWebAppPath)
		{
			string sourcePath = Path.Combine(terrasoftWebAppPath, "bin");
			string destinationPath = _workspacePathBuilder.CoreBinFolderPath;

			if (!_fileSystem.ExistsDirectory(sourcePath))
			{
				_logger.WriteWarning($"Source directory not found: {sourcePath}");
				return;
			}

			_logger.WriteInfo($"Copying core bin files from {sourcePath} to {destinationPath}");
			_fileSystem.CreateDirectoryIfNotExists(destinationPath);
			CopyAllFiles(sourcePath, destinationPath);
		}

		private void CopyLibFiles(string terrasoftWebAppPath)
		{
			string sourcePath = Path.Combine(terrasoftWebAppPath, "Terrasoft.Configuration", "Lib");
			string destinationPath = _workspacePathBuilder.LibFolderPath;

			if (!_fileSystem.ExistsDirectory(sourcePath))
			{
				_logger.WriteWarning($"Source directory not found: {sourcePath}");
				return;
			}

			_logger.WriteInfo($"Copying lib files from {sourcePath} to {destinationPath}");
			_fileSystem.CreateDirectoryIfNotExists(destinationPath);
			CopyAllFiles(sourcePath, destinationPath);
		}

		private void CopyConfigurationBinFiles(string terrasoftWebAppPath)
		{
			string confBinPath = Path.Combine(terrasoftWebAppPath, "conf", "bin");

			if (!_fileSystem.ExistsDirectory(confBinPath))
			{
				_logger.WriteWarning($"Configuration bin directory not found: {confBinPath}");
				return;
			}

			// Find the latest numbered folder
			var numberedFolders = _fileSystem.GetDirectories(confBinPath)
				.Where(dir => int.TryParse(Path.GetFileName(dir), out _))
				.OrderByDescending(dir => int.Parse(Path.GetFileName(dir)))
				.ToList();

			if (!numberedFolders.Any())
			{
				_logger.WriteWarning($"No numbered folders found in {confBinPath}");
				return;
			}

			string latestFolder = numberedFolders.First();
			string destinationPath = _workspacePathBuilder.ConfigurationBinFolderPath;

			_logger.WriteInfo($"Copying configuration bin files from {latestFolder} to {destinationPath}");
			_fileSystem.CreateDirectoryIfNotExists(destinationPath);

			// Copy specific DLLs
			CopyFileIfExists(latestFolder, destinationPath, TerrasoftConfigurationDll);
			CopyFileIfExists(latestFolder, destinationPath, TerrasoftConfigurationODataDll);
		}

	private void CopyPackages(string terrasoftWebAppPath)
	{
		string packagesSourcePath = Path.Combine(terrasoftWebAppPath, "Terrasoft.Configuration", "Pkg");

		if (!_fileSystem.ExistsDirectory(packagesSourcePath))
		{
			_logger.WriteWarning($"Packages directory not found: {packagesSourcePath}");
			return;
		}

		string packagesDestinationRoot = Path.Join(_workspacePathBuilder.RootPath,".application", "net-framework", "packages");
		_fileSystem.CreateDirectoryIfNotExists(packagesDestinationRoot);			
		var packageFolders = _fileSystem.GetDirectories(packagesSourcePath);

			foreach (string packageFolder in packageFolders)
			{
				string packageName = Path.GetFileName(packageFolder);
				string filesBinPath = Path.Combine(packageFolder, "Files", "bin");

				if (_fileSystem.ExistsDirectory(filesBinPath))
				{
					string destinationPackagePath = Path.Combine(packagesDestinationRoot, packageName);
					_logger.WriteInfo($"Copying package {packageName}");
					_fileSystem.CreateDirectoryIfNotExists(destinationPackagePath);
					CopyDirectory(packageFolder, destinationPackagePath);
				}
			}
		}

	private void DownloadNetCoreConfiguration(string extractedPath)
	{
		_logger.WriteInfo("Detected NetCore Creatio");
		
		// For NetCore (NET8), the structure is:
		// - Root DLL and PDB files -> .application/net-core/core-bin
		// - Terrasoft.Configuration/Lib/ -> .application/net-core/bin
		// - conf/bin/{NUMBER}/ -> .application/net-core/bin
		// - Terrasoft.Configuration/Pkg/ -> .application/net-core/packages (filtered by Files/bin)

		string coreBinDestination = _workspacePathBuilder.CoreBinFolderPath.Replace("net-framework", "net-core");
		string libDestination = _workspacePathBuilder.LibFolderPath.Replace("net-framework", "net-core");
		string configBinDestination = _workspacePathBuilder.ConfigurationBinFolderPath.Replace("net-framework", "net-core");

		// Copy root DLL and PDB files
		_logger.WriteInfo($"Copying NetCore root assemblies (DLL and PDB) to {coreBinDestination}");
		_fileSystem.CreateDirectoryIfNotExists(coreBinDestination);
		CopyRootAssemblies(extractedPath, coreBinDestination);

		// Copy Terrasoft.Configuration/Lib if exists
		string libPath = Path.Combine(extractedPath, "Terrasoft.Configuration", "Lib");
		if (_fileSystem.ExistsDirectory(libPath))
		{
			_logger.WriteInfo($"Copying NetCore lib files to {libDestination}");
			_fileSystem.CreateDirectoryIfNotExists(libDestination);
			CopyAllFiles(libPath, libDestination);
		}

		// Copy conf/bin/{NUMBER} - select latest numbered folder
		CopyNetCoreConfigurationBinFiles(extractedPath, configBinDestination);

		// Copy packages with Files/bin filter
		CopyNetCorePackages(extractedPath);

		_logger.WriteInfo("NetCore configuration downloaded successfully");
	}

	private void CopyRootAssemblies(string extractedPath, string destination)
	{
		var files = _fileSystem.GetFiles(extractedPath);
		int copiedCount = 0;

		foreach (string file in files)
		{
			string fileName = Path.GetFileName(file);
			string extension = Path.GetExtension(fileName).ToLowerInvariant();

			// Copy only DLL and PDB files
			if (extension == ".dll" || extension == ".pdb")
			{
				string destFile = Path.Combine(destination, fileName);
				_fileSystem.CopyFile(file, destFile, true);
				copiedCount++;
			}
		}

		_logger.WriteInfo($"Copied {copiedCount} root assemblies (DLL and PDB files)");
	}

	private void CopyNetCoreConfigurationBinFiles(string extractedPath, string destination)
	{
		string confBinPath = Path.Combine(extractedPath, "conf", "bin");

		if (!_fileSystem.ExistsDirectory(confBinPath))
		{
			_logger.WriteWarning($"Configuration bin directory not found: {confBinPath}");
			return;
		}

		// Find the latest numbered folder
		var numberedFolders = _fileSystem.GetDirectories(confBinPath)
			.Where(dir => int.TryParse(Path.GetFileName(dir), out _))
			.OrderByDescending(dir => int.Parse(Path.GetFileName(dir)))
			.ToList();

		if (!numberedFolders.Any())
		{
			_logger.WriteWarning($"No numbered folders found in {confBinPath}");
			return;
		}

		string latestFolder = numberedFolders.First();
		_logger.WriteInfo($"Copying NetCore configuration bin files from {latestFolder} to {destination}");
		_fileSystem.CreateDirectoryIfNotExists(destination);

		// Copy specific DLLs
		CopyFileIfExists(latestFolder, destination, TerrasoftConfigurationDll);
		CopyFileIfExists(latestFolder, destination, TerrasoftConfigurationODataDll);
	}

	private void CopyNetCorePackages(string extractedPath)
	{
		string packagesSourcePath = Path.Combine(extractedPath, "Terrasoft.Configuration", "Pkg");

		if (!_fileSystem.ExistsDirectory(packagesSourcePath))
		{
			_logger.WriteWarning($"Packages directory not found: {packagesSourcePath}");
			return;
		}

		
		string packagesDestinationRoot = Path.Join(_workspacePathBuilder.RootPath,".application", "net-core", "packages");
		_fileSystem.CreateDirectoryIfNotExists(packagesDestinationRoot);

		var packageFolders = _fileSystem.GetDirectories(packagesSourcePath);

		foreach (string packageFolder in packageFolders)
		{
			string packageName = Path.GetFileName(packageFolder);
			string filesBinPath = Path.Combine(packageFolder, "Files", "bin");

			if (_fileSystem.ExistsDirectory(filesBinPath))
			{
				string destinationPackagePath = Path.Combine(packagesDestinationRoot, packageName);
				_logger.WriteInfo($"Copying NetCore package {packageName}");
				_fileSystem.CreateDirectoryIfNotExists(destinationPackagePath);
				CopyDirectory(packageFolder, destinationPackagePath);
			}
		}
	}		private void CopyAllFiles(string sourcePath, string destinationPath)
		{
			var files = _fileSystem.GetFiles(sourcePath);
			foreach (string file in files)
			{
				string fileName = Path.GetFileName(file);
				string destFile = Path.Combine(destinationPath, fileName);
				_fileSystem.CopyFile(file, destFile, true);
			}
		}

		private void CopyFileIfExists(string sourcePath, string destinationPath, string fileName)
		{
			string sourceFile = Path.Combine(sourcePath, fileName);
			if (_fileSystem.ExistsFile(sourceFile))
			{
				string destFile = Path.Combine(destinationPath, fileName);
				_fileSystem.CopyFile(sourceFile, destFile, true);
				_logger.WriteInfo($"Copied {fileName}");
			}
			else
			{
				_logger.WriteWarning($"File not found: {sourceFile}");
			}
		}

		private void CopyDirectory(string sourcePath, string destinationPath)
		{
			_fileSystem.CreateDirectoryIfNotExists(destinationPath);

			// Copy all files
			var files = _fileSystem.GetFiles(sourcePath);
			foreach (string file in files)
			{
				string fileName = Path.GetFileName(file);
				string destFile = Path.Combine(destinationPath, fileName);
				_fileSystem.CopyFile(file, destFile, true);
			}

			// Recursively copy subdirectories
			var directories = _fileSystem.GetDirectories(sourcePath);
			foreach (string directory in directories)
			{
				string dirName = Path.GetFileName(directory);
				string destDir = Path.Combine(destinationPath, dirName);
				CopyDirectory(directory, destDir);
			}
		}

		#endregion

		#region Methods: Public

		public void DownloadFromZip(string zipFilePath)
		{
			zipFilePath.CheckArgumentNullOrWhiteSpace(nameof(zipFilePath));

			if (!_fileSystem.ExistsFile(zipFilePath))
			{
				throw new FileNotFoundException($"Zip file not found: {zipFilePath}");
			}

			if (!_workspacePathBuilder.IsWorkspace)
			{
				throw new InvalidOperationException("Current directory is not a workspace. Please run this command from a workspace directory.");
			}

			_logger.WriteInfo($"Extracting Creatio from {zipFilePath}");

			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory =>
			{
				try
				{
					// Extract zip to temp directory
					_compressionUtilities.Unzip(zipFilePath, tempDirectory);
					_logger.WriteInfo($"Extracted to temporary directory: {tempDirectory}");

					// Detect Creatio type and download configuration
					if (IsNetFrameworkCreatio(tempDirectory))
					{
						DownloadNetFrameworkConfiguration(tempDirectory);
					}
					else
					{
						DownloadNetCoreConfiguration(tempDirectory);
					}
				}
				catch (Exception ex)
				{
					_logger.WriteError($"Error downloading configuration from zip: {ex.Message}");
					throw;
				}
			});

			_logger.WriteInfo("Configuration download from zip completed successfully");
		}

		#endregion

	}

	#endregion

}
