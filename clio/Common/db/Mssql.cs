using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.SqlClient;

namespace Clio.Common.db;

/// <summary>
/// Result of a SQL Server restore operation.
/// </summary>
/// <param name="Success">Whether the restore operation completed successfully.</param>
/// <param name="Messages">Progress or diagnostic messages emitted during restore.</param>
public sealed record DatabaseRestoreResult(bool Success, IReadOnlyList<string> Messages);

/// <summary>
/// Provides SQL Server database operations used by clio commands.
/// </summary>
public interface IMssql {
	/// <summary>
	/// Initializes the client with SQL Server connection settings.
	/// </summary>
	void Init(string host, int port, string username, string password, bool isWindowsAuth = false);

	/// <summary>
	/// Restores a database from a backup file and returns whether it succeeded.
	/// </summary>
	bool CreateDb(string dbName, string backupFileName);

	/// <summary>
	/// Restores a database from a backup file and captures emitted database messages.
	/// </summary>
	DatabaseRestoreResult RestoreDatabase(string dbName, string backupFileName, Action<string> onMessage = null);

	/// <summary>
	/// Determines whether the target database already exists.
	/// </summary>
	bool CheckDbExists(string dbName);

	/// <summary>
	/// Renames an existing database.
	/// </summary>
	void RenameDb(string optionsDbName, string s);

	/// <summary>
	/// Drops an existing database.
	/// </summary>
	void DropDb(string optionsDbName);

	/// <summary>
	/// Tests whether a connection to SQL Server can be established.
	/// </summary>
	bool TestConnection();

	/// <summary>
	/// Gets the default SQL Server data path.
	/// </summary>
	string GetDataPath();
}

