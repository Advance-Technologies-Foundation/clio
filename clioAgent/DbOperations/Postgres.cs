using System.Data.Common;
using System.Globalization;
using clioAgent.Handlers.ChainLinks;
using ErrorOr;
using Npgsql;

namespace clioAgent.DbOperations;

public interface IInitializedPostgres : IDisposable {

	#region Methods: Public

	Task<bool> CheckDbExistsAsync(string templateName);

	Task<bool> CheckTemplateExistsAsync(string templateName);

	Task<bool> CreateDbAsync(string dbName);

	Task<bool> CreateDbFromTemplateAsync(string templateName, string dbName, bool force = false);

	Task<bool> DropDbAsync(string dbName);

	Task<string> FindDbByCommentAsync(string comment);

	Task<string> FindTemplateDbByCommentAsync(string comment);

	Task SetCommentOnDbAsync(string dbName, string comment);

	Task<bool> SetDatabaseAsTemplateAsync(string dbName, string templateFor);

	public DbSettings DbSettings { get; }
	#endregion

}

public interface IPostgres {

	#region Properties: Public

	bool IsInitialized { get; set; }

	#endregion

	#region Methods: Public

	ErrorOr<IInitializedPostgres> Init();

	#endregion

}

public class Postgres(Settings settings, ILogger<IPostgres> logger) : IPostgres, IInitializedPostgres, IAsyncDisposable {

	private readonly ILogger<IPostgres> _logger = logger;

	#region Fields: Private

	private NpgsqlDataSource? _dataSource;

	#endregion

	#region Properties: Public

	public bool IsInitialized { get; set; }

	#endregion

	#region Methods: Private

	private static ErrorOr<DbSettings> ValidateConnectionString(string connectionString){
		DbConnectionStringBuilder dbConnectionStringBuilder = new() {
			ConnectionString = connectionString
		};

		bool isCsServerObj = dbConnectionStringBuilder.TryGetValue("Server", out object? csServerObj);
		if (!isCsServerObj || csServerObj == null) {
			return Error.Failure("RestoreDb.ServerNotFound", "Server not found in connection string");
		}
		string? csServer = csServerObj.ToString()!;
		if (string.IsNullOrWhiteSpace(csServerObj.ToString())) {
			return Error.Failure("RestoreDb.ServerNotValid", "Server is not valid");
		}

		bool isCsPortObj = dbConnectionStringBuilder.TryGetValue("Port", out object? csPortObj);
		if (!isCsPortObj || csPortObj == null) {
			return Error.Failure("RestoreDb.PortNotFound", "Port not found in connection string");
		}
		bool isPort = int.TryParse(csPortObj.ToString(), CultureInfo.InvariantCulture, out int port);
		if (!isPort) {
			return Error.Failure("RestoreDb.PortNotValid", "Port is not valid");
		}

		bool isCsPasswordObj = dbConnectionStringBuilder.TryGetValue("password", out object? csPasswordObj);
		if (!isCsPasswordObj || csPasswordObj == null) {
			return Error.Failure("RestoreDb.PasswordNotFound", "Password not found in connection string");
		}
		string? csPassword = csPasswordObj.ToString();
		if (string.IsNullOrWhiteSpace(csPassword)) {
			return Error.Failure("RestoreDb.PasswordNotValid", "Password is not valid");
		}

		bool isCsUserIdObj = dbConnectionStringBuilder.TryGetValue("User ID", out object? csUserIdObj);
		if (!isCsUserIdObj || csUserIdObj == null) {
			return Error.Failure("RestoreDb.UserIdNotFound", "User ID not found in connection string");
		}
		string? csUserId = csUserIdObj.ToString();
		if (string.IsNullOrWhiteSpace(csUserId)) {
			return Error.Failure("RestoreDb.UserIdNotValid", "User ID is not valid");
		}
		return new DbSettings(csServer, port, csPassword, csUserId);
	}

	#endregion

	#region Methods: Public

