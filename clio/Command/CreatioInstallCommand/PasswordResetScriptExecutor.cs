using System;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Clio.Command.CreatioInstallCommand;

/// <summary>
/// Represents parameters required to execute the force-password-reset disabling script.
/// </summary>
public sealed record PasswordResetScriptExecutionRequest{
	/// <summary>
	/// Gets the target database type.
	/// </summary>
	public InstallerHelper.DatabaseType DatabaseType { get; init; }

	/// <summary>
	/// Gets the database host name.
	/// </summary>
	public string Host { get; init; }

	/// <summary>
	/// Gets the database port.
	/// </summary>
	public int Port { get; init; }

	/// <summary>
	/// Gets the database name.
	/// </summary>
	public string DatabaseName { get; init; }

	/// <summary>
	/// Gets the database user name.
	/// </summary>
	public string Username { get; init; }

	/// <summary>
	/// Gets the database password.
	/// </summary>
	public string Password { get; init; }

	/// <summary>
	/// Gets a value indicating whether Windows authentication should be used for SQL Server.
	/// </summary>
	public bool UseWindowsAuth { get; init; }
}

/// <summary>
/// Represents result of password reset script execution.
/// </summary>
public sealed record PasswordResetScriptExecutionResult{
	/// <summary>
	/// Gets a value indicating whether script execution succeeded.
	/// </summary>
	public bool Success { get; init; }

	/// <summary>
	/// Gets script execution error details when execution fails.
	/// </summary>
	public string ErrorMessage { get; init; }
}

/// <summary>
/// Executes SQL that disables immediate Supervisor password change requirement.
/// </summary>
public interface IPasswordResetScriptExecutor{
	#region Methods: Public

	/// <summary>
	/// Executes a DB-specific script against the target environment.
	/// </summary>
	/// <param name="request">Execution request with database connection details.</param>
	/// <returns>Script execution result.</returns>
	PasswordResetScriptExecutionResult TryExecute(PasswordResetScriptExecutionRequest request);

	#endregion
}

/// <summary>
/// Default implementation of <see cref="IPasswordResetScriptExecutor"/>.
/// </summary>
public class PasswordResetScriptExecutor : IPasswordResetScriptExecutor{
	#region Fields: Private

	private const string PostgresScript = """
	                               update "SysAdminUnit"
	                               set "ForceChangePassword" = false
	                               where "Id" = '7F3B869F-34F3-4F20-AB4D-7480A5FDF647'
	                               """;

	private const string SqlServerScript = """
	                                update [SysAdminUnit]
	                                set [ForceChangePassword] = 0
	                                where [Id] = '7F3B869F-34F3-4F20-AB4D-7480A5FDF647'
	                                """;

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public PasswordResetScriptExecutionResult TryExecute(PasswordResetScriptExecutionRequest request) {
		try {
			return request.DatabaseType switch {
				InstallerHelper.DatabaseType.Postgres => ExecutePostgresScript(request),
				InstallerHelper.DatabaseType.MsSql => ExecuteSqlServerScript(request),
				var _ => new PasswordResetScriptExecutionResult {
					Success = false,
					ErrorMessage = $"Unsupported database type '{request.DatabaseType}'."
				}
			};
		}
		catch (Exception ex) {
			return new PasswordResetScriptExecutionResult {
				Success = false,
				ErrorMessage = ex.Message
			};
		}
	}

	#endregion

	#region Methods: Private

	private static PasswordResetScriptExecutionResult ExecutePostgresScript(PasswordResetScriptExecutionRequest request) {
		NpgsqlConnectionStringBuilder builder = new() {
			Host = request.Host,
			Port = request.Port,
			Database = request.DatabaseName,
			Username = request.Username,
			Password = request.Password,
			Timeout = 15,
			CommandTimeout = 30
		};

		using NpgsqlConnection connection = new(builder.ConnectionString);
		connection.Open();
		using NpgsqlCommand command = new(PostgresScript, connection);
		command.CommandTimeout = 30;
		command.ExecuteNonQuery();

		return new PasswordResetScriptExecutionResult {
			Success = true
		};
	}

	private static PasswordResetScriptExecutionResult ExecuteSqlServerScript(
		PasswordResetScriptExecutionRequest request) {
		string dataSource = request.Host.Contains("\\", StringComparison.Ordinal) || request.Port == 0
			? request.Host
			: $"{request.Host},{request.Port}";
		SqlConnectionStringBuilder builder = new() {
			DataSource = dataSource,
			InitialCatalog = request.DatabaseName,
			Encrypt = false,
			TrustServerCertificate = true,
			IntegratedSecurity = request.UseWindowsAuth,
			ConnectTimeout = 15
		};
		if (!request.UseWindowsAuth) {
			builder.UserID = request.Username;
			builder.Password = request.Password;
		}

		using SqlConnection connection = new(builder.ConnectionString);
		connection.Open();
		using SqlCommand command = new(SqlServerScript, connection) {
			CommandTimeout = 30
		};
		command.ExecuteNonQuery();

		return new PasswordResetScriptExecutionResult {
			Success = true
		};
	}

	#endregion
}
