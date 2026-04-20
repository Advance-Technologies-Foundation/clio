using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.db;
using Clio.UserEnvironment;
using CommandLine;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

#region Class: RestoreDbCommandOptions

[Verb("restore-db", Aliases = ["rdb"], HelpText = "Restores database from backup file")]
public class RestoreDbCommandOptions : EnvironmentOptions{
	/// <summary>
	/// Gets or sets a value indicating whether the post-restore forced-password-reset disabling script may run.
	/// </summary>
	[Option("disable-reset-password", Required = false, Hidden = true, Default = true,
		HelpText = "Disables reset password after restore")]
	public bool DisableResetPassword { get; set; } = true;

	[Option( "dbName", Required = false, HelpText = "dbName")]
	public string DbName { get; set; }
	
	[Option( "backupPath", Required = false, HelpText = "backup Path")]
	public string BackupPath { get; set; }

	[Option("dbServerName", Required = false, 
		HelpText = "Name of database server configuration from appsettings.json")]
	public string DbServerName { get; set; }

	[Option("drop-if-exists", Required = false, 
		HelpText = "Automatically drops existing database if present without prompting")]
	public bool DropIfExists { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether restore-db should create or refresh only the PostgreSQL template.
	/// </summary>
	[Option("as-template", Required = false,
		HelpText = "Create or refresh only the PostgreSQL template without creating a target database")]
	public bool AsTemplate { get; set; }
	
}

#endregion

#region Class: RestoreDbCommand

public class RestoreDbCommand : Command<RestoreDbCommandOptions>
{

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly IAbstractionsFileSystem _abstractionsFileSystem;
	private readonly IDbClientFactory _dbClientFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly ICreatioInstallerService _creatioInstallerService;
	private readonly IDbConnectionTester _dbConnectionTester;
	private readonly IBackupFileDetector _backupFileDetector;
	private readonly IPostgresToolsPathDetector _postgresToolsPathDetector;
	private readonly IProcessExecutor _processExecutor;
	private readonly IDbOperationLogSessionFactory _dbOperationLogSessionFactory;
	private readonly IDbOperationLogContextAccessor _dbOperationLogContextAccessor;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="RestoreDbCommand"/> class.
	/// </summary>
	/// <param name="logger">Logger used for command output.</param>
	/// <param name="fileSystem">Filesystem abstraction.</param>
	/// <param name="dbClientFactory">Factory for database clients.</param>
	/// <param name="settingsRepository">Repository for environment and DB server settings.</param>
	/// <param name="creatioInstallerService">Installer service used for Kubernetes/postgres restore flows.</param>
	/// <param name="dbConnectionTester">Service that validates local database connectivity before restore.</param>
	/// <param name="backupFileDetector">Service that detects backup file type.</param>
	/// <param name="postgresToolsPathDetector">Service that resolves <c>pg_restore</c> path.</param>
	/// <param name="processExecutor">Process execution abstraction used to run <c>pg_restore</c>.</param>
	/// <param name="dbOperationLogSessionFactory">Factory that creates per-invocation database operation log artifacts.</param>
	/// <param name="dbOperationLogContextAccessor">Accessor for the active database operation log session.</param>
	public RestoreDbCommand(ILogger logger, IFileSystem fileSystem, IAbstractionsFileSystem abstractionsFileSystem,
		IDbClientFactory dbClientFactory,
		ISettingsRepository settingsRepository, ICreatioInstallerService creatioInstallerService,
		IDbConnectionTester dbConnectionTester, IBackupFileDetector backupFileDetector,
		IPostgresToolsPathDetector postgresToolsPathDetector, IProcessExecutor processExecutor,
		IDbOperationLogSessionFactory dbOperationLogSessionFactory = null,
		IDbOperationLogContextAccessor dbOperationLogContextAccessor = null) {
		_logger = logger;
		_fileSystem = fileSystem;
		_abstractionsFileSystem = abstractionsFileSystem;
		_dbClientFactory = dbClientFactory;
		_settingsRepository = settingsRepository;
		_creatioInstallerService = creatioInstallerService;
		_dbConnectionTester = dbConnectionTester;
		_backupFileDetector = backupFileDetector;
		_postgresToolsPathDetector = postgresToolsPathDetector;
		_processExecutor = processExecutor;
		_dbOperationLogSessionFactory = dbOperationLogSessionFactory ?? NullDbOperationLogSessionFactory.Instance;
		_dbOperationLogContextAccessor = dbOperationLogContextAccessor ?? NullDbOperationLogContextAccessor.Instance;
	}

