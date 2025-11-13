#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;
using Clio.Workspaces;

#endregion

namespace Clio.Workspace;

#region Interface: IZipBasedApplicationDownloader

public interface IZipBasedApplicationDownloader{
	#region Methods: Public

	/// <summary>
	///     Downloads configuration from a Creatio zip file or directory to the <c>workspace/.application</c> folder.
	///     Automatically detects whether the path is a ZIP file or extracted directory.
	/// </summary>
	/// <param name="path">Path to a Creatio zip file or extracted directory</param>
	void DownloadFromPath(string path);

	/// <summary>
	///     Downloads configuration from a Creatio zip file to the <c>workspace/.application</c> folder.
	/// </summary>
	/// <param name="zipFilePath">Path to a Creatio zip file</param>
	void DownloadFromZip(string zipFilePath);

	/// <summary>
	///     Downloads configuration from already-extracted Creatio directory to workspace .application folder.
	/// </summary>
	/// <param name="directoryPath">Path to extracted Creatio directory</param>
	void DownloadFromDirectory(string directoryPath);

	#endregion
}

#endregion

#region Class: ZipBasedApplicationDownloader

public class ZipBasedApplicationDownloader : IZipBasedApplicationDownloader{
	#region Constants: Private

	private const string NetFrameworkMarkerFolder = "Terrasoft.WebApp";
	private const string TerrasoftConfigurationDll = "Terrasoft.Configuration.dll";
	private const string TerrasoftConfigurationODataDll = "Terrasoft.Configuration.ODataEntities.dll";

	#endregion

	#region Fields: Private

	private readonly ICompressionUtilities _compressionUtilities;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;

	#endregion

	#region Constructors: Public

