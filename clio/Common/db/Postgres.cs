using System;
using Npgsql;

namespace Clio.Common.db;


public interface IPostgres
{
	void Init(string host, int port, string username, string password);
	bool CreateDbFromTemplate(string templateName, string dbName);
	bool CreateDb(string dbName);
	bool SetDatabaseAsTemplate(string dbName);
	bool CheckTemplateExists(string templateName);
	bool CheckDbExists(string dbName);
	bool DropDb(string dbName);
	bool SetDatabaseComment(string dbName, string comment);
	string GetDatabaseComment(string dbName);
	string FindTemplateBySourceFile(string sourceFileName);
}

public class Postgres : IPostgres
{

	private string _connectionString;
	private readonly ILogger _logger;

	public Postgres() {
		_logger = ConsoleLogger.Instance;
	}

	public Postgres(ILogger logger) {
		_logger = logger ?? ConsoleLogger.Instance;
	}
	
	public Postgres(int port, string username, string password, ILogger logger = null) {
		_connectionString = $"Host={BindingsModule.k8sDns};Port={port};Username={username};Password={password};Database=postgres";
		_logger = logger ?? ConsoleLogger.Instance;
	}
	
	public void Init(string host, int port, string username, string password)
	{
		_connectionString = $"Host={host};Port={port};Username={username};Password={password};Database=postgres";
	}
	
	public virtual bool CreateDbFromTemplate (string templateName, string dbName) {
		//_logger.WriteInfo($"Creating database '{dbName}' from template '{templateName}'");
		bool dbExists = CheckDbExists(dbName);
		_logger.WriteInfo($"Database '{dbName}' exists: {dbExists}");
		if (dbExists) {
			_logger.WriteWarning($"Dropping existing database '{dbName}'");
			DropDb(dbName);
			_logger.WriteWarning($"Dropped existing database '{dbName}'");
		}
		try {
			_logger.WriteInfo($"Creating database '{dbName}' from template '{templateName}'");
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			
			string killSqlConnections = @$"
			SELECT pg_terminate_backend(pg_stat_activity.pid)
			FROM pg_stat_activity
			WHERE pg_stat_activity.datname = '{templateName}'
			";
			using NpgsqlCommand killConnectionCmd = dataSource.CreateCommand(killSqlConnections);
			killConnectionCmd.ExecuteNonQuery();
			
			using NpgsqlCommand cmd = dataSource.CreateCommand($"CREATE DATABASE \"{dbName}\" TEMPLATE=\"{templateName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			cmd.CommandTimeout = 600; // 10 minutes
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool CreateDb (string dbName) {
		
		try {
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"CREATE DATABASE \"{dbName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false; // 3 minutes should be enough time to restore from template
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool SetDatabaseAsTemplate( string dbName) {
		try {
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"UPDATE pg_database SET datistemplate='true' WHERE datname='{dbName}'");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool CheckTemplateExists (string templateName) {
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datistemplate = true AND datName = '{templateName}';
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			object result = cmd.ExecuteScalar();
			cnn.Close();
			return result is long and 1;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool CheckDbExists (string templateName) {
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datName = '{templateName}';
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			object result = cmd.ExecuteScalar();
			cnn.Close();
			return result is long and 1;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool DropDb(string dbName){
		try {
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			
			string killSqlConnections = @$"
			SELECT pg_terminate_backend(pg_stat_activity.pid)
			FROM pg_stat_activity
			WHERE pg_stat_activity.datname = '{dbName}'
			";
			using NpgsqlCommand killConnectionCmd = dataSource.CreateCommand(killSqlConnections);
			killConnectionCmd.ExecuteNonQuery();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"DROP DATABASE IF EXISTS \"{dbName}\";");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool SetDatabaseComment(string dbName, string comment){
		try {
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			string escapedComment = comment.Replace("'", "''");
			using NpgsqlCommand cmd = dataSource.CreateCommand($"COMMENT ON DATABASE \"{dbName}\" IS '{escapedComment}'");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual string GetDatabaseComment(string dbName){
		try {
			string sqlText = @$"
				SELECT obj_description(oid, 'pg_database') 
				FROM pg_database 
				WHERE datname = '{dbName}'
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			object result = cmd.ExecuteScalar();
			cnn.Close();
			return result?.ToString();
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return null;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return null;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return null;
		}
	}
	
	public virtual string FindTemplateBySourceFile(string sourceFileName){
		try {
			string sqlText = @$"
				SELECT datname 
				FROM pg_database 
				WHERE datistemplate = true 
				  AND shobj_description(oid, 'pg_database') LIKE '%sourceFile:{sourceFileName}%'
				LIMIT 1
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			object result = cmd.ExecuteScalar();
			cnn.Close();
			
			if (result != null && result != DBNull.Value) {
				return result.ToString();
			}
			
			// Backward compatibility: try old naming pattern
			string oldStyleTemplateName = $"template_{sourceFileName}";
			if (CheckTemplateExists(oldStyleTemplateName)) {
				return oldStyleTemplateName;
			}
			
			return null;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return null;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return null;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return null;
		}
	}
}
