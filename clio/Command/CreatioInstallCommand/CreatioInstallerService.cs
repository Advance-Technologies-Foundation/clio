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
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.DeploymentStrategies;
using Clio.Common.K8;
using Clio.Common.ScenarioHandlers;
using Clio.UserEnvironment;
using MediatR;
using StackExchange.Redis;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Command.CreatioInstallCommand;

public interface ICreatioInstallerService{
	#region Methods: Public

	int DoPgWork(DirectoryInfo unzippedDirectory, string destDbName, string templateName = "");

	int Execute(PfInstallerOptions options);

	string GetBuildFilePathFromOptions(string product, CreatioDBType dBType,
		CreatioRuntimePlatform runtimePlatform);

	int StartWebBrowser(PfInstallerOptions options);
	int StartWebBrowser(PfInstallerOptions options, bool isIisDeployment);

	#endregion
}

public class CreatioInstallerService : Command<PfInstallerOptions>, ICreatioInstallerService{
	#region Fields: Private

	private readonly IBackupFileDetector _backupFileDetector;
	private readonly IDbClientFactory _dbClientFactory;
	private readonly IDbConnectionTester _dbConnectionTester;
	private readonly DeploymentStrategyFactory _deploymentStrategyFactory;
	private readonly IFileSystem _fileSystem;
	private readonly HealthCheckCommand _healthCheckCommand;

	private readonly string _iisRootFolder;
	private readonly k8Commands _k8;
	private readonly ILogger _logger;
	private readonly IMediator _mediator;
	private readonly IPackageArchiver _packageArchiver;
	private readonly IPostgresToolsPathDetector _postgresToolsPathDetector;
	private readonly RegAppCommand _registerCommand;
	private readonly ISettingsRepository _settingsRepository;
	private readonly string[] _excludedDirectories = ["db"];

	private readonly string[] _excludedExtensions = [".bak", ".backup"];

	#endregion

	#region Fields: Protected

	protected string ProductFolder;
	protected string RemoteArtefactServerPath;

	#endregion

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

	#region Constructors: Public

	public CreatioInstallerService(IPackageArchiver packageArchiver, k8Commands k8,
		IMediator mediator, RegAppCommand registerCommand, ISettingsRepository settingsRepository,
		IFileSystem fileSystem, ILogger logger, DeploymentStrategyFactory deploymentStrategyFactory,
		HealthCheckCommand healthCheckCommand, IDbClientFactory dbClientFactory,
		IDbConnectionTester dbConnectionTester, IBackupFileDetector backupFileDetector,
		IPostgresToolsPathDetector postgresToolsPathDetector) {
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
		_settingsRepository = settingsRepository;
		_dbClientFactory = dbClientFactory;
		_dbConnectionTester = dbConnectionTester;
		_backupFileDetector = backupFileDetector;
		_postgresToolsPathDetector = postgresToolsPathDetector;
	}

	public CreatioInstallerService() { }

	#endregion

	#region Methods: Private

	private static (int dbNumber, string errorMessage)
		FindEmptyRedisDb(string hostname = "localhost", int port = 6379) {
		try {
			ConfigurationOptions configurationOptions = new() {
				SyncTimeout = 500000,
				EndPoints = {
					{ hostname, port }
				},
				AbortOnConnectFail
					= false // Prevents exceptions when the initial connection to Redis fails, allowing the client to retry connecting.
			};
			ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(configurationOptions);
			IServer server = redis.GetServer($"{hostname}", port);
			int count = server.DatabaseCount;
			for (int i = 1; i < count; i++) {
				long records = server.DatabaseSize(i);
				if (records == 0) {
					return (i, string.Empty);
				}
			}

			// All databases are occupied
			string errorMsg = $"[Redis Configuration Error] Could not find an empty Redis database. " +
							  $"All {count - 1} available databases (1-{count - 1}) at {hostname}:{port} are in use. " +
							  $"Please either: " +
							  $"1) Clear some Redis databases, " +
							  $"2) Increase the number of Redis databases, " +
							  $"3) Manually specify a database number using the --redis-db option";

			return (-1, errorMsg);
		}
		catch (Exception ex) {
			string errorMsg = $"[Redis Connection Error] Could not connect to Redis at {hostname}:{port}. " +
							  $"Error: {ex.Message}. " +
							  $"Make sure Redis is running and accessible. " +
							  $"You can also manually specify a database number using the --redis-db option";

			return (-1, errorMsg);
		}
	}

	private string CopyLocalWhenNetworkDrive(string path) {
		if (path.StartsWith(@"\\")) {
			return CopyZipLocal(path);
		}

		// DriveInfo is Windows-specific. On macOS/Linux, network drives are handled differently
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return path;
		}