	public ZipBasedApplicationDownloader(
		ICompressionUtilities compressionUtilities,
		IWorkspacePathBuilder workspacePathBuilder,
		IWorkingDirectoriesProvider workingDirectoriesProvider,
		IFileSystem fileSystem,
		ILogger logger) {
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

	private void CopyAllFiles(string sourcePath, string destinationPath) {
		string[] files = _fileSystem.GetFiles(sourcePath);

		if (Program.IsDebugMode) {
			_logger.WriteInfo($"[DEBUG]   CopyAllFiles: {files.Length} files from {sourcePath}");
		}

		foreach (string file in files) {
			string fileName = Path.GetFileName(file);
			string destFile = Path.Combine(destinationPath, fileName);
			_fileSystem.CopyFile(file, destFile, true);

			if (Program.IsDebugMode) {
				_logger.WriteInfo($"[DEBUG]     {fileName}");
			}
		}
	}

	private void CopyDirectory(string sourcePath, string destinationPath) {
		_fileSystem.CreateDirectoryIfNotExists(destinationPath);

		// Copy all files
		string[] files = _fileSystem.GetFiles(sourcePath);
		foreach (string file in files) {
			string fileName = Path.GetFileName(file);
			string destFile = Path.Combine(destinationPath, fileName);
			_fileSystem.CopyFile(file, destFile, true);
		}

		// Recursively copy subdirectories
		string[] directories = _fileSystem.GetDirectories(sourcePath);
		foreach (string directory in directories) {
			string dirName = Path.GetFileName(directory);
			string destDir = Path.Combine(destinationPath, dirName);
			CopyDirectory(directory, destDir);
		}
	}

	private void CopyFileIfExists(string sourcePath, string destinationPath, string fileName) {
		string sourceFile = Path.Combine(sourcePath, fileName);
		if (_fileSystem.ExistsFile(sourceFile)) {
			string destFile = Path.Combine(destinationPath, fileName);
			_fileSystem.CopyFile(sourceFile, destFile, true);
			_logger.WriteInfo($"Copied {fileName}");

			if (Program.IsDebugMode) {
				_logger.WriteInfo($"[DEBUG]   {sourceFile} -> {destFile}");
			}
		}
		else {
			_logger.WriteWarning($"File not found: {sourceFile}");
		}
	}

	private void CopyLibFiles(string extractedPath, bool isNetCore = true) {
		string sourcePath = isNetCore 
		? Path.Combine(extractedPath, "Terrasoft.Configuration", "Lib")
		: Path.Combine(extractedPath,"Terrasoft.WebApp", "Terrasoft.Configuration", "Lib");
		
		string destinationPath = isNetCore 
			? Path.Combine(_workspacePathBuilder.RootPath, ".application", "net-core", "lib")
			: Path.Combine(_workspacePathBuilder.RootPath, ".application", "net-framework", "lib");

		if (Program.IsDebugMode) {
			_logger.WriteInfo($"[DEBUG] CopyLibFiles: Source={sourcePath}, Destination={destinationPath}");
		}

		if (!_fileSystem.ExistsDirectory(sourcePath)) {
			_logger.WriteWarning($"Source directory not found: {sourcePath}");
			return;
		}

		_logger.WriteInfo($"Copying lib files from {sourcePath} to {destinationPath}");
		_fileSystem.CreateDirectoryIfNotExists(destinationPath);
		CopyAllFiles(sourcePath, destinationPath);
	}

	private void CopyConfigurationBinFiles(string extractedPath, string destination, bool isNetCore = true) {
		string confBinPath = isNetCore 
			? Path.Combine(extractedPath, "conf", "bin")
			: Path.Combine(extractedPath, "Terrasoft.WebApp", "conf", "bin");

		if (Program.IsDebugMode) {
			string msg = isNetCore 
				? "[DEBUG] CopyNetCoreConfigurationBinFiles (NetCore): " 
				: "[DEBUG] CopyNetCoreConfigurationBinFiles (NetFramework): ";
			_logger.WriteInfo(
				$"{msg} ConfBinPath={confBinPath}, Destination={destination}");
		}

		if (!_fileSystem.ExistsDirectory(confBinPath)) {
			_logger.WriteWarning($"Configuration bin directory not found: {confBinPath}");
			return;
		}

		// Find the latest numbered folder
		List<string> numberedFolders = _fileSystem.GetDirectories(confBinPath)
												  .Where(dir => int.TryParse(Path.GetFileName(dir), out int _))
												  .OrderByDescending(dir => int.Parse(Path.GetFileName(dir)))
												  .ToList();

		if (Program.IsDebugMode && numberedFolders.Count != 0) {
			_logger.WriteInfo(
				$"[DEBUG]   Found {numberedFolders.Count} numbered folders: {string.Join(", ", numberedFolders.Select(Path.GetFileName))}");
		}

		if (numberedFolders.Count == 0) {
			_logger.WriteWarning($"No numbered folders found in {confBinPath}");
			return;
		}

		string latestFolder = numberedFolders.First();
		string framework = isNetCore ? "NetCore" : "NetFramework";
		_logger.WriteInfo($"Copying {framework} configuration bin files from {latestFolder} to {destination}");

		if (Program.IsDebugMode) {
			_logger.WriteInfo($"[DEBUG]   Selected latest folder: {Path.GetFileName(latestFolder)}");
		}

		_fileSystem.CreateDirectoryIfNotExists(destination);

		// Copy specific DLLs
		CopyFileIfExists(latestFolder, destination, TerrasoftConfigurationDll);
		CopyFileIfExists(latestFolder, destination, TerrasoftConfigurationODataDll);
	}

	private void CopyPackages(string extractedPath, bool isNetCore = true) {
		string packagesSourcePath = isNetCore 
			? Path.Combine(extractedPath, "Terrasoft.Configuration", "Pkg")
			: Path.Combine(extractedPath, "Terrasoft.WebApp","Terrasoft.Configuration", "Pkg");

		if (Program.IsDebugMode) {
			string msg = isNetCore ? "CopyNetCorePackages" : "CopyNetFrameworkPackages";
			_logger.WriteInfo($"[DEBUG] {msg}: Source={packagesSourcePath}");
		}

		if (!_fileSystem.ExistsDirectory(packagesSourcePath)) {
			_logger.WriteWarning($"Packages directory not found: {packagesSourcePath}");
			return;
		}


		string packagesDestinationRoot
			= isNetCore 
				? Path.Join(_workspacePathBuilder.RootPath, ".application", "net-core", "packages")
				: Path.Join(_workspacePathBuilder.RootPath, ".application", "net-framework", "packages");
		
		_fileSystem.CreateDirectoryIfNotExists(packagesDestinationRoot);

		if (Program.IsDebugMode) {
			_logger.WriteInfo($"[DEBUG]   Destination: {packagesDestinationRoot}");
		}

		string[] packageFolders = _fileSystem.GetDirectories(packagesSourcePath);
		int copiedPackages = 0;
		int skippedPackages = 0;

		foreach (string packageFolder in packageFolders) {
			string packageName = Path.GetFileName(packageFolder);
			string filesBinPath = Path.Combine(packageFolder, "Files", "bin");

			if (_fileSystem.ExistsDirectory(filesBinPath)) {
				string destinationPackagePath = Path.Combine(packagesDestinationRoot, packageName);
				string msg = isNetCore ? "NetCore" : "NetFramework";
				_logger.WriteInfo($"Copying {msg} package {packageName}");

				if (Program.IsDebugMode) {
					_logger.WriteInfo($"[DEBUG]   {packageName}: {packageFolder} -> {destinationPackagePath}");
				}

				_fileSystem.CreateDirectoryIfNotExists(destinationPackagePath);
				CopyDirectory(packageFolder, destinationPackagePath);
				copiedPackages++;
			}
			else {
				skippedPackages++;
				if (Program.IsDebugMode) {
					_logger.WriteInfo($"[DEBUG]   Skipped {packageName} (no Files/bin folder)");
				}
			}
		}

		if (Program.IsDebugMode) {
			string msg = isNetCore ? "NetCore" : "NetFramework";
			_logger.WriteInfo($"[DEBUG] {msg} packages summary: Copied={copiedPackages}, Skipped={skippedPackages}");
		}
	}

	private void CopyRootAssemblies(string extractedPath, string destination, bool isNetCore = true) {
		if (Program.IsDebugMode) {
			_logger.WriteInfo($"[DEBUG] CopyRootAssemblies: Source={extractedPath}, Destination={destination}");
		}

		string[] files = isNetCore 
			? _fileSystem.GetFiles(extractedPath)
			: _fileSystem.GetFiles(Path.Combine(extractedPath, "Terrasoft.WebApp", "bin"));
		int copiedCount = 0;

		if (copiedCount == 0) {
			_logger.WriteWarning($"No root assemblies found in {extractedPath}");
		}
		
		foreach (string file in files) {
			string fileName = Path.GetFileName(file);
			string extension = Path.GetExtension(fileName).ToLowerInvariant();

			// Copy only DLL and PDB files
			if (extension == ".dll" || extension == ".pdb") {
				string destFile = Path.Combine(destination, fileName);
				_fileSystem.CopyFile(file, destFile, true);
				copiedCount++;

				if (Program.IsDebugMode) {
					_logger.WriteInfo($"[DEBUG]   Copied: {fileName} -> {destFile}");
				}
			}
		}

		_logger.WriteInfo($"Copied {copiedCount} root assemblies (DLL and PDB files)");
	}

	private void DownloadNetCoreConfiguration(string extractedPath) {
		_logger.WriteInfo("Detected NetCore Creatio");

		// For NetCore (NET8), the structure is:
		// - Root DLL and PDB files -> .application/net-core/core-bin
		// - Terrasoft.Configuration/Lib/ -> .application/net-core/bin
		// - conf/bin/{NUMBER}/ -> .application/net-core/bin
		// - Terrasoft.Configuration/Pkg/ -> .application/net-core/packages (filtered by Files/bin)

		string coreBinDestination = _workspacePathBuilder.CoreBinFolderPath.Replace("net-framework", "net-core");
		string libDestination = _workspacePathBuilder.LibFolderPath.Replace("net-framework", "net-core");
		string configBinDestination
			= _workspacePathBuilder.ConfigurationBinFolderPath.Replace("net-framework", "net-core");

		// Copy root DLL and PDB files
		_logger.WriteInfo($"Copying NetCore root assemblies (DLL and PDB) to {coreBinDestination}");
		_fileSystem.CreateDirectoryIfNotExists(coreBinDestination);
		CopyRootAssemblies(extractedPath, coreBinDestination);

		// Copy Terrasoft.Configuration/Lib if exists
		string libPath = Path.Combine(extractedPath, "Terrasoft.Configuration", "Lib");
		if (_fileSystem.ExistsDirectory(libPath)) {
			_logger.WriteInfo($"Copying NetCore lib files to {libDestination}");
			_fileSystem.CreateDirectoryIfNotExists(libDestination);
			CopyAllFiles(libPath, libDestination);
		}

		// Copy conf/bin/{NUMBER} - select the latest numbered folder
		CopyConfigurationBinFiles(extractedPath, configBinDestination, true);

		// Copy packages with Files/bin filter
		CopyPackages(extractedPath, true);

		_logger.WriteInfo("NetCore configuration downloaded successfully");
	}

	private void DownloadNetFrameworkConfiguration(string extractedPath) {
		_logger.WriteInfo("Detected NetFramework Creatio");
		
		string coreBinDestination = _workspacePathBuilder.CoreBinFolderPath.Replace("net-core", "net-framework");
		string libDestination = _workspacePathBuilder.LibFolderPath.Replace("net-core", "net-framework");
		string configBinDestination
			= _workspacePathBuilder.ConfigurationBinFolderPath.Replace("net-core", "net-framework");
		
		
		// Copy root DLL and PDB files
		_logger.WriteInfo($"Copying NetFramework root assemblies (DLL and PDB) to {coreBinDestination}");
		_fileSystem.CreateDirectoryIfNotExists(coreBinDestination);
		CopyRootAssemblies(extractedPath, coreBinDestination, false);

		// Copy Terrasoft.Configuration/Lib if exists
		string libPath = Path.Combine(extractedPath, "Terrasoft.Configuration", "Lib");
		if (_fileSystem.ExistsDirectory(libPath)) {
			_logger.WriteInfo($"Copying NetFramework lib files to {libDestination}");
			_fileSystem.CreateDirectoryIfNotExists(libDestination);
			CopyAllFiles(libPath, libDestination);
		}

		// Copy conf/bin/{NUMBER} - select the latest numbered folder
		CopyConfigurationBinFiles(extractedPath, configBinDestination, false);

		// Copy packages with Files/bin filter
		CopyPackages(extractedPath, false);
		
		CopyLibFiles(extractedPath, false);
			
		_logger.WriteInfo("NetFramework configuration downloaded successfully");
	}

	private bool IsNetFrameworkCreatio(string extractedPath) {
		string netFrameworkMarkerPath = Path.Combine(extractedPath, NetFrameworkMarkerFolder);
		return _fileSystem.ExistsDirectory(netFrameworkMarkerPath);
	}

	#endregion

	#region Methods: Public

	public void DownloadFromDirectory(string directoryPath) {
		directoryPath.CheckArgumentNullOrWhiteSpace(nameof(directoryPath));

		if (Program.IsDebugMode) {
			_logger.WriteInfo($"[DEBUG] DownloadFromDirectory started: Directory={directoryPath}");
			_logger.WriteInfo($"[DEBUG]   Workspace root: {_workspacePathBuilder.RootPath}");
		}

		if (!_fileSystem.ExistsDirectory(directoryPath)) {
			throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
		}

		if (!_workspacePathBuilder.IsWorkspace) {
			throw new InvalidOperationException(
				"Current directory is not a workspace. Please run this command from a workspace directory.");
		}

		_logger.WriteInfo($"Processing Creatio configuration from directory: {directoryPath}");

		try {
			// Detect Creatio type and download configuration
			if (IsNetFrameworkCreatio(directoryPath)) {
				DownloadNetFrameworkConfiguration(directoryPath);
			}
			else {
				DownloadNetCoreConfiguration(directoryPath);
			}

			_logger.WriteInfo("Configuration download from directory completed successfully");
		}
		catch (Exception ex) {
			_logger.WriteError($"Error downloading configuration from directory: {ex.Message}");
			if (Program.IsDebugMode) {
				_logger.WriteError($"[DEBUG] Stack trace: {ex.StackTrace}");
			}

			throw;
		}
	}

	public void DownloadFromPath(string path) {
		path.CheckArgumentNullOrWhiteSpace(nameof(path));

		bool isZipFile = Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);

		if (isZipFile) {
			DownloadFromZip(path);
		}
		else {
			DownloadFromDirectory(path);
		}
	}

