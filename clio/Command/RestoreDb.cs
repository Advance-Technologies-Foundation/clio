using System;
using System.Globalization;
using System.IO;
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

	#endregion

	#region Constructors: Public

	public RestoreDbCommand(ILogger logger, IFileSystem fileSystem, IDbClientFactory dbClientFactory, 
		ISettingsRepository settingsRepository, ICreatioInstallerService creatioInstallerService) {
		_logger = logger;
		_fileSystem = fileSystem;
		_dbClientFactory = dbClientFactory;
		_settingsRepository = settingsRepository;
		_creatioInstallerService = creatioInstallerService;
	}

	#endregion

	#region Methods: Public

	public override int Execute(RestoreDbCommandOptions options) {

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
		return _creatioInstallerService.DoPgWork(directoryInfo, dbName);
	}

	
	#endregion

}

#endregion

