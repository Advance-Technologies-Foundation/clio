using System;
using System.Collections.Generic;
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
using Clio.Common.Database;
using Clio.Common.db;
using Clio.Common.DeploymentStrategies;
using Clio.Common.K8;
using Clio.Common.ScenarioHandlers;
using Clio.UserEnvironment;
using MediatR;
using IFileSystem = Clio.Common.IFileSystem;
using MsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.CreatioInstallCommand;

/// <summary>
///     Defines operations for deploying a Creatio application and restoring its database.
/// </summary>
public interface ICreatioInstallerService{
	#region Methods: Public

	/// <summary>
	///     Restores a PostgreSQL database from unpacked deployment files.
	/// </summary>
	/// <param name="unzippedDirectoryPath">Directory path containing extracted deployment artifacts.</param>
	/// <param name="destDbName">Target the database name to create or restore.</param>
	/// <param name="templateName">Optional template source name used for template resolution.</param>
	/// <returns><c>0</c> on success; non-zero otherwise.</returns>
	int DoPgWork(string unzippedDirectoryPath, string destDbName, string templateName = "");

	/// <summary>
	///     Executes the full deployment flow for the provided installer options.
	/// </summary>
	/// <param name="options">Parsed deploy-creatio command options.</param>
	/// <returns><c>0</c> on success; non-zero otherwise.</returns>
	int Execute(PfInstallerOptions options);

	/// <summary>
	///     Resolves a build artifact path based on product, database type, and runtime platform.
	/// </summary>
	/// <param name="product">Product name or product key.</param>
	/// <param name="dBType">Database type used to select the build artifact.</param>
	/// <param name="runtimePlatform">Runtime platform used to select the build artifact.</param>
	/// <returns>Full path to the selected build artifact.</returns>
	string GetBuildFilePathFromOptions(string product, CreatioDBType dBType,
		CreatioRuntimePlatform runtimePlatform);

	/// <summary>
	///     Opens the deployed application URL in the default browser using non-IIS URL format.
	/// </summary>
	/// <param name="options">Deployment options containing site port configuration.</param>
	/// <returns><c>0</c> on success; non-zero otherwise.</returns>
	int StartWebBrowser(PfInstallerOptions options);

	/// <summary>
	///     Opens the deployed application URL in the default browser.
	/// </summary>
	/// <param name="options">Deployment options containing site port configuration.</param>
	/// <param name="isIisDeployment">Whether an IIS URL format should be used.</param>
	/// <returns><c>0</c> on success; non-zero otherwise.</returns>
	int StartWebBrowser(PfInstallerOptions options, bool isIisDeployment);

	#endregion
}

/// <summary>
///     Default implementation of <see cref="ICreatioInstallerService" /> for deploy-creatio command execution.
/// </summary>
public class CreatioInstallerService : Command<PfInstallerOptions>, ICreatioInstallerService{
	#region Fields: Private

	private readonly IBackupFileDetector _backupFileDetector;
	private readonly IDbClientFactory _dbClientFactory;
	private readonly IDbConnectionTester _dbConnectionTester;
	private readonly DeploymentStrategyFactory _deploymentStrategyFactory;
	private readonly string[] _excludedDirectories = ["db"];
	private readonly string[] _excludedExtensions = [".bak", ".backup"];
	private readonly IFileSystem _fileSystem;
	private readonly HealthCheckCommand _healthCheckCommand;

	private readonly string _iisRootFolder;
	private readonly k8Commands _k8;
	private readonly ILogger _logger;
	private readonly IMediator _mediator;
	private readonly MsFileSystem _msFileSystem;
	private readonly IPackageArchiver _packageArchiver;
	private readonly IPostgresToolsPathDetector _postgresToolsPathDetector;
	private readonly IProcessExecutor _processExecutor;
	private readonly IRedisDatabaseSelector _redisDatabaseSelector;

	private readonly string _productFolder;
	private readonly RegAppCommand _registerCommand;
	private readonly string _remoteArtefactServerPath;
	private readonly ISettingsRepository _settingsRepository;

	#endregion

	#region Constructors: Public

