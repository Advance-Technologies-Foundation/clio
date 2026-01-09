using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.Common.db;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

#region Class: RestoreDbCommandOptions

[Verb("restore-db", Aliases = ["rdb"], HelpText = "Restores database from backup file")]
public class RestoreDbCommandOptions : EnvironmentOptions{
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
	
}

#endregion

#region Class: RestoreDbCommand

public class RestoreDbCommand : Command<RestoreDbCommandOptions>
{

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly IDbClientFactory _dbClientFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly ICreatioInstallerService _creatioInstallerService;
	private readonly IDbConnectionTester _dbConnectionTester;
	private readonly IBackupFileDetector _backupFileDetector;
	private readonly IPostgresToolsPathDetector _postgresToolsPathDetector;

	#endregion

	#region Constructors: Public

	public RestoreDbCommand(ILogger logger, IFileSystem fileSystem, IDbClientFactory dbClientFactory, 
		ISettingsRepository settingsRepository, ICreatioInstallerService creatioInstallerService,
		IDbConnectionTester dbConnectionTester, IBackupFileDetector backupFileDetector,
		IPostgresToolsPathDetector postgresToolsPathDetector) {
		_logger = logger;
		_fileSystem = fileSystem;
		_dbClientFactory = dbClientFactory;
		_settingsRepository = settingsRepository;
		_creatioInstallerService = creatioInstallerService;
		_dbConnectionTester = dbConnectionTester;
		_backupFileDetector = backupFileDetector;
		_postgresToolsPathDetector = postgresToolsPathDetector;
	}

	#endregion

	#region Methods: Public

	public override int Execute(RestoreDbCommandOptions options) {

		if (!string.IsNullOrEmpty(options.DbServerName)) {
			return RestoreToLocalServer(options);
		}

		if (!string.IsNullOrEmpty(options.BackupPath)) {
			
			if (Path.GetExtension(options.BackupPath) == ".backup") {
				
				_logger.WriteInfo($"Restoring database from backup file: {options.BackupPath}");
				return RestorePg(options.DbName, options.BackupPath);
			}
			
			if (_fileSystem.ExistsDirectory(options.BackupPath)) {
				string[] backupFiles = _fileSystem.GetFiles(
					options.BackupPath, "*.backup", SearchOption.AllDirectories);
				if (backupFiles.Length == 0) {
					_logger.WriteError($"No .backup files found in directory: {options.BackupPath}");
					return 1;
				}
				_logger.WriteInfo($"Restoring database from backup file: {backupFiles[0]}");
				return RestorePg(options.DbName, backupFiles[0]);
			}
		}
		
		EnvironmentSettings env = _settingsRepository.GetEnvironment(options);
		var result =  env.DbServer.Uri.Scheme switch {
			 "mssql" => RestoreMs(env.DbServer, env.DbName, options.Force, env.BackupFilePath),
			  var _ => HandleIncorrectUri(options.Uri)
		};
		_logger.WriteLine("Done");
		return result;
	}

	private int HandleIncorrectUri(string uri){
		_logger.WriteError($"Scheme {uri} is not supported.\r\n\tExample: mssql://user:pass@127.0.01:1433 or\r\n\tpgsql://user:pass@127.0.01:5432");
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
		int result =  mssql.CreateDb(dbName, Path.GetFileName(backUpFilePath)) ? 0 : 1;
		_logger.WriteInfo($"Created database {dbName} from file {backUpFilePath}");
		return result;
	}

	private int RestorePg(string dbName, string backupFilePath ){
		var fileInfo = new FileInfo(backupFilePath);
		DirectoryInfo directoryInfo = fileInfo.Directory;
		string templateName = Path.GetFileNameWithoutExtension(backupFilePath);
		return _creatioInstallerService.DoPgWork(directoryInfo, dbName, templateName);
	}

