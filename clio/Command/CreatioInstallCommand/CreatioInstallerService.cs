using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.DeploymentStrategies;
using Clio.Common.db;
using Clio.Common.K8;
using Clio.Common.ScenarioHandlers;
using Clio.UserEnvironment;
using MediatR;
using StackExchange.Redis;
using FileSystem = Clio.Common.FileSystem;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Command.CreatioInstallCommand;

public interface ICreatioInstallerService
{

	int Execute(PfInstallerOptions options);
	string GetBuildFilePathFromOptions(string product, CreatioDBType dBType,
		CreatioRuntimePlatform runtimePlatform);
	
	int StartWebBrowser(PfInstallerOptions options);
	int StartWebBrowser(PfInstallerOptions options, bool isIisDeployment);
	int DoPgWork(DirectoryInfo unzippedDirectory, string destDbName, string templateName = "");

}

public class CreatioInstallerService : Command<PfInstallerOptions>, ICreatioInstallerService
{

	#region Fields: Private

	private static readonly Action<string, string, IProgress<double>> CopyFileWithProgress =
		(sourcePath, destinationPath, progress) => {
			const int bufferSize = 1024 * 1024; // 1MB
			byte[] buffer = new byte[bufferSize];
			int bytesRead;

			using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read);
			long totalBytes = sourceStream.Length;