	/// <summary>
	///     Initializes a new instance of the <see cref="CreatioInstallerService" /> class.
	/// </summary>
	/// <param name="packageArchiver">Archive service used to unpack deployment files.</param>
	/// <param name="k8">Kubernetes command facade.</param>
	/// <param name="mediator">Mediator for scenario handler requests.</param>
	/// <param name="registerCommand">Command used to register the deployed application environment.</param>
	/// <param name="settingsRepository">Settings repository for deployment paths and defaults.</param>
	/// <param name="fileSystem">Filesystem abstraction.</param>
	/// <param name="msFileSystem">System.IO.Abstractions filesystem facade.</param>
	/// <param name="logger">Logger for user-facing deployment output.</param>
	/// <param name="deploymentStrategyFactory">Factory for platform-specific deployment strategy selection.</param>
	/// <param name="healthCheckCommand">Health check command used for readiness verification.</param>
	/// <param name="dbClientFactory">Factory for database client instances.</param>
	/// <param name="dbConnectionTester">Service for validating DB connectivity before restore.</param>
	/// <param name="backupFileDetector">Service for detecting a backup file type.</param>
	/// <param name="postgresToolsPathDetector">Detector for PostgreSQL tools installation paths.</param>
	/// <param name="processExecutor">Process execution service used for external tool invocation.</param>
	public CreatioInstallerService(IPackageArchiver packageArchiver, k8Commands k8,
		IMediator mediator, RegAppCommand registerCommand, ISettingsRepository settingsRepository,
		IFileSystem fileSystem, MsFileSystem msFileSystem, ILogger logger,
		DeploymentStrategyFactory deploymentStrategyFactory,
		HealthCheckCommand healthCheckCommand, IDbClientFactory dbClientFactory,
		IDbConnectionTester dbConnectionTester, IBackupFileDetector backupFileDetector,
		IPostgresToolsPathDetector postgresToolsPathDetector, IProcessExecutor processExecutor,
		IRedisDatabaseSelector redisDatabaseSelector) {
		_packageArchiver = packageArchiver;
		_k8 = k8;
		_mediator = mediator;
		_registerCommand = registerCommand;
		_fileSystem = fileSystem;
		_msFileSystem = msFileSystem;
		_iisRootFolder = settingsRepository.GetIISClioRootPath();
		_productFolder = settingsRepository.GetCreatioProductsFolder();
		_remoteArtefactServerPath = settingsRepository.GetRemoteArtefactServerPath();
		_logger = logger;
		_deploymentStrategyFactory = deploymentStrategyFactory;
		_healthCheckCommand = healthCheckCommand;
		_settingsRepository = settingsRepository;
		_dbClientFactory = dbClientFactory;
		_dbConnectionTester = dbConnectionTester;
		_backupFileDetector = backupFileDetector;
		_postgresToolsPathDetector = postgresToolsPathDetector;
		_processExecutor = processExecutor;
		_redisDatabaseSelector = redisDatabaseSelector;
	}

	/// <summary>
	///     Initializes a new instance of the <see cref="CreatioInstallerService" /> class.
	/// </summary>
	/// <remarks>
	///     Parameterless constructor exists for tooling and test scenarios that require delayed initialization.
	/// </remarks>
	public CreatioInstallerService() { }

	#endregion

	#region Methods: Private

	private string BuildMssqlConnectionString(LocalDbServerConfiguration dbConfig, string databaseName) {
		string dataSource = dbConfig.Hostname.Contains("\\") || dbConfig.Port == 0
			? dbConfig.Hostname
			: $"{dbConfig.Hostname},{dbConfig.Port}";

		if (dbConfig.UseWindowsAuth) {
			return
				$"Data Source={dataSource};Initial Catalog={databaseName};Integrated Security=true;MultipleActiveResultSets=true;Pooling=true;Max Pool Size=100";
		}

		return
			$"Data Source={dataSource};Initial Catalog={databaseName};User Id={dbConfig.Username}; Password={dbConfig.Password};MultipleActiveResultSets=true;Pooling=true;Max Pool Size=100";
	}

	private void CopyFileWithProgress(string sourcePath, string destinationPath, IProgress<double> progress) {
		const int bufferSize = 1024 * 1024; // 1MB
		byte[] buffer = new byte[bufferSize];
		int bytesRead;

		using FileSystemStream sourceStream = _fileSystem.FileOpenStream(sourcePath, FileMode.Open, FileAccess.Read,
			FileShare.Read);
		long totalBytes = sourceStream.Length;

		using FileSystemStream destinationStream = _fileSystem.FileOpenStream(destinationPath, FileMode.OpenOrCreate,
			FileAccess.Write, FileShare.None);
		while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0) {
			destinationStream.Write(buffer, 0, bytesRead);
			double percentage = 100d * sourceStream.Position / totalBytes;
			progress.Report(percentage);
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
			path = _msFileSystem.Path.GetFullPath(path);
		}