	#endregion

	#region Methods: Public

	public override int Execute(RestoreDbCommandOptions options) {
		using IDbOperationLogSession dbOperationLogSession = _dbOperationLogSessionFactory.BeginSession("restore-db");
		try {
			int? directBackupRestoreResult = TryHandleDirectBackupRestore(options);
			if (directBackupRestoreResult.HasValue) {
				return directBackupRestoreResult.Value;
			}

			int result;

			if (!string.IsNullOrEmpty(options.DbServerName)) {
				result = RestoreToLocalServer(options);
				return result;
			}
		
			EnvironmentSettings env = _settingsRepository.GetEnvironment(options);
			if (env.DbServer?.Uri == null) {
				_logger.WriteError("Database server configuration is required for this restore mode.");
				return 1;
			}

			if (options.AsTemplate) {
				_logger.WriteError("--as-template is supported only for PostgreSQL .backup files or ZIP packages.");
				return 1;
			}
			result = env.DbServer.Uri.Scheme switch {
				"mssql" => RestoreMs(env.DbServer, env.DbName, options.Force, env.BackupFilePath),
				var _ => HandleIncorrectUri(options.Uri)
			};
			if (result == 0) {
				TryDisableForcedPasswordReset(options, InstallerHelper.DatabaseType.MsSql, env.DbName,
					options.BackupPath ?? env.BackupFilePath);
			}
			_logger.WriteLine("Done");
			return result;
		}
		finally {
			string? logFilePath = dbOperationLogSession.LogFilePath;
			_logger.WriteInfo($"Database operation log: {logFilePath}");
		}
	}

	private int HandleIncorrectUri(string uri){
		string safeUri = SanitizeUriForLogging(uri);
		_logger.WriteError($"Scheme {safeUri} is not supported.\r\n\tExample: mssql://127.0.0.1:1433 or\r\n\tpgsql://127.0.0.1:5432");
		return 1;
	}
	private int RestoreMs(DbServer dbServer, string dbName, bool force, string backUpFilePath){
		Credentials credentials  = dbServer.GetCredentials();
		IMssql mssql = _dbClientFactory.CreateMssql(dbServer.Uri.Host, dbServer.Uri.Port, credentials.Username, credentials.Password);
		if(mssql.CheckDbExists(dbName)) {
			bool shouldDrop = force;
			if(!shouldDrop) {
				_logger.WriteWarning($"Database {dbName} already exists, would you like to keep it ? (Y / N)");
				var newName = Console.ReadLine();
				shouldDrop = newName.ToUpper(CultureInfo.InvariantCulture) == "N";
			}
			if(!shouldDrop) {
				string backupDbName = $"{dbName}_{DateTime.Now:yyyyMMddHHmmss}";
				mssql.RenameDb(dbName, backupDbName);
				_logger.WriteInfo($"Renamed existing database from {dbName} to {backupDbName}");
			}else {
				mssql.DropDb(dbName);
				_logger.WriteInfo($"Dropped existing database {dbName}");
			}
		}
		_fileSystem.CopyFiles(new[]{backUpFilePath}, dbServer.WorkingFolder, true);
		_logger.WriteInfo($"Copied backup file to server \r\n\tfrom: {backUpFilePath} \r\n\tto  : {dbServer.WorkingFolder}");
		
		_logger.WriteInfo("Started db restore...");
		DatabaseRestoreResult restoreResult = mssql.RestoreDatabase(
			dbName,
			GetFileName(backUpFilePath),
			WriteNativeLogLine);
		int result = restoreResult.Success ? 0 : 1;
		_logger.WriteInfo($"Created database {dbName} from file {backUpFilePath}");
		return result;
	}