/// <summary>
/// Default SQL Server database client implementation.
/// </summary>
public class Mssql : IMssql {
	private SqlConnectionStringBuilder _builder;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="Mssql"/> class.
	/// </summary>
	public Mssql(ILogger logger = null) {
		_logger = logger ?? NullLogger.Instance;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Mssql"/> class with explicit connection settings.
	/// </summary>
	public Mssql(string host, int port, string username, string password, bool isWindowsAuth = false) : this((ILogger)null) {
		_builder = new SqlConnectionStringBuilder {
			DataSource = host.Contains("\\") || port == 0 ? host : $"{host},{port}",
			InitialCatalog = "master",
			Encrypt = false,
			IntegratedSecurity = isWindowsAuth
		};
		if (!isWindowsAuth) {
			_builder.UserID = username;
			_builder.Password = password;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Mssql"/> class for the Kubernetes SQL Server endpoint.
	/// </summary>
	public Mssql(int port, string username, string password) : this((ILogger)null) {
		_builder = new SqlConnectionStringBuilder {
			DataSource = $"{BindingsModule.k8sDns},{port}",
			UserID = username,
			Password = password,
			InitialCatalog = "master",
			Encrypt = false
		};
	}

	/// <inheritdoc />
	public void Init(string host, int port, string username, string password, bool isWindowsAuth = false) {
		_builder = new SqlConnectionStringBuilder {
			DataSource = !isWindowsAuth ? $"{host},{port}" : host,
			InitialCatalog = "master",
			Encrypt = false,
			IntegratedSecurity = isWindowsAuth
		};
		if (!isWindowsAuth) {
			_builder.UserID = username;
			_builder.Password = password;
		}
	}

	/// <inheritdoc />
	public bool CreateDb(string dbName, string backupFileName) {
		return RestoreDatabase(dbName, backupFileName).Success;
	}

	/// <inheritdoc />
	public DatabaseRestoreResult RestoreDatabase(string dbName, string backupFileName, Action<string> onMessage = null) {
		List<string> messages = [];

		void Emit(string message) {
			if (string.IsNullOrWhiteSpace(message)) {
				return;
			}

			messages.Add(message);
			onMessage?.Invoke(message);
		}

		try {
			using SqlConnection connection = new(_builder.ConnectionString) {
				FireInfoMessageEventOnUserErrors = true
			};
			connection.InfoMessage += (_, args) => {
				foreach (SqlError error in args.Errors) {
					Emit(error.Message);
				}
			};
			connection.Open();

			string ldf = $"{dbName}-{DateTime.Now:yyyy-MMM-dd-HHmmss}.ldf";
			string mdf = $"{dbName}-{DateTime.Now:yyyy-MMM-dd-HHmmss}.mdf";
			DefaultPaths defaultPaths = GetInstanceDefaultPaths(connection, closeConnection: false);

			string sqlText = $@"

			USE [master]
			RESTORE DATABASE [{dbName}] 
			FROM  DISK = N'{defaultPaths.DataPath}{backupFileName}' 
			WITH  FILE = 1,  
			MOVE N'TSOnline_Data' 
			TO N'{defaultPaths.DataPath}{mdf}',  
			MOVE N'TSOnline_Log' TO N'{defaultPaths.LogPath}{ldf}',  
			NOUNLOAD,  STATS = 5
			";

			SqlCommand cmd = new(sqlText, connection) {
				CommandTimeout = 600
			};
			cmd.ExecuteNonQuery();
			connection.Close();
			return new DatabaseRestoreResult(true, messages);
		}
		catch (SqlException e) {
			Emit(e.Message);
			_logger.WriteError(e.Message);
			return new DatabaseRestoreResult(false, messages);
		}
	}

	/// <inheritdoc />
	public bool CheckDbExists(string dbName) {
		try {
			using SqlConnection connection = new(_builder.ConnectionString);
			connection.Open();

			string sqlText = $@"
			SELECT 
				count(name) as [Count] 
			FROM 
				sys.databases WHERE name = '{dbName}'
			";

			SqlCommand cmd = new(sqlText, connection);
			object result = cmd.ExecuteScalar();
			connection.Close();
			return int.Parse(result.ToString()) == 1;
		}
		catch (SqlException e) {
			_logger.WriteError(e.ToString());
			return false;
		}
	}

	/// <inheritdoc />
	public void RenameDb(string from, string to) {
		using SqlConnection connection = new(_builder.ConnectionString);
		connection.Open();
		string sqlText = $"""
			USE master;
			ALTER DATABASE [{from}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
			ALTER DATABASE [{from}] MODIFY NAME = [{to}];
			ALTER DATABASE [{to}] SET MULTI_USER;
			""";
		SqlCommand cmd = new(sqlText, connection);
		cmd.ExecuteNonQuery();
		connection.Close();
	}

	/// <inheritdoc />
	public void DropDb(string optionsDbName) {
		using SqlConnection connection = new(_builder.ConnectionString);
		connection.Open();
		string sqlText = $"""
			USE [master]
			ALTER DATABASE [{optionsDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
			DROP DATABASE [{optionsDbName}];
			""";
		SqlCommand cmd = new(sqlText, connection);
		cmd.ExecuteNonQuery();
		connection.Close();
	}

	private DefaultPaths GetInstanceDefaultPaths(bool closeConnection) {
		using SqlConnection connection = new(_builder.ConnectionString);
		connection.Open();
		return GetInstanceDefaultPaths(connection, closeConnection);
	}

	private DefaultPaths GetInstanceDefaultPaths(SqlConnection connection, bool closeConnection) {
		string sqlText = $"""
			USE [master]
			SELECT
				SERVERPROPERTY('InstanceDefaultDataPath') AS DefaultDataPath,
				SERVERPROPERTY('InstanceDefaultLogPath') AS DefaultLogPath;
			""";
		SqlCommand cmd = new(sqlText, connection);
		using SqlDataReader reader = cmd.ExecuteReader();

		string dataPath = string.Empty;
		string logPath = string.Empty;
		while (reader.Read()) {
			dataPath = reader[0].ToString();
			logPath = reader[1].ToString();
		}

		if (closeConnection) {
			connection.Close();
		}

		return new DefaultPaths(dataPath, logPath);
	}

	/// <inheritdoc />
	public bool TestConnection() {
		try {
			using SqlConnection connection = new(_builder.ConnectionString);
			connection.Open();
			connection.Close();
			return true;
		}
		catch {
			return false;
		}
	}

	/// <inheritdoc />
	public string GetDataPath() {
		DefaultPaths paths = GetInstanceDefaultPaths(closeConnection: true);
		return paths.DataPath;
	}

	private sealed record DefaultPaths(string DataPath, string LogPath);
}
