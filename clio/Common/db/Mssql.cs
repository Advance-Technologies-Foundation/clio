using System;
using System.IO;
using Microsoft.Data.SqlClient;

namespace Clio.Common.db;

public class Mssql
{
	
	private readonly SqlConnectionStringBuilder _builder;

	public Mssql(int port, string username, string password) {
		_builder = new SqlConnectionStringBuilder {
			DataSource = $"localhost,{port}",
			UserID = username,
			Password = password,
			InitialCatalog = "master",
			Encrypt = false
		};
	}
	
	public bool CreateDb (string dbName, string backupFileName) {
		
		try
		{
			using SqlConnection connection = new SqlConnection(_builder.ConnectionString);
			connection.Open();
			
			string ldf = Path.GetFileNameWithoutExtension(backupFileName) + ".ldf";
			string mdf = Path.GetFileNameWithoutExtension(backupFileName) + ".mdf";
			
			string sqlText = $@"

			USE [master]
			RESTORE DATABASE [{dbName}] 
			FROM  DISK = N'/var/opt/mssql/data/{backupFileName}' 
			WITH  FILE = 1,  
			MOVE N'TSOnline_Data' 
			TO N'/var/opt/mssql/data/{mdf}',  
			MOVE N'TSOnline_Log' TO N'/var/opt/mssql/data/{ldf}',  
			NOUNLOAD,  STATS = 5
			";
			
			var cmd = new SqlCommand(sqlText, connection);
			var result = cmd.ExecuteNonQuery();
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
	
	
}