	private int RestorePg(string dbName, string backupFilePath ){
		string directoryPath = GetDirectoryName(backupFilePath);
		string templateName = GetFileNameWithoutExtension(backupFilePath);
		return _creatioInstallerService.DoPgWork(directoryPath, dbName, templateName);
	}

	private int RestorePg(string dbName, string backupFilePath, string sourceTemplateName) {
		string directoryPath = GetDirectoryName(backupFilePath);
		string templateName = string.IsNullOrWhiteSpace(sourceTemplateName)
			? GetFileNameWithoutExtension(backupFilePath)
			: sourceTemplateName;
		return _creatioInstallerService.DoPgWork(directoryPath, dbName, templateName);
	}

	private string EnsurePgTemplateAndGetName(string backupFilePath, string sourceTemplateName, bool dropIfExists) {
		string directoryPath = GetDirectoryName(backupFilePath);
		string templateName = string.IsNullOrWhiteSpace(sourceTemplateName)
			? GetFileNameWithoutExtension(backupFilePath)
			: sourceTemplateName;
		return _creatioInstallerService.EnsurePgTemplateAndGetName(directoryPath, templateName, dropIfExists);
	}

	private int RestoreToLocalServer(RestoreDbCommandOptions options) {
		LocalDbServerConfiguration config = _settingsRepository.GetLocalDbServer(options.DbServerName);
		string originalBackupPath = options.BackupPath;
		
		if (config == null) {
			var availableServers = _settingsRepository.GetLocalDbServerNames().ToList();
			string availableList = availableServers.Any() 
				? string.Join(", ", availableServers) 
				: "(none configured)";
			_logger.WriteError($"Database server configuration '{options.DbServerName}' was not found or is disabled in appsettings.json. Available enabled configurations: {availableList}");
			return 1;
		}

		if (string.IsNullOrEmpty(options.BackupPath)) {
			_logger.WriteError("BackupPath is required when using dbServerName");
			return 1;
		}

		if (!_fileSystem.ExistsFile(options.BackupPath)) {
			_logger.WriteError($"Backup file not found at {options.BackupPath}");
			return 1;
		}

		if (!options.AsTemplate && string.IsNullOrEmpty(options.DbName)) {
			_logger.WriteError("DbName is required when restoring to local server");
			return 1;
		}

		if (options.AsTemplate && !string.IsNullOrEmpty(options.DbName)) {
			_logger.WriteWarning("DbName is ignored when --as-template is specified.");
		}

		string backupPath = options.BackupPath;
		string extractedPath = null;

		try {
			if (GetFileExtension(backupPath) == ".zip") {
				_logger.WriteInfo($"ZIP file detected, extracting backup file...");
				extractedPath = ExtractBackupFromZip(backupPath, config.DbType);
				if (string.IsNullOrEmpty(extractedPath)) {
					_logger.WriteError("Failed to extract backup file from ZIP");
					return 1;
				}
				backupPath = extractedPath;
				_logger.WriteInfo($"Extracted backup file: {backupPath}");
			}

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
				WriteDockerPostgresConnectionHint(config);
				return 1;
			}

			_logger.WriteInfo("Connection test successful");

			BackupFileType detectedType = _backupFileDetector.DetectBackupType(backupPath);
			string dbType = config.DbType?.ToLowerInvariant();

			if (detectedType == BackupFileType.Unknown) {
				_logger.WriteError($"Cannot determine backup file type from {backupPath}. Supported extensions: .backup (PostgreSQL), .bak (MSSQL)");
				return 1;
			}

			bool isCompatible = (detectedType == BackupFileType.PostgresBackup && (dbType == "postgres" || dbType == "postgresql")) ||
			                    (detectedType == BackupFileType.MssqlBackup && dbType == "mssql");

			if (!isCompatible) {
				_logger.WriteError($"Backup file type {detectedType} is not compatible with database type {config.DbType}");
				return 1;
			}

			_logger.WriteInfo($"Restoring {detectedType} backup to {config.DbType} server...");

			int result = dbType switch {
				"mssql" => RestoreMssqlToLocalServer(config, backupPath, options.DbName, options.DropIfExists),
				"postgres" or "postgresql" => RestorePostgresToLocalServer(
					config,
					backupPath,
					options.DbName,
					options.DropIfExists,
					options.AsTemplate,
					originalBackupPath),
				_ => HandleUnsupportedDbType(config.DbType)
			};
			if (result == 0 && !options.AsTemplate) {
				InstallerHelper.DatabaseType databaseType = dbType switch {
					"mssql" => InstallerHelper.DatabaseType.MsSql,
					"postgres" or "postgresql" => InstallerHelper.DatabaseType.Postgres,
					var _ => InstallerHelper.DatabaseType.Postgres
				};
				TryDisableForcedPasswordReset(options, databaseType, options.DbName, originalBackupPath);
			} else if (result == 0 && options.AsTemplate && (dbType == "postgres" || dbType == "postgresql")) {
				string templateName = TryResolveLocalPostgresTemplateName(config, originalBackupPath);
				if (!string.IsNullOrWhiteSpace(templateName)) {
					_logger.WriteInfo($"Template database name: {templateName}");
					TryDisableForcedPasswordReset(options, InstallerHelper.DatabaseType.Postgres, templateName,
						originalBackupPath);
				}
			}
			return result;
		} finally {
			if (!string.IsNullOrEmpty(extractedPath) && _fileSystem.ExistsFile(extractedPath)) {
				try {
					string extractedDir = GetDirectoryName(extractedPath);
					if (_fileSystem.ExistsDirectory(extractedDir)) {
						_fileSystem.DeleteDirectory(extractedDir, true);
						_logger.WriteInfo("Cleaned up temporary extracted files");
					}
				} catch {
					// Ignore cleanup errors
				}
			}
		}
	}

