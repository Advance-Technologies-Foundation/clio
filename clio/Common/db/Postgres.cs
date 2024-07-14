using System;
using Npgsql;

namespace Clio.Common.db;

public class Postgres
{

	private readonly string _connectionString;

	public Postgres(int port, string username, string password) {
		_connectionString = $"Host=127.0.0.1;Port={port};Username={username};Password={password};Database=postgres";
	}
	
	public bool CreateDbFromTemplate (string templateName, string dbName) {
		try {
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
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
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
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
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
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
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
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
			return false;
		}
	}
	
	public bool DropDbByName(string dbName){
		try {
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"DROP DATABASE IF EXISTS \"{dbName}\";");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			Console.WriteLine($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			Console.WriteLine(ne.Message);
			return false;
		}
		catch(Exception e) {
			Console.WriteLine(e.Message);
			return false;
		}
	}
}