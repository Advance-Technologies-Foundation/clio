using System;
using System.IO;
using Microsoft.Data.SqlClient;
using NRedisStack;

namespace Clio.Common.db;

public interface IMssql
{
	void Init(string host, int port, string username, string password);
	
	bool CreateDb (string dbName, string backupFileName);

	bool CheckDbExists(string dbName);

	void RenameDb(string optionsDbName, string s);

	void DropDb(string optionsDbName);

}

public class Mssql : IMssql
{
	
	private SqlConnectionStringBuilder _builder;

	public Mssql(){}
	public void Init(string host, int port, string username, string password){
		_builder = new SqlConnectionStringBuilder {
			DataSource = $"{host},{port}",
			UserID = username,
			Password = password,
			InitialCatalog = "master",
			Encrypt = false
		};
	}
	
	public Mssql(string host, int port, string username, string password) {
		_builder = new SqlConnectionStringBuilder {
			DataSource = $"{host},{port}",
			UserID = username,
			Password = password,
			InitialCatalog = "master",
			Encrypt = false
		};
	}
	
	public Mssql(int port, string username, string password) {
		_builder = new SqlConnectionStringBuilder {
			DataSource = $"127.0.0.1,{port}",
			UserID = username,
			Password = password,
			InitialCatalog = "master",
			Encrypt = false
		};
	}
	
	public bool CreateDb (string dbName, string backupFileName) {
		
		try
		{
			using SqlConnection connection = new (_builder.ConnectionString);
			connection.Open();
			
			string ldf = $"{dbName}-{DateTime.Now:yyyy-MMM-dd-HHmmss}.ldf";
			string mdf = $"{dbName}-{DateTime.Now:yyyy-MMM-dd-HHmmss}.mdf";
			
			DefaultPaths defaultPaths = GetInstanceDefaultPaths(connection, false);
			
			string sqlText = $@"

			USE [master]
			RESTORE DATABASE [{dbName}] 
			FROM  DISK = N'{defaultPaths.dataPath}{backupFileName}' 
			WITH  FILE = 1,  
			MOVE N'TSOnline_Data' 
			TO N'{defaultPaths.dataPath}{mdf}',  
			MOVE N'TSOnline_Log' TO N'{defaultPaths.logPath}{ldf}',  
			NOUNLOAD,  STATS = 5
			";
			
			SqlCommand cmd = new (sqlText, connection) {
				CommandTimeout = 600
			};
			int result = cmd.ExecuteNonQuery();
			connection.Close();
			return true;
		}
		catch (SqlException e)
		{
			Console.WriteLine(e.ToString());
			return false;
		}
	}

	public bool CheckDbExists(string dbName) {
		try
		{
			using SqlConnection connection = new SqlConnection(_builder.ConnectionString);
			connection.Open();
			
			string sqlText = $@"
			SELECT 
				count(name) as [Count] 
			FROM 
				sys.databases WHERE name = '{dbName}'
			";
			
			var cmd = new SqlCommand(sqlText, connection);
			var result = cmd.ExecuteScalar();
			connection.Close();
			return int.Parse(result.ToString()) == 1;
		}
		catch (SqlException e)
		{
			Console.WriteLine(e.ToString());
			return false;
		}
	}

	public void RenameDb(string from, string to){
		
		using SqlConnection connection = new SqlConnection(_builder.ConnectionString);
		connection.Open();
		var sqlText = $"""
									USE master;
									ALTER DATABASE [{from}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
									ALTER DATABASE [{from}] MODIFY NAME = [{to}];
									ALTER DATABASE [{to}] SET MULTI_USER;
								
						""";
		var cmd = new SqlCommand(sqlText, connection);
		cmd.ExecuteNonQuery();
		connection.Close();
	}

	public void DropDb(string optionsDbName){
		using SqlConnection connection = new SqlConnection(_builder.ConnectionString);
		connection.Open();
		var sqlText = $"""
						
									USE [master]
									ALTER DATABASE [{optionsDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
									DROP DATABASE [{optionsDbName}];
								
						""";
		var cmd = new SqlCommand(sqlText, connection);
		cmd.ExecuteNonQuery();
		connection.Close();
	}

	private DefaultPaths GetInstanceDefaultPaths(bool closeConnection){
		using SqlConnection connection = new (_builder.ConnectionString);
		connection.Open();
		return GetInstanceDefaultPaths(connection, closeConnection);
	}
	private DefaultPaths GetInstanceDefaultPaths(SqlConnection connection, bool closeConnection){
		string sqlText = $"""
						
								USE [master]
								SELECT
									SERVERPROPERTY('InstanceDefaultDataPath') AS DefaultDataPath,
									SERVERPROPERTY('InstanceDefaultLogPath') AS DefaultLogPath;
								
						""";
		var cmd = new SqlCommand(sqlText, connection);
		using SqlDataReader reader = cmd.ExecuteReader();
		
		string dataPath = string.Empty; 
		string logPath = string.Empty;
		while(reader.Read()) {
			dataPath = reader[0].ToString();
			logPath = reader[1].ToString();
		}
		if(closeConnection) {
			connection.Close();
		}
		return new DefaultPaths(dataPath, logPath);
	}
	
	record DefaultPaths(string dataPath, string logPath);
}