	private string ExtractBackupFromZip(string zipPath, string dbType) {
		return ExtractBackupFromZip(zipPath, dbType, out _);
	}

	private string ExtractBackupFromZip(string zipPath, string dbType, out BackupFileType backupType) {
		backupType = BackupFileType.Unknown;
		try {
			string tempDir = CombinePath(_abstractionsFileSystem.Path.GetTempPath(), $"clio_restore_{Guid.NewGuid():N}");
			_fileSystem.CreateDirectory(tempDir);

			_logger.WriteInfo($"Extracting ZIP file to temporary directory: {tempDir}");

			using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
			string? preferredExtension = dbType?.ToLowerInvariant() switch {
				"mssql" => ".bak",
				"postgres" or "postgresql" => ".backup",
				var _ => null
			};
			string searchPattern = preferredExtension ?? "*.backup or *.bak";
			var backupEntries = archive.Entries
				.Where(e => !string.IsNullOrWhiteSpace(e.Name))
				.Where(e => e.FullName.Contains("db/") || e.FullName.Contains("db\\") ||
					(!e.FullName.Contains('/') && !e.FullName.Contains('\\')))
				.Where(e => {
					string extension = GetFileExtension(e.Name);
					return extension is ".backup" or ".bak";
				})
				.ToList();

			var backupEntry = !string.IsNullOrWhiteSpace(preferredExtension)
				? backupEntries.FirstOrDefault(e => GetFileExtension(e.Name) == preferredExtension)
				: backupEntries
					.OrderByDescending(e => GetFileExtension(e.Name) == ".backup")
					.FirstOrDefault();

			if (backupEntry == null) {
				_logger.WriteError($"No backup file found in ZIP. Expected a file in 'db/' folder with extension {searchPattern}");
				return null;
			}

			string extractPath = CombinePath(tempDir, backupEntry.Name);
			backupEntry.ExtractToFile(extractPath);
			backupType = GetFileExtension(backupEntry.Name) == ".bak"
				? BackupFileType.MssqlBackup
				: BackupFileType.PostgresBackup;
				
			_logger.WriteInfo($"Extracted: {backupEntry.FullName} ({backupEntry.Length / 1024 / 1024} MB)");
				
			return extractPath;
		} catch (Exception ex) {
			_logger.WriteError($"Failed to extract ZIP file: {ex.Message}");
			return null;
		}
	}