	public async Task<bool> CheckDbExistsAsync(string templateName){
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datName = '{templateName}';
			";

			await using NpgsqlConnection cnn = await _dataSource!.OpenConnectionAsync();
			await using NpgsqlCommand cmd = _dataSource.CreateCommand(sqlText);
			object? result = await cmd.ExecuteScalarAsync();
			await cnn.CloseAsync();
			return result is long and 1;
		}
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
			return false;
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
			return false;
		}
	}

	public async Task<bool> CheckTemplateExistsAsync(string templateName){
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datistemplate = true AND datName = '{templateName}';
			";

			await using NpgsqlConnection cnn = await _dataSource!.OpenConnectionAsync();
			await using NpgsqlCommand cmd = _dataSource.CreateCommand(sqlText);
			object? result = await cmd.ExecuteScalarAsync();
			await cnn.CloseAsync();

			return result is long and 1;
		}
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
			return false;
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
			return false;
		}
	}

	public async Task<bool> CreateDbAsync(string dbName){
		try {
			await using NpgsqlConnection cnn = await _dataSource!.OpenConnectionAsync();
			await using NpgsqlCommand cmd
				= _dataSource.CreateCommand($"CREATE DATABASE \"{dbName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			await cmd.ExecuteNonQueryAsync();
			await cnn.CloseAsync();
			return true;
		}
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
			return false;
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
			return false;
		}
	}

	
	public async Task<bool> CheckDbExists(string dbName){
		
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datName = '{dbName}';
			";

			await using NpgsqlConnection cnn = await _dataSource!.OpenConnectionAsync();
			await using NpgsqlCommand cmd = _dataSource.CreateCommand(sqlText);
			object? result = await cmd.ExecuteScalarAsync();
			await cnn.CloseAsync();
			return result is long and 1;
		}
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
			return false;
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
			return false;
		}
	}
	
	public async Task<bool> CreateDbFromTemplateAsync(string templateName, string dbName, bool force = false){
		try {
			await using NpgsqlConnection cnn = await _dataSource!.OpenConnectionAsync();
			string killSqlConnections = @$"
			SELECT pg_terminate_backend(pg_stat_activity.pid)
			FROM pg_stat_activity
			WHERE pg_stat_activity.datname = '{templateName}'
			";
			await using NpgsqlCommand killConnectionCmd = _dataSource.CreateCommand(killSqlConnections);
			await killConnectionCmd.ExecuteNonQueryAsync();
			var exists = await CheckDbExists(dbName);
			if(exists && !force) {
				await Console.Error.WriteLineAsync($"Template database {templateName} already exists");
				return false;
			} 
			
			if (exists && force) {
				await DropDbAsync(dbName);
			}

			await using NpgsqlCommand cmd = _dataSource.CreateCommand(
				$"CREATE DATABASE \"{dbName}\" TEMPLATE=\"{templateName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			await cmd.ExecuteNonQueryAsync();
			await cnn.CloseAsync();
			return true;
		}
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
			return false;
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
			return false;
		}
	}

	public void Dispose(){
		if (_dataSource is null) {
			return;
		}
		_dataSource.Dispose();
	}

	public async ValueTask DisposeAsync(){
		if (_dataSource is not null) {
			await _dataSource.DisposeAsync();
		}
	}

	public async Task<bool> DropDbAsync(string dbName){
		try {
			string killSqlConnections = @$"
			SELECT pg_terminate_backend(pg_stat_activity.pid)
			FROM pg_stat_activity
			WHERE pg_stat_activity.datname = '{dbName}'
			";

			await using NpgsqlConnection cnn = await _dataSource!.OpenConnectionAsync();
			await using NpgsqlCommand killConnectionCmd = _dataSource.CreateCommand(killSqlConnections);
			await killConnectionCmd.ExecuteNonQueryAsync();
			await using NpgsqlCommand cmd = _dataSource.CreateCommand($"DROP DATABASE IF EXISTS \"{dbName}\";");
			await cmd.ExecuteNonQueryAsync();
			await cnn.CloseAsync();
			return true;
		}
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
			return false;
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
			return false;
		}
	}

	public async Task<string> FindDbByCommentAsync(string comment){
		try {
			string sqlText = @$"
			SELECT description 
			FROM pg_shdescription
			JOIN pg_database on objoid = pg_database.oid 
			WHERE datname = '{comment}'
			AND pg_database.datistemplate = false
			";

			await using NpgsqlConnection cnn = await _dataSource!.OpenConnectionAsync();
			await using NpgsqlCommand cmd = _dataSource.CreateCommand(sqlText);
			object? result = await cmd.ExecuteScalarAsync();
			await cnn.CloseAsync();
			return result?.ToString() ?? string.Empty;
		}
		
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
			return string.Empty;
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
			return string.Empty;
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
			return string.Empty;
		}
	}

	public async Task<string> FindTemplateDbByCommentAsync(string comment){
		try {
			string sqlText = @$"
				SELECT datname 
				FROM pg_shdescription
				JOIN pg_database on objoid = pg_database.oid 
				WHERE description = '{comment}' 
				AND pg_database.datistemplate = true
			";

			await using NpgsqlConnection cnn = await _dataSource!.OpenConnectionAsync();
			await using NpgsqlCommand cmd = _dataSource.CreateCommand(sqlText);
			object? result = await cmd.ExecuteScalarAsync();
			await cnn.CloseAsync();
			return result?.ToString() ?? string.Empty;
		}
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
			return string.Empty;
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
			return string.Empty;
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
			return string.Empty;
		}
	}

	public ErrorOr<IInitializedPostgres> Init(){
		Db? dbSection = settings.Db?.FirstOrDefault(db => db.Type == "PGSQL");
		if (dbSection is null) {
			return Error.Failure("RestoreDb.DbSettingsNotFound", "Db settings not found");
		}

		string cs = dbSection.Servers?.FirstOrDefault()?.ConnectionString ?? string.Empty;
		if (string.IsNullOrEmpty(cs)) {
			return Error.Failure("RestoreDb.ConnectionStringNotFound", "Connection string not found");
		}

		ErrorOr<DbSettings> maybeDbSettings = ValidateConnectionString(cs);
		if (maybeDbSettings.IsError) {
			return maybeDbSettings.Errors;
		}
		DbSettings = maybeDbSettings.Value;
		
		NpgsqlDataSourceBuilder dataSourceBuilder = new() {
			ConnectionStringBuilder = {
				Pooling = true,
				CommandTimeout = 600,
				Host = DbSettings.Server,
				Port = DbSettings.Port,
				Username = DbSettings.UserId,
				Password = DbSettings.Password,
				Database = "postgres"
			}
		};
		_dataSource = dataSourceBuilder.Build();
		IsInitialized = true;
		return this;
	}

	public async Task SetCommentOnDbAsync(string dbName, string comment){
		try {
			await using NpgsqlConnection cnn = _dataSource!.OpenConnection();
			await using NpgsqlCommand cmd
				= _dataSource.CreateCommand($"COMMENT ON DATABASE \"{dbName}\" IS '{comment}'");
			await cmd.ExecuteNonQueryAsync();
			await cnn.CloseAsync();
		}
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
		}
	}

	public async Task<bool> SetDatabaseAsTemplateAsync(string dbName, string templateFor){
		try {
			await using NpgsqlConnection cnn = await _dataSource!.OpenConnectionAsync();
			await using NpgsqlCommand cmd
				= _dataSource.CreateCommand($"UPDATE pg_database SET datistemplate='true' WHERE datname='{dbName}'");
			await cmd.ExecuteNonQueryAsync();
			await cnn.CloseAsync();
			await SetCommentOnDbAsync(dbName, templateFor);
			return true;
		}
		catch (Exception e) when (e is PostgresException pe) {
			_logger.LogError(pe, $"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch (Exception e) when (e is NpgsqlException ne) {
			_logger.LogError(ne, ne.Message);
			return false;
		}
		catch (Exception e) {
			_logger.LogError(e, e.Message);
			return false;
		}
	}

	public DbSettings DbSettings { get; private set; }

	#endregion

}
