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
		/// Downloads configuration from Creatio zip file or directory to workspace .application folder.
		/// Automatically detects whether the path is a ZIP file or extracted directory.
		/// </summary>
		/// <param name="path">Path to Creatio zip file or extracted directory</param>
		void DownloadFromPath(string path);

		/// <summary>
		/// Downloads configuration from Creatio zip file to workspace .application folder.
		/// </summary>
		/// <param name="zipFilePath">Path to Creatio zip file</param>
		void DownloadFromZip(string zipFilePath);

		/// <summary>
		/// Downloads configuration from already-extracted Creatio directory to workspace .application folder.
		/// </summary>
		/// <param name="directoryPath">Path to extracted Creatio directory</param>
		void DownloadFromDirectory(string directoryPath);

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

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] CopyCoreBinFiles: Source={sourcePath}, Destination={destinationPath}");
		}

		if (!_fileSystem.ExistsDirectory(sourcePath))
		{
			_logger.WriteWarning($"Source directory not found: {sourcePath}");
			return;
		}

		_logger.WriteInfo($"Copying core bin files from {sourcePath} to {destinationPath}");
		_fileSystem.CreateDirectoryIfNotExists(destinationPath);
		CopyAllFiles(sourcePath, destinationPath);
	}		private void CopyLibFiles(string terrasoftWebAppPath)
		{
			string sourcePath = Path.Combine(terrasoftWebAppPath, "Terrasoft.Configuration", "Lib");
			string destinationPath = _workspacePathBuilder.LibFolderPath;

			if (Program.IsDebugMode)
			{
				_logger.WriteInfo($"[DEBUG] CopyLibFiles: Source={sourcePath}, Destination={destinationPath}");
			}

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

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] CopyConfigurationBinFiles: ConfBinPath={confBinPath}");
		}

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

		if (Program.IsDebugMode && numberedFolders.Any())
		{
			_logger.WriteInfo($"[DEBUG]   Found {numberedFolders.Count} numbered folders: {string.Join(", ", numberedFolders.Select(Path.GetFileName))}");
		}

		if (!numberedFolders.Any())
		{
			_logger.WriteWarning($"No numbered folders found in {confBinPath}");
			return;
		}

		string latestFolder = numberedFolders.First();
		string destinationPath = _workspacePathBuilder.ConfigurationBinFolderPath;

		_logger.WriteInfo($"Copying configuration bin files from {latestFolder} to {destinationPath}");
		
		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG]   Selected latest folder: {Path.GetFileName(latestFolder)}, Destination={destinationPath}");
		}

		_fileSystem.CreateDirectoryIfNotExists(destinationPath);

		// Copy specific DLLs
		CopyFileIfExists(latestFolder, destinationPath, TerrasoftConfigurationDll);
		CopyFileIfExists(latestFolder, destinationPath, TerrasoftConfigurationODataDll);
	}	private void CopyPackages(string terrasoftWebAppPath)
	{
		string packagesSourcePath = Path.Combine(terrasoftWebAppPath, "Terrasoft.Configuration", "Pkg");

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] CopyPackages: Source={packagesSourcePath}");
		}

		if (!_fileSystem.ExistsDirectory(packagesSourcePath))
		{
			_logger.WriteWarning($"Packages directory not found: {packagesSourcePath}");
			return;
		}

		string packagesDestinationRoot = Path.Join(_workspacePathBuilder.RootPath,".application", "net-framework", "packages");
		_fileSystem.CreateDirectoryIfNotExists(packagesDestinationRoot);
		
		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG]   Destination: {packagesDestinationRoot}");
		}
		
		var packageFolders = _fileSystem.GetDirectories(packagesSourcePath);
		int copiedPackages = 0;
		int skippedPackages = 0;

			foreach (string packageFolder in packageFolders)
			{
				string packageName = Path.GetFileName(packageFolder);
				string filesBinPath = Path.Combine(packageFolder, "Files", "bin");

				if (_fileSystem.ExistsDirectory(filesBinPath))
				{
					string destinationPackagePath = Path.Combine(packagesDestinationRoot, packageName);
					_logger.WriteInfo($"Copying package {packageName}");
					
					if (Program.IsDebugMode)
					{
						_logger.WriteInfo($"[DEBUG]   {packageName}: {packageFolder} -> {destinationPackagePath}");
					}

					_fileSystem.CreateDirectoryIfNotExists(destinationPackagePath);
					CopyDirectory(packageFolder, destinationPackagePath);
					copiedPackages++;
				}
				else
				{
					skippedPackages++;
					if (Program.IsDebugMode)
					{
						_logger.WriteInfo($"[DEBUG]   Skipped {packageName} (no Files/bin folder)");
					}
				}
			}

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] NetFramework packages summary: Copied={copiedPackages}, Skipped={skippedPackages}");
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
		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] CopyRootAssemblies: Source={extractedPath}, Destination={destination}");
		}

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
				
				if (Program.IsDebugMode)
				{
					_logger.WriteInfo($"[DEBUG]   Copied: {fileName} -> {destFile}");
				}
			}
		}

		_logger.WriteInfo($"Copied {copiedCount} root assemblies (DLL and PDB files)");
	}

	private void CopyNetCoreConfigurationBinFiles(string extractedPath, string destination)
	{
		string confBinPath = Path.Combine(extractedPath, "conf", "bin");

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] CopyNetCoreConfigurationBinFiles: ConfBinPath={confBinPath}, Destination={destination}");
		}

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

		if (Program.IsDebugMode && numberedFolders.Any())
		{
			_logger.WriteInfo($"[DEBUG]   Found {numberedFolders.Count} numbered folders: {string.Join(", ", numberedFolders.Select(Path.GetFileName))}");
		}

		if (!numberedFolders.Any())
		{
			_logger.WriteWarning($"No numbered folders found in {confBinPath}");
			return;
		}

		string latestFolder = numberedFolders.First();
		_logger.WriteInfo($"Copying NetCore configuration bin files from {latestFolder} to {destination}");
		
		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG]   Selected latest folder: {Path.GetFileName(latestFolder)}");
		}

		_fileSystem.CreateDirectoryIfNotExists(destination);

		// Copy specific DLLs
		CopyFileIfExists(latestFolder, destination, TerrasoftConfigurationDll);
		CopyFileIfExists(latestFolder, destination, TerrasoftConfigurationODataDll);
	}

	private void CopyNetCorePackages(string extractedPath)
	{
		string packagesSourcePath = Path.Combine(extractedPath, "Terrasoft.Configuration", "Pkg");

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] CopyNetCorePackages: Source={packagesSourcePath}");
		}

		if (!_fileSystem.ExistsDirectory(packagesSourcePath))
		{
			_logger.WriteWarning($"Packages directory not found: {packagesSourcePath}");
			return;
		}

		
		string packagesDestinationRoot = Path.Join(_workspacePathBuilder.RootPath,".application", "net-core", "packages");
		_fileSystem.CreateDirectoryIfNotExists(packagesDestinationRoot);

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG]   Destination: {packagesDestinationRoot}");
		}

		var packageFolders = _fileSystem.GetDirectories(packagesSourcePath);
		int copiedPackages = 0;
		int skippedPackages = 0;

		foreach (string packageFolder in packageFolders)
		{
			string packageName = Path.GetFileName(packageFolder);
			string filesBinPath = Path.Combine(packageFolder, "Files", "bin");

			if (_fileSystem.ExistsDirectory(filesBinPath))
			{
				string destinationPackagePath = Path.Combine(packagesDestinationRoot, packageName);
				_logger.WriteInfo($"Copying NetCore package {packageName}");
				
				if (Program.IsDebugMode)
				{
					_logger.WriteInfo($"[DEBUG]   {packageName}: {packageFolder} -> {destinationPackagePath}");
				}

				_fileSystem.CreateDirectoryIfNotExists(destinationPackagePath);
				CopyDirectory(packageFolder, destinationPackagePath);
				copiedPackages++;
			}
			else
			{
				skippedPackages++;
				if (Program.IsDebugMode)
				{
					_logger.WriteInfo($"[DEBUG]   Skipped {packageName} (no Files/bin folder)");
				}
			}
		}

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] NetCore packages summary: Copied={copiedPackages}, Skipped={skippedPackages}");
		}
	}		private void CopyAllFiles(string sourcePath, string destinationPath)
		{
			var files = _fileSystem.GetFiles(sourcePath);
			
			if (Program.IsDebugMode)
			{
				_logger.WriteInfo($"[DEBUG]   CopyAllFiles: {files.Length} files from {sourcePath}");
			}

			foreach (string file in files)
			{
				string fileName = Path.GetFileName(file);
				string destFile = Path.Combine(destinationPath, fileName);
				_fileSystem.CopyFile(file, destFile, true);
				
				if (Program.IsDebugMode)
				{
					_logger.WriteInfo($"[DEBUG]     {fileName}");
				}
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
				
				if (Program.IsDebugMode)
				{
					_logger.WriteInfo($"[DEBUG]   {sourceFile} -> {destFile}");
				}
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

		public void DownloadFromPath(string path)
		{
			path.CheckArgumentNullOrWhiteSpace(nameof(path));

			bool isZipFile = Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);

			if (isZipFile)
			{
				DownloadFromZip(path);
			}
			else
			{
				DownloadFromDirectory(path);
			}
		}

		#endregion

		#region Methods: Public

	public void DownloadFromZip(string zipFilePath)
	{
		zipFilePath.CheckArgumentNullOrWhiteSpace(nameof(zipFilePath));

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] DownloadFromZip started: ZipFile={zipFilePath}");
			_logger.WriteInfo($"[DEBUG]   Workspace root: {_workspacePathBuilder.RootPath}");
		}

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
				if (Program.IsDebugMode)
				{
					_logger.WriteInfo($"[DEBUG]   Temporary directory created: {tempDirectory}");
				}

				// Extract zip to temp directory
				_compressionUtilities.Unzip(zipFilePath, tempDirectory);
				_logger.WriteInfo($"Extracted to temporary directory: {tempDirectory}");					// Detect Creatio type and download configuration
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

	public void DownloadFromDirectory(string directoryPath)
	{
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));

		if (Program.IsDebugMode)
		{
			_logger.WriteInfo($"[DEBUG] DownloadFromDirectory started: Directory={directoryPath}");
			_logger.WriteInfo($"[DEBUG]   Workspace root: {_workspacePathBuilder.RootPath}");
		}

		if (!_fileSystem.ExistsDirectory(directoryPath))
		{
			throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
		}

		if (!_workspacePathBuilder.IsWorkspace)
		{
			throw new InvalidOperationException("Current directory is not a workspace. Please run this command from a workspace directory.");
		}

		_logger.WriteInfo($"Processing Creatio configuration from directory: {directoryPath}");

		try
		{
			// Detect Creatio type and download configuration
			if (IsNetFrameworkCreatio(directoryPath))
			{
				DownloadNetFrameworkConfiguration(directoryPath);
			}
			else
			{
				DownloadNetCoreConfiguration(directoryPath);
			}

			_logger.WriteInfo("Configuration download from directory completed successfully");
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Error downloading configuration from directory: {ex.Message}");
			if (Program.IsDebugMode)
			{
				_logger.WriteError($"[DEBUG] Stack trace: {ex.StackTrace}");
			}
			throw;
		}
	}

		#endregion

	}

	#endregion

}