			using FileStream destinationStream = new(destinationPath, FileMode.OpenOrCreate, FileAccess.Write);
			while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0) {
				destinationStream.Write(buffer, 0, bytesRead);
				// Report progress
				double percentage = 100d * sourceStream.Position / totalBytes;
				progress.Report(percentage);
			}
		};

	private readonly string _iisRootFolder;
	private readonly IPackageArchiver _packageArchiver;
	private readonly k8Commands _k8;
	private readonly IMediator _mediator;
	private readonly RegAppCommand _registerCommand;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly DeploymentStrategyFactory _deploymentStrategyFactory;
	private readonly HealthCheckCommand _healthCheckCommand;

	#endregion

	#region Fields: Protected

	protected string ProductFolder;
	protected string RemoteArtefactServerPath;

	#endregion

	#region Constructors: Public

	public CreatioInstallerService(IPackageArchiver packageArchiver, k8Commands k8,
		IMediator mediator, RegAppCommand registerCommand, ISettingsRepository settingsRepository,
		IFileSystem fileSystem, ILogger logger, DeploymentStrategyFactory deploymentStrategyFactory,
		HealthCheckCommand healthCheckCommand) {
		_packageArchiver = packageArchiver;
		_k8 = k8;
		_mediator = mediator;
		_registerCommand = registerCommand;
		_fileSystem = fileSystem;
		_iisRootFolder = settingsRepository.GetIISClioRootPath();
		ProductFolder = settingsRepository.GetCreatioProductsFolder();
		RemoteArtefactServerPath = settingsRepository.GetRemoteArtefactServerPath();
		_logger = logger;
		_deploymentStrategyFactory = deploymentStrategyFactory;
		_healthCheckCommand = healthCheckCommand;
	}

	public CreatioInstallerService() {

	}

	#endregion

	#region Methods: Private

	private int ExitWithErrorMessage(string message){
		_logger.WriteError(message);
		return 1;
	}

	private int ExitWithOkMessage(string message){
		_logger.WriteInfo(message);
		return 0;
	}

	private int FindEmptyRedisDb(int port){
		ConfigurationOptions configurationOptions = new ConfigurationOptions() {
			SyncTimeout = 500000,
			EndPoints =
			{
				{$"{BindingsModule.k8sDns}",port }
			},
			AbortOnConnectFail = false // Prevents exceptions when the initial connection to Redis fails, allowing the client to retry connecting.
		};
		ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(configurationOptions);
		IServer server = redis.GetServer($"{BindingsModule.k8sDns}", port);
		int count = server.DatabaseCount;
		for (int i = 1; i < count; i++) {
			long records = server.DatabaseSize(i);
			if (records == 0) {
				return i;
			}
		}
		return -1;
	}

	public int StartWebBrowser(PfInstallerOptions options) {
		return StartWebBrowser(options, false);
	}

	public int StartWebBrowser(PfInstallerOptions options, bool isIisDeployment) {

		string url = isIisDeployment 
			? $"http://{InstallerHelper.FetFQDN()}:{options.SitePort}"
			: $"http://localhost:{options.SitePort}";
		
		try {
			// Windows
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") {CreateNoWindow = true});
			}
			//Linux
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				Process.Start("xdg-open", url);
			}
			// macOS
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				Process.Start("open", url);
			}
		} 
		catch(Exception ex) {
			_logger.WriteError($"Failed to launch web browser: {ex.Message}");
			return 1;
		}
		return 0;
	}

	private string CopyLocalWhenNetworkDrive(string path){
		if (path.StartsWith(@"\\")) {
			return CopyZipLocal(path);
		}

		// DriveInfo is Windows-specific. On macOS/Linux, network drives are handled differently
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return path;
		}

		
		if(path.StartsWith(".\\")){
			path = Path.GetFullPath(path);
		}
		
		return new DriveInfo(Path.GetPathRoot(path)) switch {
			{DriveType: DriveType.Network} => CopyZipLocal(path),
			_ => path
		};
	}

	private string CopyZipLocal(string src){
		if (!Directory.Exists(ProductFolder)) {
			Directory.CreateDirectory(ProductFolder);
		}

		FileInfo srcInfo = new(src);
		string dest = Path.Join(ProductFolder, srcInfo.Name);

		if (File.Exists(dest)) {
			return dest;
		}

		_logger.WriteLine($"Detected network drive as source, copying to local folder {ProductFolder}");
		Console.Write("Copy Progress:    ");
		Progress<double> progressReporter = new(progress => {
			string result = progress switch {
				< 10 => progress.ToString("0").PadLeft(2) + " %",
				< 100 => progress.ToString("0").PadLeft(1) + " %",
				100 => "100 %",
				_ => ""
			};
			Console.CursorLeft = 15;
			Console.Write(result);
		});
		CopyFileWithProgress(src, dest, progressReporter);
		return dest;
	}

	private async Task<int> CreateIISSite(DirectoryInfo unzippedDirectory, PfInstallerOptions options){
		_logger.WriteInfo("[Create IIS Site] - Started");
		CreateIISSiteRequest request = new() {
			Arguments = new Dictionary<string, string> {
				{"siteName", options.SiteName},
				{"port", options.SitePort.ToString()},
				{"sourceDirectory", unzippedDirectory.FullName},
				{"destinationDirectory", _iisRootFolder}, {
					"isNetFramework",
					(InstallerHelper.DetectFramework(unzippedDirectory) == InstallerHelper.FrameworkType.NetFramework)
					.ToString()
				}
			}
		};
		return (await _mediator.Send(request)).Value switch {
			(HandlerError error) => ExitWithErrorMessage(error.ErrorDescription),
			(CreateIISSiteResponse {Status: BaseHandlerResponse.CompletionStatus.Success} result) => ExitWithOkMessage(
				result.Description),
			(CreateIISSiteResponse {Status: BaseHandlerResponse.CompletionStatus.Failure} result) =>
				ExitWithErrorMessage(result.Description),
			_ => ExitWithErrorMessage("Unknown error occured")
		};
	}

	private IDeploymentStrategy SelectDeploymentStrategy(PfInstallerOptions options) {
		IDeploymentStrategy strategy = _deploymentStrategyFactory.SelectStrategy(
			options.DeploymentMethod ?? "auto",
			options.NoIIS
		);
		return strategy;
	}

	private void CreatePgTemplate(DirectoryInfo unzippedDirectory, string tmpDbName){
		k8Commands.ConnectionStringParams csp = _k8.GetPostgresConnectionString();
		Postgres postgres = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		bool exists = postgres.CheckTemplateExists(tmpDbName);
		if (exists) {
			_logger.WriteInfo($"[Database restore] - Template '{tmpDbName}' already exists, skipping restore");
			return;
		}
		
		// Search for backup file in db directory or root
		FileInfo src = unzippedDirectory.GetDirectories("db").FirstOrDefault()?.GetFiles("*.backup").FirstOrDefault();
		if (src is null) {
			src = unzippedDirectory?.GetFiles("*.backup").FirstOrDefault();
		}
		
		// Log detailed information if backup file not found
		if (src is null) {
			_logger.WriteError($"[Database restore failed] - Backup file not found in {unzippedDirectory.FullName}");
			_logger.WriteError($"[Database restore failed] - Directory structure: {string.Join(", ", unzippedDirectory.GetDirectories().Select(d => d.Name))}");
			var files = unzippedDirectory.GetFiles("*.*", System.IO.SearchOption.TopDirectoryOnly);
			_logger.WriteError($"[Database restore failed] - Files in root: {string.Join(", ", files.Take(10).Select(f => f.Name))}");
			
			// Check if db directory exists and list its contents
			var dbDir = unzippedDirectory.GetDirectories("db").FirstOrDefault();
			if (dbDir != null) {
				var dbFiles = dbDir.GetFiles("*.*", System.IO.SearchOption.TopDirectoryOnly);
				_logger.WriteError($"[Database restore failed] - Files in db/: {string.Join(", ", dbFiles.Take(10).Select(f => f.Name))}");
			}
			
			throw new FileNotFoundException("Backup file not found in the specified directory.");
		}
		
		_logger.WriteInfo($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");

		_k8.CopyBackupFileToPod(k8Commands.PodType.Postgres, src.FullName, src.Name);

		postgres.CreateDb(tmpDbName);
		_k8.RestorePgDatabase(src.Name, tmpDbName);
		postgres.SetDatabaseAsTemplate(tmpDbName);
		_k8.DeleteBackupImage(k8Commands.PodType.Postgres, src.Name);
		_logger.WriteInfo($"[Completed Database restore] - {DateTime.Now:hh:mm:ss}");
	}

	private int DoMsWork(DirectoryInfo unzippedDirectory, string siteName){
		FileInfo src = unzippedDirectory.GetDirectories("db").FirstOrDefault()?.GetFiles("*.bak").FirstOrDefault();
		_logger.WriteInfo($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");
		
		if(src is not {Exists: true}) {
			throw new FileNotFoundException("Backup file not found in the specified directory.");
		}
		
		bool useFs = false;
		string dest = Path.Join("\\\\wsl.localhost","rancher-desktop","mnt","clio-infrastructure","mssql","data", $"{siteName}.bak");
		if(src.Length < int.MaxValue) {
			_k8.CopyBackupFileToPod(k8Commands.PodType.Mssql, src.FullName, $"{siteName}.bak");
		}else {
			//This is a hack, we have to fix Cp class to allow large files
			useFs = true;
			_logger.WriteWarning($"Copying large file to local directory {dest}" );
			_fileSystem.CopyFile(src.FullName, dest, true);
		}
		k8Commands.ConnectionStringParams csp = _k8.GetMssqlConnectionString();
		Mssql mssql = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		bool exists = mssql.CheckDbExists(siteName);
		if (!exists) {
			mssql.CreateDb(siteName, $"{siteName}.bak");
		}
		if(useFs) {
			_fileSystem.DeleteFile(dest);
		}else {
			_k8.DeleteBackupImage(k8Commands.PodType.Mssql, $"{siteName}.bak");
		}
		return 0;
	}

	public int DoPgWork(DirectoryInfo unzippedDirectory, string destDbName, string templateName = "") {

		string tmpDbName = string.IsNullOrWhiteSpace(templateName) ? "template_" + unzippedDirectory.Name : "template_"+templateName;
		k8Commands.ConnectionStringParams csp = _k8.GetPostgresConnectionString();
		Postgres postgres = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		CreatePgTemplate(unzippedDirectory, tmpDbName);
		postgres.CreateDbFromTemplate(tmpDbName, destDbName);
		_logger.WriteInfo($"[Database created] - {destDbName}");
		return 0;
	}

	private Version GetLatestProductVersion(string latestBranchPath, Version latestVersion, string product,
		CreatioRuntimePlatform platform){
		string dirPath = Path.Combine(latestBranchPath, latestVersion.ToString(),
			GetProductDirectoryName(product, platform));
		if (Directory.Exists(dirPath)) {
			return latestVersion;
		}
		Version previousVersion = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build,
			latestVersion.Revision - 1);
		return GetLatestProductVersion(latestBranchPath, previousVersion, product, platform);
	}

	private string GetProductDirectoryName(string product, CreatioRuntimePlatform platform){
		return $"{product}{platform.ToRuntimePlatformString()}_Softkey_ENU";
	}

	private string GetProductFileNameWithoutBuildNumber(string product, CreatioDBType creatioDBType,
		CreatioRuntimePlatform creatioRuntimePlatform){
		return $"_{product}{creatioRuntimePlatform.ToRuntimePlatformString()}_Softkey_{creatioDBType}_ENU.zip";
	}

	private async Task<int> UpdateConnectionString(DirectoryInfo unzippedDirectory, PfInstallerOptions options){
		_logger.WriteInfo("[CheckUpdate connection string] - Started");
		InstallerHelper.DatabaseType dbType;
		try {
			dbType = InstallerHelper.DetectDataBase(unzippedDirectory);
		} catch (Exception ex) {
			_logger.WriteWarning($"[DetectDataBase] - Could not detect database type: {ex.Message}");
			_logger.WriteInfo("[DetectDataBase] - Defaulting to PostgreSQL");
			dbType = InstallerHelper.DatabaseType.Postgres;
		}
		k8Commands.ConnectionStringParams csParam = dbType switch {
			InstallerHelper.DatabaseType.Postgres => _k8.GetPostgresConnectionString(),
			InstallerHelper.DatabaseType.MsSql => _k8.GetMssqlConnectionString()
		};

		int redisDb = FindEmptyRedisDb(csParam.RedisPort);

		// Determine the folder path based on deployment strategy
		string folderPath = DetermineFolderPath(options);
		_logger.WriteInfo($"[Connection string] - Target folder path: {folderPath}");

		ConfigureConnectionStringRequest request = dbType switch {
			InstallerHelper.DatabaseType.Postgres => new ConfigureConnectionStringRequest {
				Arguments = new Dictionary<string, string> {
					{"folderPath", folderPath}, {
						"dbString",
						$"Server={BindingsModule.k8sDns};Port={csParam.DbPort};Database={options.SiteName};User ID={csParam.DbUsername};password={csParam.DbPassword};Timeout=500; CommandTimeout=400;MaxPoolSize=1024;"
					},
					{"redis", $"host={BindingsModule.k8sDns};db={redisDb};port={csParam.RedisPort}"}, {
						"isNetFramework",
						(InstallerHelper.DetectFramework(unzippedDirectory) ==
							InstallerHelper.FrameworkType.NetFramework).ToString()
					}
				}
			},
			InstallerHelper.DatabaseType.MsSql => new ConfigureConnectionStringRequest {
				Arguments = new Dictionary<string, string> {
					{"folderPath", folderPath}, {
						"dbString",
						$"Data Source={BindingsModule.k8sDns},{csParam.DbPort};Initial Catalog={options.SiteName};User Id={csParam.DbUsername}; Password={csParam.DbPassword};MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100"
					},
					{"redis", $"host={BindingsModule.k8sDns};db={redisDb};port={csParam.RedisPort}"}, {
						"isNetFramework",
						(InstallerHelper.DetectFramework(unzippedDirectory) ==
							InstallerHelper.FrameworkType.NetFramework).ToString()
					}
				}
			}
		};

		return (await _mediator.Send(request)).Value switch {
			(HandlerError error) => ExitWithErrorMessage(error.ErrorDescription),
			(ConfigureConnectionStringResponse {
				Status: BaseHandlerResponse.CompletionStatus.Success
			} result) => ExitWithOkMessage(result.Description),
			(ConfigureConnectionStringResponse {
				Status: BaseHandlerResponse.CompletionStatus.Failure
			} result) => ExitWithErrorMessage(result.Description),
			_ => ExitWithErrorMessage("Unknown error occured")
		};
	}

	/// <summary>
	/// Determines the folder path based on whether deployment is IIS or DotNet.
	/// For IIS: uses _iisRootFolder + site name
	/// For DotNet: uses current directory + site name (or AppPath if provided)
	/// </summary>
	private string DetermineFolderPath(PfInstallerOptions options) {
		IDeploymentStrategy strategy = SelectDeploymentStrategy(options);
		
		// Validate site name is not empty
		if (string.IsNullOrWhiteSpace(options.SiteName)) {
			_logger.WriteError("Site name is required but was empty");
			throw new InvalidOperationException("Site name must not be empty");
		}
		
		if (strategy is IISDeploymentStrategy) {
			// IIS deployment uses configured IIS root path
			if (string.IsNullOrWhiteSpace(_iisRootFolder)) {
				_logger.WriteError("IIS root folder is not configured");
				throw new InvalidOperationException("IIS root folder must be configured for IIS deployment");
			}
			return Path.Combine(_iisRootFolder, options.SiteName);
		} 
		else {
			// DotNet deployment uses current directory or specified AppPath
			if (!string.IsNullOrEmpty(options.AppPath)) {
				return options.AppPath;
			}
			return Path.Combine(Directory.GetCurrentDirectory(), options.SiteName);
		}
	}

	#endregion

	#region Methods: Internal

	internal string GetBuildFilePathFromOptions(string remoteArtifactServerPath, string product,
		CreatioDBType creatioDBType, CreatioRuntimePlatform platform){
		Version latestBranchVersion = GetLatestVersion(remoteArtifactServerPath);
		string latestBranchesBuildPath = Path.Combine(remoteArtifactServerPath, latestBranchVersion.ToString());
		IDirectoryInfo latestBranchesDireInfo = _fileSystem.GetDirectoryInfo(latestBranchesBuildPath);
		IOrderedEnumerable<IDirectoryInfo> latestBranchSubdirectories = latestBranchesDireInfo.GetDirectories()
			.OrderByDescending(dir => dir.CreationTimeUtc);
		List<IDirectoryInfo> revisionDirectories = new List<IDirectoryInfo>();
		foreach (IDirectoryInfo subdir in latestBranchSubdirectories) {
			if (Version.TryParse(subdir.Name, out Version ver)) {
				revisionDirectories.Add(subdir);
			}
		}
		if (revisionDirectories.Count == 0) {
			revisionDirectories.Add(latestBranchesDireInfo);
		}
		string productZipFileName = GetProductFileNameWithoutBuildNumber(product, creatioDBType, platform);
		foreach (IDirectoryInfo searchDir in revisionDirectories) {
			IOrderedEnumerable<IFileInfo> zipFiles = searchDir.GetFiles("*.zip", SearchOption.AllDirectories).ToList()
				.OrderByDescending(product => product.LastWriteTime);
			foreach (IFileInfo zipFile in zipFiles) {
				if (zipFile.Name.Contains(productZipFileName, StringComparison.OrdinalIgnoreCase)) {
					return zipFile.FullName;
				}
			}
		}
		throw new ItemNotFoundException(productZipFileName);
	}

	internal Version GetLatestVersion(string remoteArtifactServerPath){
		string[] branches = _fileSystem.GetDirectories(remoteArtifactServerPath);
		List<Version> version = new List<Version>();
		foreach (string branch in branches) {
			string branchName = branch.Split(Path.DirectorySeparatorChar).Last();
			if (Version.TryParse(branchName, out Version ver)) {
				version.Add(ver);
			}
		}
		return version.Max();
	}

	private bool WaitForServerReady(string environmentName) {
		const int initialDelaySeconds = 15; // Initial delay to allow server to start
		const int maxAttempts = 10; // Increased attempts for longer wait time
		const int delaySeconds = 3;
		
		_logger.WriteInfo($"Waiting {initialDelaySeconds} seconds for server to start...");
		System.Threading.Thread.Sleep(initialDelaySeconds * 1000);
		
		for (int attempt = 1; attempt <= maxAttempts; attempt++) {
			HealthCheckOptions healthOptions = new() {
				Environment = environmentName
			};
			int result = _healthCheckCommand.Execute(healthOptions);
			if (result == 0) {
				_logger.WriteInfo($"Server is ready after {attempt} attempt(s).");
				return true;
			}

			if (attempt < maxAttempts) {
				_logger.WriteInfo($"Waiting for server to become ready... ({attempt}/{maxAttempts}). Next check in {delaySeconds} seconds.");
				System.Threading.Thread.Sleep(delaySeconds * 1000);
			}
		}

		_logger.WriteWarning($"Server did not become ready after {initialDelaySeconds + (maxAttempts * delaySeconds)} seconds.");
		return false;
	}

	#endregion

	#region Methods: Public

	public override int Execute(PfInstallerOptions options){
		if (string.IsNullOrEmpty(options.ZipFile) && !string.IsNullOrEmpty(options.Product)) {
			options.ZipFile = GetBuildFilePathFromOptions(options.Product, options.DBType, options.RuntimePlatform);
		}
		if (!File.Exists(options.ZipFile)) {
			_logger.WriteInfo($"Could not find zip file: {options.ZipFile}");
			return 1;
		}

		// Determine deployment strategy to know whether to use IIS or DotNet
		IDeploymentStrategy strategy = SelectDeploymentStrategy(options);
		bool isIisDeployment = strategy is IISDeploymentStrategy;

		// Only use IIS root folder validation for IIS deployments
		if (isIisDeployment) {
			if (!Directory.Exists(_iisRootFolder)) {
				Directory.CreateDirectory(_iisRootFolder);
			}
		}

		// STEP 1: Get site name from user
		while (string.IsNullOrEmpty(options.SiteName)) {
			Console.WriteLine("Please enter site name:");
			string? input = Console.ReadLine();
			options.SiteName = input?.Trim() ?? string.Empty;
			
			if (string.IsNullOrEmpty(options.SiteName)) {
				Console.WriteLine("Site name cannot be empty");
				continue;
			}

			// Validate site name against appropriate root folder
			string rootPath = isIisDeployment 
				? _iisRootFolder 
				: Directory.GetCurrentDirectory();

			if (Directory.Exists(Path.Combine(rootPath, options.SiteName))) {
				Console.WriteLine(
					$"Site with name {options.SiteName} already exists in {Path.Combine(rootPath, options.SiteName)}");
				options.SiteName = string.Empty;
			}
		}

		// STEP 2: Get port from user
		// Only prompt for port on Windows IIS deployments
		// DotNet deployments on macOS/Linux use default port or user-specified port
		if (isIisDeployment) {
			while (options.SitePort is <= 0 or > 65536) {
				Console.WriteLine(
					$"Please enter site port, Max value - 65535:{Environment.NewLine}(recommended range between 40000 and 40100)");
				if (int.TryParse(Console.ReadLine(), out int value)) {
					options.SitePort = value;
				} else {
					Console.WriteLine("Site port must be an in value");
				}
			}
		} 
		else {
			// For DotNet deployments, check if user wants to use custom port or default
			Console.WriteLine("Port configuration for DotNet deployment:");
			
			// If port was already specified via command line, use it
			if (options.SitePort > 0 && options.SitePort <= 65535) {
				// Port already set, skip prompting
			} else {
				bool portSelected = false;
				
				while (!portSelected) {
					Console.WriteLine("Press Enter to use default port 8080, or enter a custom port number:");
					string portInput = (Console.ReadLine() ?? string.Empty).Trim();
					
					int selectedPort = 8080; // Default
					
					if (string.IsNullOrEmpty(portInput)) {
						selectedPort = 8080;
					} else if (int.TryParse(portInput, out int customPort)) {
						if (customPort > 0 && customPort <= 65535) {
							selectedPort = customPort;
						} else {
							Console.WriteLine("Invalid port number. Port must be between 1 and 65535. Please try again.");
							continue;
						}
					} else {
						Console.WriteLine("Invalid port input. Please enter a number between 1 and 65535.");
						continue;
					}

					// Check port availability for DotNet deployment
					if (!IsPortAvailable(selectedPort)) {
						Console.WriteLine($"⚠ WARNING: Port {selectedPort} appears to be in use by another process.");
						Console.WriteLine("What would you like to do?");
						Console.WriteLine("1. Select a different port (press 1)");
						Console.WriteLine("2. Try another port (press Enter or any other key for port selection)");
						
						string choice = (Console.ReadLine() ?? string.Empty).ToLower().Trim();
						if (choice == "1") {
							continue; // Loop back to ask for port again
						} else {
							continue; // Also loop back
						}
					}
					
					options.SitePort = selectedPort;
					portSelected = true;
				}
			}
			
			// Ensure we have a valid port
			if (options.SitePort <= 0 || options.SitePort > 65535) {
				options.SitePort = 8080;
			}
		}

		// STEP 3: Now output all logging information after user has provided input
		_logger.WriteLine(); // Blank line for readability
		_logger.WriteInfo($"[OS Platform] - {(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "Windows")}");
		_logger.WriteInfo($"[Is IIS Deployment] - {isIisDeployment}");
		_logger.WriteInfo($"[Site Name] - {options.SiteName}");
		_logger.WriteInfo($"[Site Port] - {options.SitePort}");

		options.ZipFile = CopyLocalWhenNetworkDrive(options.ZipFile);
		string deploymentFolder = DetermineFolderPath(options);
		_logger.WriteInfo($"[Starting unzipping] - {options.ZipFile} to {deploymentFolder}");
		//DirectoryInfo unzippedDirectory = InstallerHelper.UnzipOrTakeExisting(options.ZipFile, deploymentFolder, _packageArchiver); //TODO: BUG, this no longer creates an unzipped folder as a template
		DirectoryInfo unzippedDirectory = InstallerHelper.UnzipOrTakeExistingOld(options.ZipFile, _packageArchiver);
		
		if (!_fileSystem.ExistsDirectory(deploymentFolder)) {
			_fileSystem.CreateDirectory(deploymentFolder);
		}
		_fileSystem.CopyDirectory(unzippedDirectory.FullName, deploymentFolder, true);
		DirectoryInfo deploymentFolderInfo = new DirectoryInfo(deploymentFolder);
		
		_logger.WriteInfo($"[Unzip completed] - {unzippedDirectory.FullName}");
		_logger.WriteLine();

		InstallerHelper.DatabaseType dbType;
		try {
			dbType = InstallerHelper.DetectDataBase(unzippedDirectory);
		} catch (Exception ex) {
			_logger.WriteWarning($"[DetectDataBase] - Could not detect database type: {ex.Message}");
			_logger.WriteInfo("[DetectDataBase] - Defaulting to Postgres");
			dbType = InstallerHelper.DatabaseType.Postgres;
		}

		int dbRestoreResult = dbType switch {
			InstallerHelper.DatabaseType.MsSql => DoMsWork(unzippedDirectory, options.SiteName),
			var _ => DoPgWork(unzippedDirectory, options.SiteName, Path.GetFileNameWithoutExtension(options.ZipFile))
		};

		int deploySiteResult = dbRestoreResult switch {
			0 => DeployApplication(deploymentFolderInfo, options),
			var _ => ExitWithErrorMessage("Database restore failed")
		};

		
		int updateConnectionStringResult = deploySiteResult switch {
			0 => UpdateConnectionString(deploymentFolderInfo, options).GetAwaiter().GetResult(),
			var _ => ExitWithErrorMessage("Failed to deploy application")
		};

		string uri = $"http://localhost:{options.SitePort}";
		if (isIisDeployment) {
			uri = $"http://{InstallerHelper.FetFQDN()}:{options.SitePort}";
		}
		_registerCommand.Execute(new RegAppOptions {
			EnvironmentName = options.SiteName,
			Login = "Supervisor",
			Password = "Supervisor",
			Uri = uri,
			IsNetCore = InstallerHelper.DetectFramework(deploymentFolderInfo) == InstallerHelper.FrameworkType.NetCore,
			EnvironmentPath = deploymentFolder
		});

		// For DotNet deployments, wait for server to become ready before proceeding
		if (!isIisDeployment) {
			_logger.WriteInfo("Waiting for server to become ready...");
			if (!WaitForServerReady(options.SiteName)) {
				_logger.WriteWarning("Server did not become ready within the timeout period.");
			}
		}

		if (options.AutoRun) {
			_logger.WriteInfo("[Auto-launching application]");
			StartWebBrowser(options,isIisDeployment);
		}

		return 0;
	}

	private int DeployApplication(DirectoryInfo unzippedDirectory, PfInstallerOptions options) {
		try {
			IDeploymentStrategy strategy = SelectDeploymentStrategy(options);
			int result = strategy.Deploy(unzippedDirectory, options).GetAwaiter().GetResult();
			
			if (result == 0) {
				string url = strategy.GetApplicationUrl(options);
				_logger.WriteInfo($"[Application deployed successfully] - URL: {url}");
			}
			
			return result;
		}
		catch (Exception ex) {
			return ExitWithErrorMessage($"Deployment failed: {ex.Message}");
		}
	}

	public string GetBuildFilePathFromOptions(string product, CreatioDBType dBType,
		CreatioRuntimePlatform runtimePlatform){
		return GetBuildFilePathFromOptions(RemoteArtefactServerPath, product, dBType, runtimePlatform);
	}

	private bool IsPortAvailable(int port) {
		try {
			IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
			TcpConnectionInformation[] tcpConnections = ipGlobalProperties.GetActiveTcpConnections();
			
			foreach (TcpConnectionInformation tcpConnection in tcpConnections) {
				if (tcpConnection.LocalEndPoint.Port == port) {
					_logger.WriteWarning($"Port {port} is in use (active connection)");
					return false;
				}
			}
			
			IPEndPoint[] listeners = ipGlobalProperties.GetActiveTcpListeners();
			foreach (IPEndPoint listener in listeners) {
				if (listener.Port == port) {
					_logger.WriteWarning($"Port {port} is in use (listening port)");
					return false;
				}
			}
			
			_logger.WriteInfo($"Port {port} is available");
			return true;
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Could not check port availability: {ex.Message}. Assuming port is available.");
			return true;
		}
	}

	#endregion

}