	private int RestoreMssqlToLocalServer(LocalDbServerConfiguration config, string backupPath, string dbName, bool dropIfExists) {
		try {
			IMssql mssql = _dbClientFactory.CreateMssql(config.Hostname, config.Port, config.Username, config.Password, config.UseWindowsAuth);
			
			if (mssql.CheckDbExists(dbName)) {
				if (!dropIfExists) {
					_logger.WriteError($"Database {dbName} already exists. Use --drop-if-exists flag to automatically drop it.");
					return 1;
				}
				_logger.WriteWarning($"Database {dbName} already exists");
				_logger.WriteWarning("Dropping existing database...");
				mssql.DropDb(dbName);
				_logger.WriteInfo($"Dropped existing database {dbName}");
			}

			string dataPath = mssql.GetDataPath();
			_logger.WriteInfo($"SQL Server data path: {dataPath}");

			string backupFileName = GetFileName(backupPath);
			string destinationPath = CombinePath(dataPath, backupFileName);

			_logger.WriteInfo($"Copying backup file to SQL Server data directory...");
			_fileSystem.CopyFiles(new[] { backupPath }, dataPath, true);
			_logger.WriteInfo($"Copied backup file \r\n\tfrom: {backupPath} \r\n\tto  : {destinationPath}");

			_logger.WriteInfo("Starting database restore...");
			_logger.WriteInfo("This may take several minutes depending on database size. SQL Server will report progress every 5%.");
			DatabaseRestoreResult restoreResult = mssql.RestoreDatabase(
				dbName,
				backupFileName,
				WriteNativeLogLine);
			
			if (restoreResult.Success) {
				_logger.WriteInfo($"Successfully restored database {dbName} from {backupPath}");
				return 0;
			} else {
				_logger.WriteError($"Failed to restore database {dbName}");
				return 1;
			}
		} catch (Exception ex) {
			_logger.WriteError($"Error restoring MSSQL database: {ex.Message}");
			return 1;
		}
	}

