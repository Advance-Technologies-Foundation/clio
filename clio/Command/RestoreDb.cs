using System;
using System.Globalization;
using System.IO;
using Clio.Common;
using Clio.Common.db;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

#region Class: RestoreDbCommandOptions

[Verb("RestoreDb", Aliases = new string[] { }, HelpText = "Restores database from backup file")]
public class RestoreDbCommandOptions : EnvironmentOptions
{

	// clio-dev RestoreDb -n mydb10 -f "D:\Projects\CreatioProductBuild\8.1.2.2482_Studio_Softkey_MSSQL_ENU\db\BPMonline812Studio.bak" -d "\\wsl.localhost\rancher-desktop\mnt\clio-infrastructure\mssql\data" -u "mssql://sa:$Zarelon01$Zarelon01@localhost:1433" --force  
	#region Properties: Public
	
	[Option("force", Required = false, HelpText = "Force restore")]
	public bool Force { get; set; }
	
	
	#endregion

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

	#endregion

	#region Constructors: Public

	public RestoreDbCommand(ILogger logger, IFileSystem fileSystem, IDbClientFactory dbClientFactory, ISettingsRepository settingsRepository) {
		_logger = logger;
		_fileSystem = fileSystem;
		_dbClientFactory = dbClientFactory;
		_settingsRepository = settingsRepository;
	}

	#endregion

	#region Methods: Public

	public override int Execute(RestoreDbCommandOptions options) {
		EnvironmentSettings env = _settingsRepository.GetEnvironment(options.Environment);
		
		return env.DbServer.Uri switch {
			env.DbServer.Uri.Scheme == "mssql" => RestoreMs(env.DbServer.Uri, env.DbName, options.Force, env.BackupFilePath, env.FolderVisibleToServer, env.Login, env.Password),
			env.DbServer.Uri.Scheme == "pgsql" => RestoreMs(env.DbServer.Uri, options),
			var _ => HandleIncorrectUri(options.Uri)
		};
	}

	private int HandleIncorrectUri(string uri){
		_logger.WriteError($"Scheme {uri} is not supported.\r\n\tExample: mssql://user:pass@127.0.01:1433 or\r\n\tpgsql://user:pass@127.0.01:5432");
		return 1;
	}
	private int RestoreMs(Uri dbUri, string dbName, bool force, string backUpFilePath, string folderVisibleToServer, string dbLogin, string dbPassword){
		Credentials credentials  = GetCredentials(dbUri, dbLogin, dbPassword);
		IMssql mssql = _dbClientFactory.CreateMssql(dbUri.Host, dbUri.Port, credentials.Username, credentials.Password);
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
		_fileSystem.CopyFiles(new[]{backUpFilePath}, folderVisibleToServer, true);
		var result =  mssql.CreateDb(dbName, Path.GetFileName(backUpFilePath)) ? 0 : 1;
		_logger.WriteInfo($"Created database {dbName}");
		return result;
	}
	private int RestorePg(Uri uri, RestoreDbCommandOptions options){
		throw new NotImplementedException("Not implemented yet;");
		// var credentials  = GetCredentials(uri, options);
		return 0;
	}

	private Credentials GetCredentials(Uri uri, string login, string password) =>
		uri.UserInfo.Split(':') switch {
			var credentials when credentials.Length == 2 => new Credentials(credentials[0], credentials[1]),
			var _ => new Credentials(login, password)
		};
	
	#endregion

	private record Credentials(string Username, string Password);
}

#endregion