	public void DownloadFromZip(string zipFilePath) {
		zipFilePath.CheckArgumentNullOrWhiteSpace(nameof(zipFilePath));

		if (Program.IsDebugMode) {
			_logger.WriteInfo($"[DEBUG] DownloadFromZip started: ZipFile={zipFilePath}");
			_logger.WriteInfo($"[DEBUG]   Workspace root: {_workspacePathBuilder.RootPath}");
		}

		if (!_fileSystem.ExistsFile(zipFilePath)) {
			throw new FileNotFoundException($"Zip file not found: {zipFilePath}");
		}

		if (!_workspacePathBuilder.IsWorkspace) {
			throw new InvalidOperationException(
				"Current directory is not a workspace. Please run this command from a workspace directory.");
		}

		_logger.WriteInfo($"Extracting Creatio from {zipFilePath}");

		_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
			try {
				if (Program.IsDebugMode) {
					_logger.WriteInfo($"[DEBUG]   Temporary directory created: {tempDirectory}");
				}

				// Extract zip to temp directory
				_compressionUtilities.Unzip(zipFilePath, tempDirectory);
				_logger.WriteInfo(
					$"Extracted to temporary directory: {tempDirectory}"); // Detect Creatio type and download configuration
				if (IsNetFrameworkCreatio(tempDirectory)) {
					DownloadNetFrameworkConfiguration(tempDirectory);
				}
				else {
					DownloadNetCoreConfiguration(tempDirectory);
				}
			}
			catch (Exception ex) {
				_logger.WriteError($"Error downloading configuration from zip: {ex.Message}");
				throw;
			}
		});

		_logger.WriteInfo("Configuration download from zip completed successfully");
	}

	#endregion
}

#endregion