	private int RestorePostgresToLocalServer(
		LocalDbServerConfiguration config,
		string backupPath,
		string dbName,
		bool dropIfExists,
		bool asTemplate = false,
		string sourcePath = null) {
		string pgRestorePath = _postgresToolsPathDetector.GetPgRestorePath(config.PgToolsPath);
		
		if (string.IsNullOrEmpty(pgRestorePath)) {
            _logger.WriteError("pg_restore not found. Install PostgreSQL client tools on the machine running clio, ensure pg_restore is in PATH, or specify pgToolsPath in configuration.");
            _logger.WriteInfo("When using --dbServerName with PostgreSQL, clio runs pg_restore locally against the configured host and port; it does not copy the .backup file into Docker or Kubernetes.");
			_logger.WriteInfo("Download PostgreSQL from: https://www.postgresql.org/download/");
			if (!string.IsNullOrEmpty(config.PgToolsPath)) {
				_logger.WriteError($"pg_restore not found at specified path: {config.PgToolsPath}");
			}
			return 1;
		}

		_logger.WriteInfo($"Using pg_restore from: {pgRestorePath}");

		try {
			Postgres postgres = _dbClientFactory.CreatePostgres(config.Hostname, config.Port, config.Username, config.Password);
			if (!asTemplate) {
				if (postgres.CheckDbExists(dbName)) {
					if (!dropIfExists) {
						_logger.WriteError($"Database {dbName} already exists. Use --drop-if-exists flag to automatically drop it.");
						return 1;
					}
					_logger.WriteWarning($"Database {dbName} already exists");
					_logger.WriteWarning("Dropping existing database...");
					postgres.DropDb(dbName);
					_logger.WriteInfo($"Dropped existing database {dbName}");
				}

				_logger.WriteInfo($"Creating database {dbName}...");
				bool dbCreated = postgres.CreateDb(dbName);
				if (!dbCreated) {
					_logger.WriteError($"Failed to create database {dbName}");
					return 1;
				}

				_logger.WriteInfo($"Database {dbName} created successfully");
				_logger.WriteInfo($"Starting restore from {backupPath}...");
				_logger.WriteInfo($"Running local pg_restore against {config.Hostname}:{config.Port}. The backup file stays on the host filesystem and is not copied into Docker or Kubernetes.");
				if (Program.IsDebugMode) {
					_logger.WriteInfo("This may take several minutes depending on database size. Detailed progress will be shown below:");
				} else {
					_logger.WriteInfo("This may take several minutes depending on database size. Run with --debug flag to see detailed progress.");
				}

				int restoreExitCode = ExecutePgRestoreCommand(pgRestorePath, config, backupPath, dbName);
				if (restoreExitCode == 0) {
					_logger.WriteInfo($"Successfully restored database {dbName} from {backupPath}");
					return 0;
				}

				_logger.WriteError($"pg_restore failed with exit code {restoreExitCode}");
				return 1;
			}

			string sourceFileName = !string.IsNullOrEmpty(sourcePath)
				? GetFileNameWithoutExtension(sourcePath)
				: GetFileNameWithoutExtension(backupPath);
			string templateName = postgres.FindTemplateBySourceFile(sourceFileName);

			if (!string.IsNullOrWhiteSpace(templateName) && dropIfExists) {
				_logger.WriteWarning(
					$"Template '{templateName}' already exists for source '{sourceFileName}'. Dropping existing template...");
				if (!postgres.DropDb(templateName)) {
					_logger.WriteError($"Failed to drop template database {templateName}");
					return 1;
				}

				_logger.WriteInfo($"Dropped template database {templateName}");
				templateName = null;
			}

			if (string.IsNullOrWhiteSpace(templateName)) {
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
				_logger.WriteInfo($"Running local pg_restore against {config.Hostname}:{config.Port}. The backup file stays on the host filesystem and is not copied into Docker or Kubernetes.");
				if (Program.IsDebugMode) {
					_logger.WriteInfo("This may take several minutes depending on database size. Detailed progress will be shown below:");
				} else {
					_logger.WriteInfo("This may take several minutes depending on database size. Run with --debug flag to see detailed progress.");
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

				string metadata = $"sourceFile:{sourceFileName}|createdDate:{DateTime.UtcNow:o}|version:1.0";
				postgres.SetDatabaseComment(templateName, metadata);
				_logger.WriteInfo($"Template database {templateName} created successfully with source reference: {sourceFileName}");
			} else {
				_logger.WriteInfo($"Found existing template '{templateName}' for source '{sourceFileName}', skipping restore");
			}

			_logger.WriteInfo($"Template database {templateName} is ready.");
			return 0;
		} catch (Exception ex) {
			_logger.WriteError($"Error restoring PostgreSQL database: {ex.Message}");
			return 1;
		}
	}

	private int ExecutePgRestoreCommand(string pgRestorePath, LocalDbServerConfiguration config, string backupPath, string dbName) {
        string arguments = $"-h {config.Hostname} -p {config.Port} -U {config.Username} -d {dbName} -v --no-owner --no-privileges \"{backupPath}\"";
		DateTime lastProgressMessage = DateTime.Now;
		ProcessExecutionOptions executionOptions = new(pgRestorePath, arguments) {
			EnvironmentVariables = new Dictionary<string, string> {
				["PGPASSWORD"] = config.Password
			},
			OnOutput = (line, stream) => {
				if (string.IsNullOrEmpty(line)) {
					return;
				}

				WriteNativeLogLine(FormatProcessOutputLine(line, stream));

				if (Program.IsDebugMode) {
					_logger.WriteDebug(line);
					return;
				}

				if (stream == ProcessOutputStream.StdOut &&
					(DateTime.Now - lastProgressMessage).TotalSeconds >= 30) {
					_logger.WriteInfo("Restore in progress...");
					lastProgressMessage = DateTime.Now;
				}
			}
		};
		ProcessExecutionResult result = _processExecutor.ExecuteWithRealtimeOutputAsync(executionOptions)
			.GetAwaiter()
			.GetResult();
		return result.Started ? result.ExitCode ?? 1 : 1;
	}

	private void WriteDockerPostgresConnectionHint(LocalDbServerConfiguration config) {
		if (!string.Equals(config.DbType, "postgres", StringComparison.OrdinalIgnoreCase) &&
		    !string.Equals(config.DbType, "postgresql", StringComparison.OrdinalIgnoreCase)) {
			return;
		}
		_logger.WriteWarning($"If PostgreSQL is running in Docker, verify docker ps shows the container, port {config.Port} is published to the host, and connect using the published host port in appsettings.json.");
	}
	private int HandleUnsupportedDbType(string dbType) {
		_logger.WriteError($"Database type '{dbType}' is not supported. Supported types: mssql, postgres");
		return 1;
	}

	private void TryDisableForcedPasswordReset(
		RestoreDbCommandOptions options,
		InstallerHelper.DatabaseType dbType,
		string databaseName,
		string backupPath) {
		PfInstallerOptions installerOptions = new() {
			DisableResetPassword = options.DisableResetPassword,
			DbServerName = options.DbServerName,
			SiteName = databaseName,
			ZipFile = backupPath ?? string.Empty
		};
		_creatioInstallerService.TryDisableForcedPasswordReset(installerOptions, dbType);
	}

	private string TryResolveLocalPostgresTemplateName(LocalDbServerConfiguration config, string sourcePath) {
		try {
			Postgres postgres = _dbClientFactory.CreatePostgres(config.Hostname, config.Port, config.Username,
				config.Password);
			string sourceFileName = GetFileNameWithoutExtension(sourcePath);
			return postgres.FindTemplateBySourceFile(sourceFileName);
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Failed to resolve template name for password reset: {ex.Message}");
			return null;
		}
	}

	private void WriteNativeLogLine(string line) {
		_dbOperationLogContextAccessor.CurrentSession?.WriteNativeLine(line);
	}

	private static string SanitizeUriForLogging(string uri) {
		if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsedUri)) {
			return "(invalid-uri)";
		}