		if (path.StartsWith(".\\")) {
			path = Path.GetFullPath(path);
		}

		return new DriveInfo(Path.GetPathRoot(path)) switch {
				   { DriveType: DriveType.Network } => CopyZipLocal(path),
				   var _ => path
			   };
	}

	private string CopyZipLocal(string src) {
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
								var _ => ""
							};
			Console.CursorLeft = 15;
			Console.Write(result);
		});
		CopyFileWithProgress(src, dest, progressReporter);
		return dest;
	}

	private async Task<int> CreateIISSite(DirectoryInfo unzippedDirectory, PfInstallerOptions options) {
		_logger.WriteInfo("[Create IIS Site] - Started");
		CreateIISSiteRequest request = new() {
			Arguments = new Dictionary<string, string> {
				{ "siteName", options.SiteName },
				{ "port", options.SitePort.ToString() },
				{ "sourceDirectory", unzippedDirectory.FullName },
				{ "destinationDirectory", _iisRootFolder }, {
					"isNetFramework",
					(InstallerHelper.DetectFramework(unzippedDirectory) == InstallerHelper.FrameworkType.NetFramework)
					.ToString()
				}
			}
		};
		return (await _mediator.Send(request)).Value switch {
				   HandlerError error => ExitWithErrorMessage(error.ErrorDescription),
				   CreateIISSiteResponse {
					   Status: BaseHandlerResponse.CompletionStatus.Success
				   } result => ExitWithOkMessage(
					   result.Description),
				   CreateIISSiteResponse { Status: BaseHandlerResponse.CompletionStatus.Failure } result =>
					   ExitWithErrorMessage(result.Description),
				   var _ => ExitWithErrorMessage("Unknown error occured")
			   };
	}

	private void CreatePgTemplate(DirectoryInfo unzippedDirectory, string tmpDbName, string sourceFileName) {
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
			_logger.WriteError(
				$"[Database restore failed] - Directory structure: {string.Join(", ", unzippedDirectory.GetDirectories().Select(d => d.Name))}");
			FileInfo[] files = unzippedDirectory.GetFiles("*.*", SearchOption.TopDirectoryOnly);
			_logger.WriteError(
				$"[Database restore failed] - Files in root: {string.Join(", ", files.Take(10).Select(f => f.Name))}");

			// Check if db directory exists and list its contents
			DirectoryInfo dbDir = unzippedDirectory.GetDirectories("db").FirstOrDefault();
			if (dbDir != null) {
				FileInfo[] dbFiles = dbDir.GetFiles("*.*", SearchOption.TopDirectoryOnly);
				_logger.WriteError(
					$"[Database restore failed] - Files in db/: {string.Join(", ", dbFiles.Take(10).Select(f => f.Name))}");
			}

			throw new FileNotFoundException("Backup file not found in the specified directory.");
		}

		_logger.WriteInfo($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");

		_k8.CopyBackupFileToPod(k8Commands.PodType.Postgres, src.FullName, src.Name);

		postgres.CreateDb(tmpDbName);
		_k8.RestorePgDatabase(src.Name, tmpDbName);
		postgres.SetDatabaseAsTemplate(tmpDbName);

		// Set metadata comment
		string metadata = $"sourceFile:{sourceFileName}|createdDate:{DateTime.UtcNow:o}|version:1.0";
		postgres.SetDatabaseComment(tmpDbName, metadata);
		_logger.WriteInfo($"[Template metadata] - {metadata}");

		_k8.DeleteBackupImage(k8Commands.PodType.Postgres, src.Name);
		_logger.WriteInfo($"[Completed Database restore] - {DateTime.Now:hh:mm:ss}");
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

	/// <summary>
	///     Determines the folder path based on whether deployment is IIS or DotNet.
	///     For IIS: uses _iisRootFolder + site name
	///     For DotNet: uses current directory + site name (or AppPath if provided)
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

		// DotNet deployment uses current directory or specified AppPath
		if (!string.IsNullOrEmpty(options.AppPath)) {
			return options.AppPath;
		}

		return Path.Combine(Directory.GetCurrentDirectory(), options.SiteName);
	}

	private int DoMsWork(DirectoryInfo unzippedDirectory, string siteName) {
		FileInfo src = unzippedDirectory.GetDirectories("db").FirstOrDefault()?.GetFiles("*.bak").FirstOrDefault();
		_logger.WriteInfo($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");

		if (src is not { Exists: true }) {
			throw new FileNotFoundException("Backup file not found in the specified directory.");
		}

		bool useFs = false;
		string dest = Path.Join("\\\\wsl.localhost", "rancher-desktop", "mnt", "clio-infrastructure", "mssql", "data",
			$"{siteName}.bak");
		if (src.Length < int.MaxValue) {
			_k8.CopyBackupFileToPod(k8Commands.PodType.Mssql, src.FullName, $"{siteName}.bak");
		}
		else {
			//This is a hack, we have to fix Cp class to allow large files
			useFs = true;
			_logger.WriteWarning($"Copying large file to local directory {dest}");
			_fileSystem.CopyFile(src.FullName, dest, true);
		}

		k8Commands.ConnectionStringParams csp = _k8.GetMssqlConnectionString();
		Mssql mssql = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		bool exists = mssql.CheckDbExists(siteName);
		if (!exists) {
			mssql.CreateDb(siteName, $"{siteName}.bak");
		}

		if (useFs) {
			_fileSystem.DeleteFile(dest);
		}
		else {
			_k8.DeleteBackupImage(k8Commands.PodType.Mssql, $"{siteName}.bak");
		}

		return 0;
	}

	private int ExecutePgRestoreCommand(string pgRestorePath, LocalDbServerConfiguration config, string backupPath,
		string dbName) {
		ProcessStartInfo processInfo = new() {
			FileName = pgRestorePath,
			Arguments
				= $"-h {config.Hostname} -p {config.Port} -U {config.Username} -d {dbName} -v \"{backupPath}\" --no-owner --no-privileges",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			Environment = {
				["PGPASSWORD"] = config.Password
			}
		};

		using Process process = Process.Start(processInfo);

		if (Program.IsDebugMode) {
			process.OutputDataReceived += (sender, e) => {
				if (!string.IsNullOrEmpty(e.Data)) {
					_logger.WriteDebug(e.Data);
				}
			};

			process.ErrorDataReceived += (sender, e) => {
				if (!string.IsNullOrEmpty(e.Data)) {
					_logger.WriteDebug(e.Data);
				}
			};
		}
		else {
			Task.Run(() => {
				while (!process.HasExited) {
					_logger.WriteInfo("Restore in progress...");
					Thread.Sleep(30000);
				}
			});
		}

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();

		return process.ExitCode;
	}

	private int ExitWithErrorMessage(string message) {
		_logger.WriteError(message);
		return 1;
	}

	private int ExitWithOkMessage(string message) {
		_logger.WriteInfo(message);
		return 0;
	}

	private Version GetLatestProductVersion(string latestBranchPath, Version latestVersion, string product,
		CreatioRuntimePlatform platform) {
		string dirPath = Path.Combine(latestBranchPath, latestVersion.ToString(),
			GetProductDirectoryName(product, platform));
		if (Directory.Exists(dirPath)) {
			return latestVersion;
		}

		Version previousVersion = new(latestVersion.Major, latestVersion.Minor, latestVersion.Build,
			latestVersion.Revision - 1);
		return GetLatestProductVersion(latestBranchPath, previousVersion, product, platform);
	}

	private string GetProductDirectoryName(string product, CreatioRuntimePlatform platform) {
		return $"{product}{platform.ToRuntimePlatformString()}_Softkey_ENU";
	}

	private string GetProductFileNameWithoutBuildNumber(string product, CreatioDBType creatioDBType,
		CreatioRuntimePlatform creatioRuntimePlatform) {
		return $"_{product}{creatioRuntimePlatform.ToRuntimePlatformString()}_Softkey_{creatioDBType}_ENU.zip";
	}

	private int HandleUnsupportedDbType(string dbType) {
		_logger.WriteError($"Database type '{dbType}' is not supported. Supported types: mssql, postgres");
		return 1;
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

	private int RestoreMssqlToLocalServer(LocalDbServerConfiguration config, string backupPath, string dbName,
		bool dropIfExists) {
		try {
			IMssql mssql = _dbClientFactory.CreateMssql(config.Hostname, config.Port, config.Username, config.Password, config.UseWindowsAuth);

			if (mssql.CheckDbExists(dbName)) {
				if (!dropIfExists) {
					_logger.WriteError(
						$"Database {dbName} already exists. Use --drop-if-exists flag to automatically drop it.");
					return 1;
				}

				_logger.WriteWarning($"Database {dbName} already exists");
				_logger.WriteWarning("Dropping existing database...");
				mssql.DropDb(dbName);
				_logger.WriteInfo($"Dropped existing database {dbName}");
			}

			string dataPath = mssql.GetDataPath();
			_logger.WriteInfo($"SQL Server data path: {dataPath}");

			string backupFileName = Path.GetFileName(backupPath);
			string destinationPath = Path.Combine(dataPath, backupFileName);

			_logger.WriteInfo("Copying backup file to SQL Server data directory...");
			_fileSystem.CopyFiles(new[] { backupPath }, dataPath, true);
			_logger.WriteInfo($"Copied backup file \r\n\tfrom: {backupPath} \r\n\tto  : {destinationPath}");

			_logger.WriteInfo("Starting database restore...");
			_logger.WriteInfo(
				"This may take several minutes depending on database size. SQL Server will report progress every 5%.");
			bool success = mssql.CreateDb(dbName, backupFileName);

			if (success) {
				_logger.WriteInfo($"Successfully restored database {dbName} from {backupPath}");
				return 0;
			}

			_logger.WriteError($"Failed to restore database {dbName}");
			return 1;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error restoring MSSQL database: {ex.Message}");
			return 1;
		}
	}

	private int RestorePostgresToLocalServer(LocalDbServerConfiguration config, string backupPath, string dbName,
		bool dropIfExists, string zipFilePath = null) {
		string pgRestorePath = _postgresToolsPathDetector.GetPgRestorePath(config.PgToolsPath);

		if (string.IsNullOrEmpty(pgRestorePath)) {
			_logger.WriteError("pg_restore not found. Please install PostgreSQL client tools.");
			_logger.WriteInfo("Download PostgreSQL from: https://www.postgresql.org/download/");
			return 1;
		}

		_logger.WriteInfo($"Using pg_restore from: {pgRestorePath}");

		try {
			Postgres postgres
				= _dbClientFactory.CreatePostgres(config.Hostname, config.Port, config.Username, config.Password);

			string sourceFileName = !string.IsNullOrEmpty(zipFilePath)
				? Directory.Exists(zipFilePath)
					? new DirectoryInfo(zipFilePath).Name
					: Path.GetFileNameWithoutExtension(zipFilePath)
				: Path.GetFileNameWithoutExtension(backupPath);

			// Try to find existing template by source file
			string templateName = postgres.FindTemplateBySourceFile(sourceFileName);

			if (string.IsNullOrEmpty(templateName)) {
				// No existing template found, create new one with GUID-based name
				templateName = $"template_{Guid.NewGuid():N}";

				_logger.WriteInfo($"Template for '{sourceFileName}' does not exist, creating '{templateName}'...");

				_logger.WriteInfo($"Creating template database {templateName}...");
				bool templateCreated = postgres.CreateDb(templateName);

				if (!templateCreated) {
					_logger.WriteError($"Failed to create template database {templateName}");
					return 1;
				}

				_logger.WriteInfo($"Template database {templateName} created successfully");
				_logger.WriteInfo($"Starting restore from {backupPath}...");
				if (Program.IsDebugMode) {
					_logger.WriteInfo(
						"This may take several minutes depending on database size. Detailed progress will be shown below:");
				}
				else {
					_logger.WriteInfo(
						"This may take several minutes depending on database size. Run with --debug flag to see detailed progress.");
				}

				int exitCode = ExecutePgRestoreCommand(pgRestorePath, config, backupPath, templateName);

				if (exitCode != 0) {
					_logger.WriteError($"pg_restore failed with exit code {exitCode}");
					return 1;
				}

				_logger.WriteInfo($"Setting database {templateName} as template...");
				bool setAsTemplate = postgres.SetDatabaseAsTemplate(templateName);

				if (!setAsTemplate) {
					_logger.WriteError($"Failed to set database {templateName} as template");
					return 1;
				}

				// Set metadata comment
				string metadata = $"sourceFile:{sourceFileName}|createdDate:{DateTime.UtcNow:o}|version:1.0";
				postgres.SetDatabaseComment(templateName, metadata);

				_logger.WriteInfo(
					$"Template database {templateName} created successfully with source reference: {sourceFileName}");
			}
			else {
				_logger.WriteInfo(
					$"Found existing template '{templateName}' for source '{sourceFileName}', skipping restore");
			}

			if (postgres.CheckDbExists(dbName)) {
				if (!dropIfExists) {
					_logger.WriteError(
						$"Database {dbName} already exists. Use --drop-if-exists flag to automatically drop it.");
					return 1;
				}

				_logger.WriteWarning($"Database {dbName} already exists");
				_logger.WriteWarning("Dropping existing database...");
				postgres.DropDb(dbName);
				_logger.WriteInfo($"Dropped existing database {dbName}");
			}

			_logger.WriteInfo($"Creating database {dbName} from template {templateName}...");
			bool dbCreated = postgres.CreateDbFromTemplate(templateName, dbName);

			if (!dbCreated) {
				_logger.WriteError($"Failed to create database {dbName} from template");
				return 1;
			}

			_logger.WriteInfo($"Successfully created database {dbName} from template {templateName}");
			return 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error restoring PostgreSQL database: {ex.Message}");
			return 1;
		}
	}

	private int RestoreToLocalDb(DirectoryInfo unzippedDirectory, string destDbName, string dbServerName,
		bool dropIfExists, string zipFilePath = null) {
		_logger.WriteInfo($"[Restoring database to local server] - Server: {dbServerName}, Database: {destDbName}");

		// Get local database configuration
		LocalDbServerConfiguration config = _settingsRepository.GetLocalDbServer(dbServerName);
		if (config == null) {
			List<string> availableServers = _settingsRepository.GetLocalDbServerNames().ToList();
			string availableList = availableServers.Count != 0
				? string.Join(", ", availableServers)
				: "(none configured)";
			_logger.WriteError(
				$"Database server configuration '{dbServerName}' not found in appsettings.json. Available configurations: {availableList}");
			return 1;
		}

		// Find backup file
		FileInfo[] backupFiles = unzippedDirectory.GetFiles("*.backup", SearchOption.AllDirectories)
												  .Concat(unzippedDirectory.GetFiles("*.bak",
													  SearchOption.AllDirectories))
												  .ToArray();

		if (backupFiles.Length == 0) {
			_logger.WriteError($"No database backup file found in {unzippedDirectory.FullName}");
			_logger.WriteInfo("Expected .backup (PostgreSQL) or .bak (MSSQL) file in db/ folder");
			return 1;
		}

		string backupFilePath = backupFiles[0].FullName;
		_logger.WriteInfo($"[Found backup file] - {backupFilePath}");

		// Test connection
		_logger.WriteInfo($"Testing connection to {config.DbType} server at {config.Hostname}:{config.Port}...");
		ConnectionTestResult testResult = _dbConnectionTester.TestConnection(config);

		if (!testResult.Success) {
			_logger.WriteError($"Connection test failed: {testResult.ErrorMessage}");
			if (!string.IsNullOrEmpty(testResult.DetailedError)) {
				_logger.WriteError($"Details: {testResult.DetailedError}");
			}

			if (!string.IsNullOrEmpty(testResult.Suggestion)) {
				_logger.WriteWarning($"Suggestion: {testResult.Suggestion}");
			}

			return 1;
		}

		_logger.WriteInfo("Connection test successful");

		// Detect backup type
		BackupFileType detectedType = _backupFileDetector.DetectBackupType(backupFilePath);
		string dbType = config.DbType?.ToLowerInvariant();

		if (detectedType == BackupFileType.Unknown) {
			_logger.WriteError($"Cannot determine backup file type from {backupFilePath}");
			return 1;
		}

		bool isCompatible = (detectedType == BackupFileType.PostgresBackup &&
							 (dbType == "postgres" || dbType == "postgresql")) ||
							(detectedType == BackupFileType.MssqlBackup && dbType == "mssql");

		if (!isCompatible) {
			_logger.WriteError($"Backup file type {detectedType} is not compatible with database type {config.DbType}");
			return 1;
		}

		_logger.WriteInfo($"Restoring {detectedType} backup to {config.DbType} server...");

		// Restore based on database type
		return dbType switch {
				   "mssql" => RestoreMssqlToLocalServer(config, backupFilePath, destDbName, dropIfExists),
				   "postgres" or "postgresql" => RestorePostgresToLocalServer(config, backupFilePath, destDbName,
					   dropIfExists, zipFilePath),
				   var _ => HandleUnsupportedDbType(config.DbType)
			   };
	}

	private IDeploymentStrategy SelectDeploymentStrategy(PfInstallerOptions options) {
		IDeploymentStrategy strategy = _deploymentStrategyFactory.SelectStrategy(
			options.DeploymentMethod ?? "auto",
			options.NoIIS
		);
		return strategy;
	}

	private string BuildMssqlConnectionString(LocalDbServerConfiguration dbConfig, string databaseName) {
		string dataSource = dbConfig.Hostname.Contains("\\") || dbConfig.Port == 0 
			? dbConfig.Hostname 
			: $"{dbConfig.Hostname},{dbConfig.Port}";

		if (dbConfig.UseWindowsAuth) {
			return $"Data Source={dataSource};Initial Catalog={databaseName};Integrated Security=true;MultipleActiveResultSets=true;Pooling=true;Max Pool Size=100";
		}
		return $"Data Source={dataSource};Initial Catalog={databaseName};User Id={dbConfig.Username}; Password={dbConfig.Password};MultipleActiveResultSets=true;Pooling=true;Max Pool Size=100";
	}

	private async Task<int> UpdateConnectionString(DirectoryInfo unzippedDirectory, PfInstallerOptions options,
		InstallerHelper.DatabaseType dbType) {
		_logger.WriteInfo("[CheckUpdate connection string] - Started");
		string dbConnectionString;
		string redisConnectionString;
		int redisDb;

		// Check if using local database server
		if (!string.IsNullOrEmpty(options.DbServerName)) {
			_logger.WriteInfo($"[Connection String Mode] - Local database server: {options.DbServerName}");

			// Get local database configuration
			LocalDbServerConfiguration dbConfig = _settingsRepository.GetLocalDbServer(options.DbServerName);
			if (dbConfig == null) {
				return ExitWithErrorMessage(
					$"Database server configuration '{options.DbServerName}' not found in appsettings.json");
			}

		// Build connection string based on database type
		dbConnectionString = dbConfig.DbType?.ToLowerInvariant() switch {
			"postgres" or "postgresql" => 
				$"Server={dbConfig.Hostname};Port={dbConfig.Port};Database={options.SiteName};User ID={dbConfig.Username};password={dbConfig.Password};Timeout=500; CommandTimeout=400;MaxPoolSize=1024;",
			"mssql" => BuildMssqlConnectionString(dbConfig, options.SiteName),
			var _ => throw new NotSupportedException($"Database type '{dbConfig.DbType}' is not supported")
		};

			// For local deployment, use localhost Redis or skip if not available
			(int dbNumber, string errorMessage) emptyDb = FindEmptyRedisDb();

			if (!string.IsNullOrEmpty(emptyDb.errorMessage)) {
				_logger.WriteError(emptyDb.errorMessage);
			}
			
			redisDb = options.RedisDb >= 0 ? options.RedisDb : emptyDb.dbNumber;
			redisConnectionString = $"host=localhost;db={redisDb};port=6379";
			_logger.WriteInfo($"[Redis Configuration] - Using local Redis: database {redisDb}");
		}
		else {
			_logger.WriteInfo("[Connection String Mode] - Kubernetes cluster");
			k8Commands.ConnectionStringParams csParam = dbType switch {
															InstallerHelper.DatabaseType.Postgres => _k8
																.GetPostgresConnectionString(),
															InstallerHelper.DatabaseType.MsSql => _k8
																.GetMssqlConnectionString(),
															var _ => throw new NotSupportedException(
																$"Database type '{dbType}' is not supported")
														};

			// Determine Redis database number
			if (options.RedisDb >= 0) {
				// User specified Redis database
				redisDb = options.RedisDb;
				_logger.WriteInfo($"[Redis Configuration] - Using user-specified database: {redisDb}");
			}
			else {
				// Auto-detect empty database
				(int dbNumber, string errorMessage) = FindEmptyRedisDb(BindingsModule.k8sDns, csParam.RedisPort);

				if (dbNumber == -1) {
					// Error finding empty database
					return ExitWithErrorMessage(errorMessage);
				}

				redisDb = dbNumber;
				_logger.WriteInfo($"[Redis Configuration] - Auto-detected empty database: {redisDb}");
			}

			// Build Kubernetes connection strings
			dbConnectionString = dbType switch {
									 InstallerHelper.DatabaseType.Postgres =>
										 $"Server={BindingsModule.k8sDns};Port={csParam.DbPort};Database={options.SiteName};User ID={csParam.DbUsername};password={csParam.DbPassword};Timeout=500; CommandTimeout=400;MaxPoolSize=1024;",
									 InstallerHelper.DatabaseType.MsSql =>
										 $"Data Source={BindingsModule.k8sDns},{csParam.DbPort};Initial Catalog={options.SiteName};User Id={csParam.DbUsername}; Password={csParam.DbPassword};MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100",
									 var _ => throw new NotSupportedException(
										 $"Database type '{dbType}' is not supported")
								 };
			redisConnectionString = $"host={BindingsModule.k8sDns};db={redisDb};port={csParam.RedisPort}";
		}

		// Determine the folder path based on deployment strategy
		string folderPath = DetermineFolderPath(options);
		_logger.WriteInfo($"[Connection string] - Target folder path: {folderPath}");

		ConfigureConnectionStringRequest request = new() {
			Arguments = new Dictionary<string, string> {
				{ "folderPath", folderPath },
				{ "dbString", dbConnectionString },
				{ "redis", redisConnectionString }, {
					"isNetFramework",
					(InstallerHelper.DetectFramework(unzippedDirectory) ==
					 InstallerHelper.FrameworkType.NetFramework).ToString()
				}
			}
		};

		return (await _mediator.Send(request)).Value switch {
				   HandlerError error => ExitWithErrorMessage(error.ErrorDescription),
				   ConfigureConnectionStringResponse {
					   Status: BaseHandlerResponse.CompletionStatus.Success
				   } result => ExitWithOkMessage(result.Description),
				   ConfigureConnectionStringResponse {
					   Status: BaseHandlerResponse.CompletionStatus.Failure
				   } result => ExitWithErrorMessage(result.Description),
				   var _ => ExitWithErrorMessage("Unknown error occured")
			   };
	}

	private bool WaitForServerReady(string environmentName) {
		const int initialDelaySeconds = 15; // Initial delay to allow server to start
		const int maxAttempts = 10; // Increased attempts for longer wait time
		const int delaySeconds = 3;

		_logger.WriteInfo($"Waiting {initialDelaySeconds} seconds for server to start...");
		Thread.Sleep(initialDelaySeconds * 1000);

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
				_logger.WriteInfo(
					$"Waiting for server to become ready... ({attempt}/{maxAttempts}). Next check in {delaySeconds} seconds.");
				Thread.Sleep(delaySeconds * 1000);
			}
		}

		_logger.WriteWarning(
			$"Server did not become ready after {initialDelaySeconds + maxAttempts * delaySeconds} seconds.");
		return false;
	}

	#endregion

	#region Methods: Protected

	internal string GetBuildFilePathFromOptions(string remoteArtifactServerPath, string product,
		CreatioDBType creatioDBType, CreatioRuntimePlatform platform) {
		Version latestBranchVersion = GetLatestVersion(remoteArtifactServerPath);
		string latestBranchesBuildPath = Path.Combine(remoteArtifactServerPath, latestBranchVersion.ToString());
		IDirectoryInfo latestBranchesDireInfo = _fileSystem.GetDirectoryInfo(latestBranchesBuildPath);
		IOrderedEnumerable<IDirectoryInfo> latestBranchSubdirectories = latestBranchesDireInfo.GetDirectories()
			.OrderByDescending(dir => dir.CreationTimeUtc);
		List<IDirectoryInfo> revisionDirectories = new();
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

	internal Version GetLatestVersion(string remoteArtifactServerPath) {
		string[] branches = _fileSystem.GetDirectories(remoteArtifactServerPath);
		List<Version> version = new();
		foreach (string branch in branches) {
			string branchName = branch.Split(Path.DirectorySeparatorChar).Last();
			if (Version.TryParse(branchName, out Version ver)) {
				version.Add(ver);
			}
		}

		return version.Max();
	}

	#endregion

	#region Methods: Public

	public async Task<int> CreateDeployDirectory(PfInstallerOptions options, string deploymentFolder) {
		DirectoryInfo unzippedDirectory = InstallerHelper.UnzipOrTakeExistingOld(options.ZipFile, _packageArchiver);
		if (!_fileSystem.ExistsDirectory(deploymentFolder)) {
			_logger.WriteInfo($"[Creating deployment folder] - {deploymentFolder}");
			_fileSystem.CreateDirectory(deploymentFolder);
		}

		string str = $"""
					  [Copy deployment files]
					      From: {unzippedDirectory.FullName} 
					      To:   {deploymentFolder}
					  """;
		_logger.WriteInfo(str);
		_fileSystem.CopyDirectoryWithFilter(unzippedDirectory.FullName, deploymentFolder, true, source => {
			string[] excludedExtensions = [".bak", ".backup"];
			string[] excludedDirectories = ["db"];

			if (Directory.Exists(source)) {
				return excludedDirectories.Contains(new DirectoryInfo(source).Name.ToLower());
			}

			if (!File.Exists(source)) {
				return excludedExtensions.Contains(Path.GetExtension(source)?.ToLower());
			}

			return true;
		});
		return 0;
	}

	public int DoPgWork(DirectoryInfo unzippedDirectory, string destDbName, string templateName = "") {
		// Use templateName for metadata if provided, otherwise use directory name
		string actualSourceName = string.IsNullOrWhiteSpace(templateName)
			? unzippedDirectory.Name
			: templateName;

		k8Commands.ConnectionStringParams csp = _k8.GetPostgresConnectionString();
		Postgres postgres = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		// Try to find existing template by source file
		string existingTemplate = postgres.FindTemplateBySourceFile(actualSourceName);

		string tmpDbName;
		if (!string.IsNullOrEmpty(existingTemplate)) {
			_logger.WriteInfo(
				$"[Database restore] - Found existing template '{existingTemplate}' for source '{actualSourceName}'");
			tmpDbName = existingTemplate;
		}
		else {
			// Generate new GUID-based name
			tmpDbName = $"template_{Guid.NewGuid():N}";
			CreatePgTemplate(unzippedDirectory, tmpDbName, actualSourceName);
		}

		postgres.CreateDbFromTemplate(tmpDbName, destDbName);
		_logger.WriteInfo($"[Database created] - {destDbName}");
		return 0;
	}

	public override int Execute(PfInstallerOptions options) {
		if (string.IsNullOrEmpty(options.ZipFile) && !string.IsNullOrEmpty(options.Product)) {
			options.ZipFile = GetBuildFilePathFromOptions(options.Product, options.DBType, options.RuntimePlatform);
		}

		if (!File.Exists(options.ZipFile) && !Directory.Exists(options.ZipFile)) {
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
				}
				else {
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
			}
			else {
				bool portSelected = false;

				while (!portSelected) {
					Console.WriteLine("Press Enter to use default port 8080, or enter a custom port number:");
					string portInput = (Console.ReadLine() ?? string.Empty).Trim();

					int selectedPort = 8080; // Default

					if (string.IsNullOrEmpty(portInput)) {
						selectedPort = 8080;
					}
					else if (int.TryParse(portInput, out int customPort)) {
						if (customPort > 0 && customPort <= 65535) {
							selectedPort = customPort;
						}
						else {
							Console.WriteLine(
								"Invalid port number. Port must be between 1 and 65535. Please try again.");
							continue;
						}
					}
					else {
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
						if (choice == "1") { }

						continue; // Also loop back
					}

					options.SitePort = selectedPort;
					portSelected = true;
				}
			}

			// Ensure we have a valid port
			if (options.SitePort is <= 0 or > 65535) {
				options.SitePort = 8080;
			}
		}

		// STEP 3: Now output all logging information after user has provided input
		_logger.WriteLine(); // Blank line for readability
		_logger.WriteInfo(
			$"[OS Platform] - {(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "Windows")}");
		_logger.WriteInfo($"[Is IIS Deployment] - {isIisDeployment}");
		_logger.WriteInfo($"[Site Name] - {options.SiteName}");
		_logger.WriteInfo($"[Site Port] - {options.SitePort}");

		options.ZipFile = CopyLocalWhenNetworkDrive(options.ZipFile);
		string deploymentFolder = DetermineFolderPath(options);

		DirectoryInfo unzippedDirectory = InstallerHelper.UnzipOrTakeExistingOld(options.ZipFile, _packageArchiver);
		if (!_fileSystem.ExistsDirectory(deploymentFolder)) {
			_logger.WriteInfo($"[Creating deployment folder] - {deploymentFolder}");
			_fileSystem.CreateDirectory(deploymentFolder);
		}

		string str = $"""
					  [Copy deployment files]
					      From: {unzippedDirectory.FullName} 
					      To:   {deploymentFolder}
					  """;
		_logger.WriteInfo(str);
		_fileSystem.CopyDirectoryWithFilter(unzippedDirectory.FullName, deploymentFolder, true, source => {
			if (Directory.Exists(source)) {
				return _excludedDirectories.Contains(new DirectoryInfo(source).Name.ToLower());
			}

			if (File.Exists(source)) {
				return _excludedExtensions.Contains(Path.GetExtension(source)?.ToLower());
			}

			return true;
		});


		InstallerHelper.DatabaseType dbType;
		try {
			dbType = InstallerHelper.DetectDataBase(unzippedDirectory);
		}
		catch (Exception ex) {
			_logger.WriteWarning($"[DetectDataBase] - Could not detect database type: {ex.Message}");
			_logger.WriteInfo("[DetectDataBase] - Defaulting to Postgres");
			dbType = InstallerHelper.DatabaseType.Postgres;
		}

		int dbRestoreResult;

		// Check if user specified a local database server
		if (!string.IsNullOrEmpty(options.DbServerName)) {
			_logger.WriteInfo($"[Database Restore Mode] - Local server: {options.DbServerName}");
			dbRestoreResult = RestoreToLocalDb(unzippedDirectory, options.SiteName, options.DbServerName,
				options.DropIfExists, options.ZipFile);
		}
		else {
			_logger.WriteInfo("[Database Restore Mode] - Kubernetes cluster");
			dbRestoreResult = dbType switch {
								  InstallerHelper.DatabaseType.MsSql => DoMsWork(unzippedDirectory, options.SiteName),
								  var _ => DoPgWork(unzippedDirectory, options.SiteName,
									  Path.GetFileNameWithoutExtension(options.ZipFile))
							  };
		}

		DirectoryInfo deploymentFolderInfo = new(deploymentFolder);
		int deploySiteResult = dbRestoreResult switch {
								   0 => DeployApplication(deploymentFolderInfo, options),
								   var _ => ExitWithErrorMessage("Database restore failed")
							   };


		int updateConnectionStringResult = deploySiteResult switch {
											   0 => UpdateConnectionString(deploymentFolderInfo, options, dbType)
													.GetAwaiter().GetResult(),
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
			StartWebBrowser(options, isIisDeployment);
		}

		return 0;
	}

	public string GetBuildFilePathFromOptions(string product, CreatioDBType dBType,
		CreatioRuntimePlatform runtimePlatform) {
		return GetBuildFilePathFromOptions(RemoteArtefactServerPath, product, dBType, runtimePlatform);
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
				Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
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
		catch (Exception ex) {
			_logger.WriteError($"Failed to launch web browser: {ex.Message}");
			return 1;
		}

		return 0;
	}

	#endregion
}
