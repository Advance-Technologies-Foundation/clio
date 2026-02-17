using System;
using Npgsql;

namespace Clio.Common.db;

public interface IDbConnectionTester
{
	ConnectionTestResult TestConnection(LocalDbServerConfiguration config);
	ConnectionTestResult TestMssqlConnection(string host, int port, string username, string password, bool isWindowsAuth = false);
	ConnectionTestResult TestPostgresConnection(string host, int port, string username, string password, bool isWindowsAuth = false);
}

public class ConnectionTestResult
{
	public bool Success { get; set; }
	public string ErrorMessage { get; set; }
	public string DetailedError { get; set; }
	public string Suggestion { get; set; }
}

public class DbConnectionTester : IDbConnectionTester
{
	private readonly IDbClientFactory _dbClientFactory;

	public DbConnectionTester(IDbClientFactory dbClientFactory) {
		_dbClientFactory = dbClientFactory;
	}

	public ConnectionTestResult TestConnection(LocalDbServerConfiguration config) {
		string dbType = config.DbType?.ToLowerInvariant();

		return dbType switch {
			"mssql" => TestMssqlConnection(config.Hostname, config.Port, config.Username, config.Password, config.UseWindowsAuth),
			"postgres" or "postgresql" => TestPostgresConnection(config.Hostname, config.Port, config.Username, config.Password, config.UseWindowsAuth),
			_ => new ConnectionTestResult {
				Success = false,
				ErrorMessage = $"Unsupported database type: {config.DbType}",
				Suggestion = "Supported types: mssql, postgres"
			}
		};
	}

	public ConnectionTestResult TestMssqlConnection(string host, int port, string username, string password, 
			bool isWindowsAuth = false) {
		try {
			IMssql mssql = _dbClientFactory.CreateMssql(host, port, username, password, isWindowsAuth);
			bool canConnect = mssql.TestConnection();

			if (canConnect) {
				return new ConnectionTestResult {
					Success = true
				};
			}

			return new ConnectionTestResult {
				Success = false,
				ErrorMessage = "Connection test failed",
				Suggestion = "Verify SQL Server is running and credentials are correct"
			};
		} catch (Exception ex) {
			return ParseMssqlException(ex);
		}
	}

	public ConnectionTestResult TestPostgresConnection(string host, int port, string username, string password, 
			bool isWindowsAuth = false) {
		try {
			Postgres postgres = _dbClientFactory.CreatePostgresSilent(host, port, username, password, isWindowsAuth);
			
			string connectionString = $"Host={host};Port={port};Username={username};Password={password};Database=postgres;Timeout=5";
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
			using NpgsqlConnection connection = dataSource.OpenConnection();
			connection.Close();

			return new ConnectionTestResult {
				Success = true
			};
		} catch (Exception ex) {
			return ParsePostgresException(ex);
		}
	}

	private ConnectionTestResult ParseMssqlException(Exception ex) {
		string errorMessage = ex.Message;
		string suggestion = "Check SQL Server connection settings";

		if (errorMessage.Contains("login failed", StringComparison.OrdinalIgnoreCase)) {
			suggestion = "Verify username and password are correct";
		} else if (errorMessage.Contains("network", StringComparison.OrdinalIgnoreCase) ||
		           errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase)) {
			suggestion = "Check if SQL Server is running and network/firewall settings allow connection";
		} else if (errorMessage.Contains("cannot open", StringComparison.OrdinalIgnoreCase)) {
			suggestion = "Verify SQL Server instance is running and accessible";
		}

		return new ConnectionTestResult {
			Success = false,
			ErrorMessage = "Failed to connect to MSSQL server",
			DetailedError = errorMessage,
			Suggestion = suggestion
		};
	}

	private ConnectionTestResult ParsePostgresException(Exception ex) {
		string errorMessage = ex.Message;
		string suggestion = "Check PostgreSQL connection settings";

		if (ex is PostgresException pgEx) {
			if (pgEx.SqlState == "28P01") {
				suggestion = "Authentication failed. Verify username and password are correct";
			} else if (pgEx.SqlState == "3D000") {
				suggestion = "Database does not exist";
			}
		} else if (ex is NpgsqlException npgEx) {
			if (errorMessage.Contains("password authentication failed", StringComparison.OrdinalIgnoreCase)) {
				suggestion = "Verify username and password are correct";
			} else if (errorMessage.Contains("could not connect", StringComparison.OrdinalIgnoreCase) ||
			           errorMessage.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)) {
				suggestion = "Check if PostgreSQL is running and network/firewall settings allow connection on port " + 
				             (ex.Data.Contains("Port") ? ex.Data["Port"] : "5432");
			} else if (errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase)) {
				suggestion = "Connection timeout. Check network connectivity and PostgreSQL server status";
			} else if (errorMessage.Contains("reading from stream", StringComparison.OrdinalIgnoreCase) ||
			           errorMessage.Contains("Exception while reading", StringComparison.OrdinalIgnoreCase)) {
				suggestion = "PostgreSQL server may not be running or is not accepting connections. Verify:\n" +
				             "  1. PostgreSQL service is running\n" +
				             "  2. Server is listening on the correct port\n" +
				             "  3. pg_hba.conf allows connections from your host\n" +
				             "  4. postgresql.conf has listen_addresses configured correctly";
			}
		}

		return new ConnectionTestResult {
			Success = false,
			ErrorMessage = "Failed to connect to PostgreSQL server",
			DetailedError = errorMessage,
			Suggestion = suggestion
		};
	}
}