		return parsedUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);
	}

	private static string FormatProcessOutputLine(string line, ProcessOutputStream stream) {
		return stream == ProcessOutputStream.StdErr ? $"[STDERR] {line}" : line;
	}

	private string GetFileExtension(string path) =>
		GetPathExtension(path).ToLowerInvariant();

	private string GetFileName(string path) {
		int lastSeparatorIndex = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
		return lastSeparatorIndex >= 0
			? path[(lastSeparatorIndex + 1)..]
			: path;
	}

	private string GetFileNameWithoutExtension(string path) {
		string fileName = GetFileName(path);
		string extension = GetPathExtension(fileName);
		return string.IsNullOrEmpty(extension)
			? fileName
			: fileName[..^extension.Length];
	}

	private string GetDirectoryName(string path) {
		int lastSeparatorIndex = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
		return lastSeparatorIndex > 0
			? path[..lastSeparatorIndex]
			: string.Empty;
	}

	private string CombinePath(params string[] parts) {
		string separator = parts.Any(part => part.Contains('\\')) ? "\\" : "/";
		IEnumerable<string> sanitizedParts = parts
			.Where(part => !string.IsNullOrWhiteSpace(part))
			.Select(part => part.TrimEnd('\\', '/'))
			.Select((part, index) => index == 0 ? part : part.TrimStart('\\', '/'));
		return string.Join(separator, sanitizedParts);
	}

	private static string GetPathExtension(string path) {
		string fileName = path.Replace('\\', '/');
		int lastSeparatorIndex = fileName.LastIndexOf('/');
		if (lastSeparatorIndex >= 0) {
			fileName = fileName[(lastSeparatorIndex + 1)..];
		}

		int lastDotIndex = fileName.LastIndexOf('.');
		return lastDotIndex >= 0
			? fileName[lastDotIndex..]
			: string.Empty;
	}

	private int? TryHandleDirectBackupRestore(RestoreDbCommandOptions options) {
		if (string.IsNullOrWhiteSpace(options.BackupPath) || !string.IsNullOrWhiteSpace(options.DbServerName)) {
			return null;
		}

		string backupPath = options.BackupPath;
		string? extractedPath = null;
		string? sourceTemplateName = null;
		BackupFileType detectedType = BackupFileType.Unknown;

		try {
			string extension = GetFileExtension(backupPath);
			if (extension == ".backup") {
				detectedType = BackupFileType.PostgresBackup;
				sourceTemplateName = GetFileNameWithoutExtension(backupPath);
			} else if (extension == ".zip") {
				sourceTemplateName = GetFileNameWithoutExtension(backupPath);
				extractedPath = ExtractBackupFromZip(backupPath, null, out detectedType);
				if (string.IsNullOrWhiteSpace(extractedPath)) {
					return 1;
				}

				backupPath = extractedPath;
			} else if (_fileSystem.ExistsDirectory(backupPath)) {
				string[] backupFiles = _fileSystem.GetFiles(backupPath, "*.backup", SearchOption.AllDirectories);
				if (backupFiles.Length == 0) {
					_logger.WriteError($"No .backup files found in directory: {options.BackupPath}");
					return 1;
				}

				detectedType = BackupFileType.PostgresBackup;
				backupPath = backupFiles[0];
				sourceTemplateName = GetFileNameWithoutExtension(backupPath);
			} else {
				return null;
			}

			if (detectedType == BackupFileType.PostgresBackup) {
				if (options.AsTemplate) {
					if (!string.IsNullOrWhiteSpace(options.DbName)) {
						_logger.WriteWarning("DbName is ignored when --as-template is specified.");
					}

					_logger.WriteInfo($"Creating PostgreSQL template from backup file: {backupPath}");
					string templateName = EnsurePgTemplateAndGetName(backupPath, sourceTemplateName,
						options.DropIfExists);
					if (string.IsNullOrWhiteSpace(templateName)) {
						return 1;
					}

					_logger.WriteInfo($"Template database name: {templateName}");
					TryDisableForcedPasswordReset(options, InstallerHelper.DatabaseType.Postgres, templateName,
						options.BackupPath);
					return 0;
				}

				if (string.IsNullOrWhiteSpace(options.DbName)) {
					_logger.WriteError("DbName is required unless --as-template is specified.");
					return 1;
				}

				_logger.WriteInfo($"Restoring database from backup file: {backupPath}");
				int restoreResult = RestorePg(options.DbName, backupPath, sourceTemplateName);
				if (restoreResult == 0) {
					TryDisableForcedPasswordReset(options, InstallerHelper.DatabaseType.Postgres, options.DbName,
						options.BackupPath);
				}

				return restoreResult;
			}

			if (options.AsTemplate) {
				_logger.WriteError("--as-template is supported only for PostgreSQL backups.");
				return 1;
			}

			return null;
		}
		finally {
			if (!string.IsNullOrWhiteSpace(extractedPath) && _fileSystem.ExistsFile(extractedPath)) {
				try {
					string? extractedDirectory = GetDirectoryName(extractedPath);
					if (!string.IsNullOrWhiteSpace(extractedDirectory) && _fileSystem.ExistsDirectory(extractedDirectory)) {
						_fileSystem.DeleteDirectory(extractedDirectory, true);
						_logger.WriteInfo("Cleaned up temporary extracted files");
					}
				}
				catch {
					// Ignore cleanup errors.
				}
			}
		}
	}
	
	
	#endregion

}

#endregion








