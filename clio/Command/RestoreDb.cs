using System;
using System.Globalization;
using System.IO;

using Clio.Common;
using Clio.Common.db;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

[Verb("restore-db", Aliases = new string[] { "rdb" }, HelpText = "Restores database from backup file")]
public class RestoreDbCommandOptions : EnvironmentOptions
{
}

public class RestoreDbCommand(ILogger logger, IFileSystem fileSystem, IDbClientFactory dbClientFactory,
    ISettingsRepository settingsRepository): Command<RestoreDbCommandOptions>
{
    private readonly ILogger _logger = logger;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IDbClientFactory _dbClientFactory = dbClientFactory;
    private readonly ISettingsRepository _settingsRepository = settingsRepository;

    public override int Execute(RestoreDbCommandOptions options)
    {
        EnvironmentSettings env = _settingsRepository.GetEnvironment(options);

        int result = env.DbServer.Uri.Scheme switch
        {
            "mssql" => RestoreMs(env.DbServer, env.DbName, options.Force, env.BackupFilePath),
            _ => HandleIncorrectUri(options.Uri)
        };
        _logger.WriteLine("Done");
        return result;
    }

    private int HandleIncorrectUri(string uri)
    {
        _logger.WriteError(
            $"Scheme {uri} is not supported.\r\n\tExample: mssql://user:pass@127.0.01:1433 or\r\n\tpgsql://user:pass@127.0.01:5432");
        return 1;
    }

    private int RestoreMs(DbServer dbServer, string dbName, bool force, string backUpFilePath)
    {
        Credentials credentials = dbServer.GetCredentials();
        IMssql mssql = _dbClientFactory.CreateMssql(dbServer.Uri.Host, dbServer.Uri.Port, credentials.Username,
            credentials.Password);
        if (mssql.CheckDbExists(dbName))
        {
            bool shouldDrop = force;
            if (!shouldDrop)
            {
                _logger.WriteWarning($"Database {dbName} already exists, would you like to keep it ? (Y / N)");
                string? newName = Console.ReadLine();
                shouldDrop = newName.ToUpper(CultureInfo.InvariantCulture) == "N";
            }

            if (!shouldDrop)
            {
                string backupDbName = $"{dbName}_{DateTime.Now:yyyyMMddHHmmss}";
                mssql.RenameDb(dbName, backupDbName);
                _logger.WriteInfo($"Renamed existing database from {dbName} to {backupDbName}");
            }
            else
            {
                mssql.DropDb(dbName);
                _logger.WriteInfo($"Dropped existing database {dbName}");
            }
        }

        _fileSystem.CopyFiles(new[] { backUpFilePath }, dbServer.WorkingFolder, true);
        _logger.WriteInfo(
            $"Copied backup file to server \r\n\tfrom: {backUpFilePath} \r\n\tto  : {dbServer.WorkingFolder}");

        _logger.WriteInfo("Started db restore...");
        int result = mssql.CreateDb(dbName, Path.GetFileName(backUpFilePath)) ? 0 : 1;
        _logger.WriteInfo($"Created database {dbName} from file {backUpFilePath}");
        return result;
    }
}