	private int RestoreToLocalServer(RestoreDbCommandOptions options) {
		LocalDbServerConfiguration config = _settingsRepository.GetLocalDbServer(options.DbServerName);
		
		if (config == null) {
			var availableServers = _settingsRepository.GetLocalDbServerNames().ToList();
			string availableList = availableServers.Any() 
				? string.Join(", ", availableServers) 
				: "(none configured)";
			_logger.WriteError($"Database server configuration '{options.DbServerName}' not found in appsettings.json. Available configurations: {availableList}");
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

		if (string.IsNullOrEmpty(options.DbName)) {
			_logger.WriteError("DbName is required when restoring to local server");
			return 1;
		}

		string backupPath = options.BackupPath;
		string extractedPath = null;

		try {
			if (Path.GetExtension(backupPath).ToLowerInvariant() == ".zip") {
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

			return dbType switch {
				"mssql" => RestoreMssqlToLocalServer(config, backupPath, options.DbName, options.DropIfExists),
				"postgres" or "postgresql" => RestorePostgresToLocalServer(config, backupPath, options.DbName, options.DropIfExists),
				_ => HandleUnsupportedDbType(config.DbType)
			};
		} finally {
			if (!string.IsNullOrEmpty(extractedPath) && _fileSystem.ExistsFile(extractedPath)) {
				try {
					string extractedDir = Path.GetDirectoryName(extractedPath);
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
		try {
			string tempDir = Path.Combine(Path.GetTempPath(), $"clio_restore_{Guid.NewGuid():N}");
			_fileSystem.CreateDirectory(tempDir);

			_logger.WriteInfo($"Extracting ZIP file to temporary directory: {tempDir}");

			using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath)) {
				string searchPattern = dbType?.ToLowerInvariant() == "mssql" ? "*.bak" : "*.backup";
				
				var backupEntry = archive.Entries
					.FirstOrDefault(e => e.FullName.Contains("db/") && 
					                    Path.GetExtension(e.Name).ToLowerInvariant() == (dbType?.ToLowerInvariant() == "mssql" ? ".bak" : ".backup"));

				if (backupEntry == null) {
					_logger.WriteError($"No backup file found in ZIP. Expected a file in 'db/' folder with extension {searchPattern}");
					return null;
				}

				string extractPath = Path.Combine(tempDir, backupEntry.Name);
				backupEntry.ExtractToFile(extractPath);
				
				_logger.WriteInfo($"Extracted: {backupEntry.FullName} ({backupEntry.Length / 1024 / 1024} MB)");
				
				return extractPath;
			}
		} catch (Exception ex) {
			_logger.WriteError($"Failed to extract ZIP file: {ex.Message}");
			return null;
		}
	}

	private int RestoreMssqlToLocalServer(LocalDbServerConfiguration config, string backupPath, string dbName, bool dropIfExists) {
		try {
			IMssql mssql = _dbClientFactory.CreateMssql(config.Hostname, config.Port, config.Username, config.Password);
			
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

			string backupFileName = Path.GetFileName(backupPath);
			string destinationPath = Path.Combine(dataPath, backupFileName);

			_logger.WriteInfo($"Copying backup file to SQL Server data directory...");
			_fileSystem.CopyFiles(new[] { backupPath }, dataPath, true);
			_logger.WriteInfo($"Copied backup file \r\n\tfrom: {backupPath} \r\n\tto  : {destinationPath}");

			_logger.WriteInfo("Starting database restore...");
			_logger.WriteInfo("This may take several minutes depending on database size. SQL Server will report progress every 5%.");
			bool success = mssql.CreateDb(dbName, backupFileName);
			
			if (success) {
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

	private int RestorePostgresToLocalServer(LocalDbServerConfiguration config, string backupPath, string dbName, bool dropIfExists) {
		string pgRestorePath = _postgresToolsPathDetector.GetPgRestorePath(config.PgToolsPath);
		
		if (string.IsNullOrEmpty(pgRestorePath)) {
			_logger.WriteError("pg_restore not found. Please install PostgreSQL client tools and ensure they are in PATH, or specify pgToolsPath in configuration.");
			_logger.WriteInfo("Download PostgreSQL from: https://www.postgresql.org/download/");
			if (!string.IsNullOrEmpty(config.PgToolsPath)) {
				_logger.WriteError($"pg_restore not found at specified path: {config.PgToolsPath}");
			}
			return 1;
		}

		_logger.WriteInfo($"Using pg_restore from: {pgRestorePath}");

		try {
			Postgres postgres = _dbClientFactory.CreatePostgres(config.Hostname, config.Port, config.Username, config.Password);
			
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
			if (Program.IsDebugMode) {
				_logger.WriteInfo("This may take several minutes depending on database size. Detailed progress will be shown below:");
			} else {
				_logger.WriteInfo("This may take several minutes depending on database size. Run with --debug flag to see detailed progress.");
			}

			int exitCode = ExecutePgRestoreCommand(pgRestorePath, config, backupPath, dbName);
			
			if (exitCode == 0) {
				_logger.WriteInfo($"Successfully restored database {dbName} from {backupPath}");
				return 0;
			} else {
				_logger.WriteError($"pg_restore failed with exit code {exitCode}");
				return 1;
			}
		} catch (Exception ex) {
			_logger.WriteError($"Error restoring PostgreSQL database: {ex.Message}");
			return 1;
		}
	}

	private int ExecutePgRestoreCommand(string pgRestorePath, LocalDbServerConfiguration config, string backupPath, string dbName) {
		var processInfo = new ProcessStartInfo {
			FileName = pgRestorePath,
			Arguments = $"-h {config.Hostname} -p {config.Port} -U {config.Username} -d {dbName} -v \"{backupPath}\" --no-owner --no-privileges",
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
		} else {
			System.DateTime lastProgressMessage = System.DateTime.Now;
			process.OutputDataReceived += (sender, e) => {
				if (!string.IsNullOrEmpty(e.Data)) {
					if ((System.DateTime.Now - lastProgressMessage).TotalSeconds >= 30) {
						_logger.WriteInfo("Restore in progress...");
						lastProgressMessage = System.DateTime.Now;
					}
				}
			};

			process.ErrorDataReceived += (sender, e) => {
				// Consume error stream silently to prevent blocking
			};
		}
		
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();

		return process.ExitCode;
	}

	private int HandleUnsupportedDbType(string dbType) {
		_logger.WriteError($"Database type '{dbType}' is not supported. Supported types: mssql, postgres");
		return 1;
	}
	
	
	#endregion

}

#endregion

