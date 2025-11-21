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
	bool DropDb(string dbName);
} 

public class Postgres : IPostgres
{

	private string _connectionString;
	private readonly ILogger _logger = ConsoleLogger.Instance;

	public Postgres(){ }
	
	public Postgres(int port, string username, string password) {
		_connectionString = $"Host={BindingsModule.k8sDns};Port={port};Username={username};Password={password};Database=postgres";
	}
	
	public void Init(string host, int port, string username, string password){
		_connectionString = $"Host={host};Port={port};Username={username};Password={password};Database=postgres";
	}
	
	public bool CreateDbFromTemplate (string templateName, string dbName) {
		_logger.WriteInfo($"Creating database '{dbName}' from template '{templateName}'");
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
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public bool CreateDb (string dbName) {
		
		try {
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"CREATE DATABASE \"{dbName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public bool SetDatabaseAsTemplate( string dbName) {
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
			_logger.WriteError(ne.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public bool CheckTemplateExists (string templateName) {
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datistemplate = true AND datName = '{templateName}';
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			var result = cmd.ExecuteScalar();
			cnn.Close();
			return result is long and 1;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public bool CheckDbExists (string templateName) {
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datName = '{templateName}';
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			var result = cmd.ExecuteScalar();
			cnn.Close();
			return result is long and 1;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public bool DropDb(string dbName){
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
			_logger.WriteError(ne.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
}