		return _msFileSystem.DriveInfo.New(_msFileSystem.Path.GetPathRoot(path)) switch {
				   { DriveType: DriveType.Network } => CopyZipLocal(path),
				   var _ => path
			   };
	}

	private string CopyZipLocal(string src) {
		if (!_msFileSystem.Directory.Exists(_productFolder)) {
			_msFileSystem.Directory.CreateDirectory(_productFolder);
		}

		IFileInfo srcInfo = _msFileSystem.FileInfo.New(src);
		string dest = _msFileSystem.Path.Join(_productFolder, srcInfo.Name);

		if (_msFileSystem.File.Exists(dest)) {
			return dest;
		}

		_logger.WriteLine($"Detected network drive as source, copying to local folder {_productFolder}");
		_logger.Write("Copy Progress:    ");
		Progress<double> progressReporter = new(progress => {
			string result = progress switch {
								< 10 => progress.ToString("0").PadLeft(2) + " %",
								< 100 => progress.ToString("0").PadLeft(1) + " %",
								100 => "100 %",
								var _ => ""
							};
			Console.CursorLeft = 15;
			_logger.Write(result);
		});
		CopyFileWithProgress(src, dest, progressReporter);
		return dest;
	}

	// private async Task<int> CreateIISSite(string unzippedDirectoryPath, PfInstallerOptions options) {
	// 	_logger.WriteInfo("[Create IIS Site] - Started");
	// 	CreateIISSiteRequest request = new() {
	// 		Arguments = new Dictionary<string, string> {
	// 			{ "siteName", options.SiteName },
	// 			{ "port", options.SitePort.ToString() },
	// 			{ "sourceDirectory", unzippedDirectoryPath },
	// 			{ "destinationDirectory", _iisRootFolder }, {
	// 				"isNetFramework",
	// 				(InstallerHelper.DetectFrameworkByPath(unzippedDirectoryPath) ==
	// 				 InstallerHelper.FrameworkType.NetFramework)
	// 				.ToString()
	// 			}
	// 		}
	// 	};
	// 	return (await _mediator.Send(request)).Value switch {
	// 			   HandlerError error => ExitWithErrorMessage(error.ErrorDescription),
	// 			   CreateIISSiteResponse {
	// 				   Status: BaseHandlerResponse.CompletionStatus.Success
	// 			   } result => ExitWithOkMessage(
	// 				   result.Description),
	// 			   CreateIISSiteResponse { Status: BaseHandlerResponse.CompletionStatus.Failure } result =>
	// 				   ExitWithErrorMessage(result.Description),
	// 			   var _ => ExitWithErrorMessage("Unknown error occured")
	// 		   };
	// }

	private void CreatePgTemplate(string unzippedDirectoryPath, string tmpDbName, string sourceFileName) {
		k8Commands.ConnectionStringParams csp = _k8.GetPostgresConnectionString();
		Postgres postgres = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		bool exists = postgres.CheckTemplateExists(tmpDbName);
		if (exists) {
			_logger.WriteInfo($"[Database restore] - Template '{tmpDbName}' already exists, skipping restore");
			return;
		}

		// Search for a backup file in the db directory or root
		string dbDirectoryPath = _fileSystem.GetDirectories(unzippedDirectoryPath, "db", SearchOption.TopDirectoryOnly)
											.FirstOrDefault();
		string src = !string.IsNullOrEmpty(dbDirectoryPath)
			? _fileSystem.GetFiles(dbDirectoryPath, "*.backup", SearchOption.TopDirectoryOnly).FirstOrDefault()
			: null;
		if (string.IsNullOrEmpty(src)) {
			src = _fileSystem.GetFiles(unzippedDirectoryPath, "*.backup", SearchOption.TopDirectoryOnly)
							 .FirstOrDefault();
		}

		// Log detailed information if a backup file not found
		if (string.IsNullOrEmpty(src)) {
			_logger.WriteError($"[Database restore failed] - Backup file not found in {unzippedDirectoryPath}");
			_logger.WriteError(
				$"[Database restore failed] - Directory structure: {string.Join(", ", _fileSystem.GetDirectories(unzippedDirectoryPath).Select(d => _msFileSystem.Path.GetFileName(d)))}");
			string[] files = _fileSystem.GetFiles(unzippedDirectoryPath, "*.*", SearchOption.TopDirectoryOnly);
			_logger.WriteError(
				$"[Database restore failed] - Files in root: {string.Join(", ", files.Take(10).Select(_msFileSystem.Path.GetFileName))}");

			// Check if the db directory exists and list its contents
			if (!string.IsNullOrEmpty(dbDirectoryPath)) {
				string[] dbFiles = _fileSystem.GetFiles(dbDirectoryPath, "*.*", SearchOption.TopDirectoryOnly);
				_logger.WriteError(
					$"[Database restore failed] - Files in db/: {string.Join(", ", dbFiles.Take(10).Select(_msFileSystem.Path.GetFileName))}");
			}

			throw new FileNotFoundException("Backup file not found in the specified directory.");
		}

		_logger.WriteInfo($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");

		string srcFileName = _msFileSystem.Path.GetFileName(src);
		_k8.CopyBackupFileToPod(k8Commands.PodType.Postgres, src, srcFileName);

		postgres.CreateDb(tmpDbName);
		_k8.RestorePgDatabase(srcFileName, tmpDbName);
		postgres.SetDatabaseAsTemplate(tmpDbName);

		// Set metadata comment
		string metadata = $"sourceFile:{sourceFileName}|createdDate:{DateTime.UtcNow:o}|version:1.0";
		postgres.SetDatabaseComment(tmpDbName, metadata);
		_logger.WriteInfo($"[Template metadata] - {metadata}");

		_k8.DeleteBackupImage(k8Commands.PodType.Postgres, srcFileName);
		_logger.WriteInfo($"[Completed Database restore] - {DateTime.Now:hh:mm:ss}");
	}


	private int DeployApplication(string unzippedDirectoryPath, PfInstallerOptions options) {
		try {
			IDeploymentStrategy strategy = SelectDeploymentStrategy(options);
			int result = strategy.Deploy(unzippedDirectoryPath, options).GetAwaiter().GetResult();

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
	///     For DotNet: uses the current directory and site name (or AppPath if provided)
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

			return _msFileSystem.Path.Combine(_iisRootFolder, options.SiteName);
		}

		// DotNet deployment uses the current directory or specified AppPath
		if (!string.IsNullOrEmpty(options.AppPath)) {
			return options.AppPath;
		}

		return _msFileSystem.Path.Combine(_msFileSystem.Directory.GetCurrentDirectory(), options.SiteName);
	}

	private int DoMsWork(string unzippedDirectoryPath, string siteName) {
		string dbDirectoryPath = _fileSystem.GetDirectories(unzippedDirectoryPath, "db", SearchOption.TopDirectoryOnly)
											.FirstOrDefault();
		string src = !string.IsNullOrEmpty(dbDirectoryPath)
			? _fileSystem.GetFiles(dbDirectoryPath, "*.bak", SearchOption.TopDirectoryOnly).FirstOrDefault()
			: null;
		_logger.WriteInfo($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");

		if (string.IsNullOrEmpty(src) || !_msFileSystem.File.Exists(src)) {
			throw new FileNotFoundException("Backup file not found in the specified directory.");
		}

		bool useFs = false;
		string dest = _msFileSystem.Path.Join("\\\\wsl.localhost", "rancher-desktop", "mnt", "clio-infrastructure",
			"mssql", "data",
			$"{siteName}.bak");
		if (_msFileSystem.FileInfo.New(src).Length < int.MaxValue) {
			_k8.CopyBackupFileToPod(k8Commands.PodType.Mssql, src, $"{siteName}.bak");
		}
		else {
			//This is a hack, we have to fix Cp class to allow large files
			useFs = true;
			_logger.WriteWarning($"Copying large file to local directory {dest}");
			_fileSystem.CopyFile(src, dest, true);
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
		ProcessExecutionOptions executionOptions = new(pgRestorePath,
			$"-h {config.Hostname} -p {config.Port} -U {config.Username} -d {dbName} -v \"{backupPath}\" --no-owner --no-privileges") {
			WorkingDirectory = _msFileSystem.Path.GetDirectoryName(pgRestorePath),
			EnvironmentVariables = new Dictionary<string, string> {
				["PGPASSWORD"] = config.Password
			},
			OnOutput = Program.IsDebugMode
				? (line, _) => _logger.WriteDebug(line)
				: null
		};

		CancellationTokenSource progressCancellationTokenSource = new();
		Task progressTask = Task.CompletedTask;
		if (!Program.IsDebugMode) {
			progressTask = Task.Run(async () => {
				while (!progressCancellationTokenSource.Token.IsCancellationRequested) {
					_logger.WriteInfo("Restore in progress...");
					try {
						await Task.Delay(30000, progressCancellationTokenSource.Token);
					}
					catch (OperationCanceledException) {
						break;
					}
				}
			});
		}

		ProcessExecutionResult executionResult;
		try {
			executionResult = _processExecutor.ExecuteWithRealtimeOutputAsync(executionOptions).GetAwaiter().GetResult();
		}
		finally {
			progressCancellationTokenSource.Cancel();
			progressTask.GetAwaiter().GetResult();
		}

		if (!executionResult.Started) {
			_logger.WriteError($"pg_restore failed to start: {executionResult.StandardError}");
			return 1;
		}

		return executionResult.ExitCode ?? 1;
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
		string dirPath = _msFileSystem.Path.Combine(latestBranchPath, latestVersion.ToString(),
			GetProductDirectoryName(product, platform));
		if (_msFileSystem.Directory.Exists(dirPath)) {
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
			IMssql mssql = _dbClientFactory.CreateMssql(config.Hostname, config.Port, config.Username, config.Password,
				config.UseWindowsAuth);

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

			string backupFileName = _msFileSystem.Path.GetFileName(backupPath);
			string destinationPath = _msFileSystem.Path.Combine(dataPath, backupFileName);

			_logger.WriteInfo("Copying backup file to SQL Server data directory...");
			_fileSystem.CopyFiles([backupPath], dataPath, true);
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
				? _msFileSystem.Directory.Exists(zipFilePath)
					? _msFileSystem.Path.GetFileName(zipFilePath)
					: _msFileSystem.Path.GetFileNameWithoutExtension(zipFilePath)
				: _msFileSystem.Path.GetFileNameWithoutExtension(backupPath);

			// Try to find the existing template by source file
			string templateName = postgres.FindTemplateBySourceFile(sourceFileName);

			if (string.IsNullOrEmpty(templateName)) {
				// No existing template found, create a new one with a GUID-based name
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
				_logger.WriteInfo(
					Program.IsDebugMode
						? "This may take several minutes depending on database size. Detailed progress will be shown below:"
						: "This may take several minutes depending on database size. Run with --debug flag to see detailed progress.");

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

	private int RestoreToLocalDb(string unzippedDirectoryPath, string destDbName, string dbServerName,
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

		// Find a backup file
		string[] backupFiles = _fileSystem.GetFiles(unzippedDirectoryPath, "*.backup", SearchOption.AllDirectories)
										  .Concat(_fileSystem.GetFiles(unzippedDirectoryPath, "*.bak",
											  SearchOption.AllDirectories))
										  .ToArray();

		if (backupFiles.Length == 0) {
			_logger.WriteError($"No database backup file found in {unzippedDirectoryPath}");
			_logger.WriteInfo("Expected .backup (PostgreSQL) or .bak (MSSQL) file in db/ folder");
			return 1;
		}

		string backupFilePath = backupFiles[0];
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

		// Restore based on a database type
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

	private async Task<int> UpdateConnectionString(string unzippedDirectoryPath, PfInstallerOptions options,
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

			// Build connection string based on a database type
			dbConnectionString = dbConfig.DbType?.ToLowerInvariant() switch {
									 "postgres" or "postgresql" =>
										 $"Server={dbConfig.Hostname};Port={dbConfig.Port};Database={options.SiteName};User ID={dbConfig.Username};password={dbConfig.Password};Timeout=500; CommandTimeout=400;MaxPoolSize=1024;",
									 "mssql" => BuildMssqlConnectionString(dbConfig, options.SiteName),
									 var _ => throw new NotSupportedException(
										 $"Database type '{dbConfig.DbType}' is not supported")
								 };

			// For local deployment, use localhost Redis or skip if not available
			RedisDatabaseSelectionResult emptyDb = _redisDatabaseSelector.FindEmptyLocalDatabase();

			if (!emptyDb.Success && !string.IsNullOrEmpty(emptyDb.ErrorMessage)) {
				_logger.WriteError(emptyDb.ErrorMessage);
			}

			redisDb = options.RedisDb >= 0 ? options.RedisDb : emptyDb.DatabaseNumber;
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
				// User specified a Redis database
				redisDb = options.RedisDb;
				_logger.WriteInfo($"[Redis Configuration] - Using user-specified database: {redisDb}");
			}
			else {
				// Auto-detect empty database
				RedisDatabaseSelectionResult selection = _redisDatabaseSelector.FindEmptyDatabase(BindingsModule.k8sDns,
					csParam.RedisPort);

				if (!selection.Success) {
					// Error finding an empty database
					return ExitWithErrorMessage(selection.ErrorMessage);
				}

				redisDb = selection.DatabaseNumber;
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
					(InstallerHelper.DetectFrameworkByPath(unzippedDirectoryPath) ==
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
		CreatioDBType creatioDbType, CreatioRuntimePlatform platform) {
		Version latestBranchVersion = GetLatestVersion(remoteArtifactServerPath);
		string latestBranchesBuildPath
			= _msFileSystem.Path.Combine(remoteArtifactServerPath, latestBranchVersion.ToString());
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

		string productZipFileName = GetProductFileNameWithoutBuildNumber(product, creatioDbType, platform);
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
			string branchName = branch.Split(_msFileSystem.Path.DirectorySeparatorChar).Last();
			if (Version.TryParse(branchName, out Version ver)) {
				version.Add(ver);
			}
		}

		return version.Max();
	}

	#endregion

	#region Methods: Public

	/// <summary>
	///     Creates a deployment directory and copies deployment files excluding backup/database artifacts.
	/// </summary>
	/// <param name="options">Deployment options containing source zip or extracted path.</param>
	/// <param name="deploymentFolder">Target the directory where deployment files should be copied.</param>
	/// <returns>A task that resolves to <c>0</c> when directory preparation succeeds.</returns>
	// 	public async Task<int> CreateDeployDirectory(PfInstallerOptions options, string deploymentFolder) {
	// 		string unzippedDirectoryPath = InstallerHelper.UnzipOrTakeExistingOldPath(options.ZipFile, _packageArchiver);
	// 		if (!_fileSystem.ExistsDirectory(deploymentFolder)) {
	// 			_logger.WriteInfo($"[Creating deployment folder] - {deploymentFolder}");
	// 			_fileSystem.CreateDirectory(deploymentFolder);
	// 		}
	//
	// 		string str = $"""
	// 					  [Copy deployment files]
	// 					      From: {unzippedDirectoryPath} 
	// 					      To:   {deploymentFolder}
	// 					  """;
	// 		_logger.WriteInfo(str);
	// 		_fileSystem.CopyDirectoryWithFilter(unzippedDirectoryPath, deploymentFolder, true, source => {
	// 			string[] excludedExtensions = [".bak", ".backup"];
	// 			string[] excludedDirectories = ["db"];
	//
	// 			if (_msFileSystem.Directory.Exists(source)) {
	// 				return excludedDirectories.Contains(_msFileSystem.Path.GetFileName(source)?.ToLower());
	// 			}
	//
	// 			if (!_msFileSystem.File.Exists(source)) {
	// 				return excludedExtensions.Contains(_msFileSystem.Path.GetExtension(source)?.ToLower());
	// 			}
	//
	// 			return true;
	// 		});
	// 		return 0;
	// 	}

	/// <inheritdoc />
	public int DoPgWork(string unzippedDirectoryPath, string destDbName, string templateName = "") {
		// Use templateName for metadata if provided, otherwise use directory name
		string actualSourceName = string.IsNullOrWhiteSpace(templateName)
			? _msFileSystem.Path.GetFileName(unzippedDirectoryPath)
			: templateName;

		k8Commands.ConnectionStringParams csp = _k8.GetPostgresConnectionString();
		Postgres postgres = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		// Try to find the existing template by source file
		string existingTemplate = postgres.FindTemplateBySourceFile(actualSourceName);

		string tmpDbName;
		if (!string.IsNullOrEmpty(existingTemplate)) {
			_logger.WriteInfo(
				$"[Database restore] - Found existing template '{existingTemplate}' for source '{actualSourceName}'");
			tmpDbName = existingTemplate;
		}
		else {
			// Generate a new GUID-based name
			tmpDbName = $"template_{Guid.NewGuid():N}";
			CreatePgTemplate(unzippedDirectoryPath, tmpDbName, actualSourceName);
		}

		postgres.CreateDbFromTemplate(tmpDbName, destDbName);
		_logger.WriteInfo($"[Database created] - {destDbName}");
		return 0;
	}

	/// <summary>
	///     Executes the deploy-creatio command workflow.
	/// </summary>
	/// <param name="options">Parsed deploy-creatio command options.</param>
	/// <returns><c>0</c> on success; non-zero otherwise.</returns>
	public override int Execute(PfInstallerOptions options) {
		if (string.IsNullOrEmpty(options.ZipFile) && !string.IsNullOrEmpty(options.Product)) {
			options.ZipFile = GetBuildFilePathFromOptions(options.Product, options.DBType, options.RuntimePlatform);
		}

		if (!_msFileSystem.File.Exists(options.ZipFile) && !_msFileSystem.Directory.Exists(options.ZipFile)) {
			_logger.WriteInfo($"Could not find zip file: {options.ZipFile}");
			return 1;
		}

		// Determine deployment strategy to know whether to use IIS or DotNet
		IDeploymentStrategy strategy = SelectDeploymentStrategy(options);
		bool isIisDeployment = strategy is IISDeploymentStrategy;

		// Only use IIS root folder validation for IIS deployments
		if (isIisDeployment) {
			if (!_msFileSystem.Directory.Exists(_iisRootFolder)) {
				_msFileSystem.Directory.CreateDirectory(_iisRootFolder);
			}
		}

		// STEP 1: Get a site name from a user
		while (string.IsNullOrEmpty(options.SiteName)) {
			_logger.WriteLine("Please enter site name:");
			string? input = Console.ReadLine();
			options.SiteName = input?.Trim() ?? string.Empty;

			if (string.IsNullOrEmpty(options.SiteName)) {
				_logger.WriteLine("Site name cannot be empty");
				continue;
			}

			// Validate site name against appropriate root folder
			string rootPath = isIisDeployment
				? _iisRootFolder
				: _msFileSystem.Directory.GetCurrentDirectory();

			if (_msFileSystem.Directory.Exists(_msFileSystem.Path.Combine(rootPath, options.SiteName))) {
				_logger.WriteLine(
					$"Site with name {options.SiteName} already exists in {_msFileSystem.Path.Combine(rootPath, options.SiteName)}");
				options.SiteName = string.Empty;
			}
		}

		// STEP 2: Get port from user
		// Only prompt for port on Windows IIS deployments
		// DotNet deployments on macOS/Linux use default port or user-specified port
		if (isIisDeployment) {
			while (options.SitePort is <= 0 or > 65536) {
				_logger.WriteLine(
					$"Please enter site port, Max value - 65535:{Environment.NewLine}(recommended range between 40000 and 40100)");
				if (int.TryParse(Console.ReadLine(), out int value)) {
					options.SitePort = value;
				}
				else {
					_logger.WriteLine("Site port must be an in value");
				}
			}
		}
		else {
			// For DotNet deployments, check if the user wants to use a custom port or default
			_logger.WriteLine("Port configuration for DotNet deployment:");

			// If the port was already specified via command line, use it
			if (options.SitePort is > 0 and <= 65535) {
				// Port already set, skip prompting
			}
			else {
				bool portSelected = false;

				while (!portSelected) {
					_logger.WriteLine("Press Enter to use default port 8080, or enter a custom port number:");
					string portInput = (Console.ReadLine() ?? string.Empty).Trim();

					int selectedPort = 8080; // Default

					if (string.IsNullOrEmpty(portInput)) {
						selectedPort = 8080;
					}
					else if (int.TryParse(portInput, out int customPort)) {
						if (customPort is > 0 and <= 65535) {
							selectedPort = customPort;
						}
						else {
							_logger.WriteLine(
								"Invalid port number. Port must be between 1 and 65535. Please try again.");
							continue;
						}
					}
					else {
						_logger.WriteLine("Invalid port input. Please enter a number between 1 and 65535.");
						continue;
					}

					// Check port availability for DotNet deployment
					if (!IsPortAvailable(selectedPort)) {
						_logger.WriteLine($"⚠ WARNING: Port {selectedPort} appears to be in use by another process.");
						_logger.WriteLine("What would you like to do?");
						_logger.WriteLine("1. Select a different port (press 1)");
						_logger.WriteLine("2. Try another port (press Enter or any other key for port selection)");

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

		// STEP 3: Now output all logging information after the user has provided input
		_logger.WriteLine(); // Blank line for readability
		_logger.WriteInfo(
			$"[OS Platform] - {(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "Windows")}");
		_logger.WriteInfo($"[Is IIS Deployment] - {isIisDeployment}");
		_logger.WriteInfo($"[Site Name] - {options.SiteName}");
		_logger.WriteInfo($"[Site Port] - {options.SitePort}");

		options.ZipFile = CopyLocalWhenNetworkDrive(options.ZipFile);
		string deploymentFolder = DetermineFolderPath(options);

		string unzippedDirectoryPath = InstallerHelper.UnzipOrTakeExistingOldPath(options.ZipFile, _packageArchiver);
		if (!_fileSystem.ExistsDirectory(deploymentFolder)) {
			_logger.WriteInfo($"[Creating deployment folder] - {deploymentFolder}");
			_fileSystem.CreateDirectory(deploymentFolder);
		}

		string str = $"""
					  [Copy deployment files]
					      From: {unzippedDirectoryPath} 
					      To:   {deploymentFolder}
					  """;
		_logger.WriteInfo(str);
		_fileSystem.CopyDirectoryWithFilter(unzippedDirectoryPath, deploymentFolder, true, source => {
			if (_msFileSystem.Directory.Exists(source)) {
				return _excludedDirectories.Contains(_msFileSystem.Path.GetFileName(source)?.ToLower());
			}

			if (_msFileSystem.File.Exists(source)) {
				return _excludedExtensions.Contains(_msFileSystem.Path.GetExtension(source)?.ToLower());
			}

			return true;
		});


		InstallerHelper.DatabaseType dbType;
		try {
			dbType = InstallerHelper.DetectDataBaseByPath(unzippedDirectoryPath);
		}
		catch (Exception ex) {
			_logger.WriteWarning($"[DetectDataBase] - Could not detect database type: {ex.Message}");
			_logger.WriteInfo("[DetectDataBase] - Defaulting to Postgres");
			dbType = InstallerHelper.DatabaseType.Postgres;
		}

		int dbRestoreResult;

		// Check if the user specified a local database server
		if (!string.IsNullOrEmpty(options.DbServerName)) {
			_logger.WriteInfo($"[Database Restore Mode] - Local server: {options.DbServerName}");
			dbRestoreResult = RestoreToLocalDb(unzippedDirectoryPath, options.SiteName, options.DbServerName,
				options.DropIfExists, options.ZipFile);
		}
		else {
			_logger.WriteInfo("[Database Restore Mode] - Kubernetes cluster");
			dbRestoreResult = dbType switch {
								  InstallerHelper.DatabaseType.MsSql => DoMsWork(unzippedDirectoryPath,
									  options.SiteName),
								  var _ => DoPgWork(unzippedDirectoryPath, options.SiteName,
									  _msFileSystem.Path.GetFileNameWithoutExtension(options.ZipFile))
							  };
		}

		int deploySiteResult = dbRestoreResult switch {
								   0 => DeployApplication(deploymentFolder, options),
								   var _ => ExitWithErrorMessage("Database restore failed")
							   };


		int updateConnectionStringResult = deploySiteResult switch {
											   0 => UpdateConnectionString(deploymentFolder, options, dbType)
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
			IsNetCore
				= InstallerHelper.DetectFrameworkByPath(deploymentFolder) == InstallerHelper.FrameworkType.NetCore,
			EnvironmentPath = deploymentFolder
		});

		// For DotNet deployments, wait for the server to become ready before proceeding
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

	/// <inheritdoc />
	public string GetBuildFilePathFromOptions(string product, CreatioDBType dBType,
		CreatioRuntimePlatform runtimePlatform) {
		return GetBuildFilePathFromOptions(_remoteArtefactServerPath, product, dBType, runtimePlatform);
	}

	/// <inheritdoc />
	public int StartWebBrowser(PfInstallerOptions options) {
		return StartWebBrowser(options, false);
	}

	/// <inheritdoc />
	public int StartWebBrowser(PfInstallerOptions options, bool isIisDeployment) {
		string url = isIisDeployment
			? $"http://{InstallerHelper.FetFQDN()}:{options.SitePort}"
			: $"http://localhost:{options.SitePort}";

		try {
			string program;
			string arguments;

			// Windows
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				program = "cmd";
				arguments = $"/c start {url}";
			}

			//Linux
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				program = "xdg-open";
				arguments = url;
			}

			// macOS
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				program = "open";
				arguments = url;
			}
			else {
				return 0;
			}

			_processExecutor.Execute(program, arguments, false);
		}
		catch (Exception ex) {
			_logger.WriteError($"Failed to launch web browser: {ex.Message}");
			return 1;
		}

		return 0;
	}

	#endregion
}
