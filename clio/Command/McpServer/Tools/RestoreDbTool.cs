using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>restore-db</c> command.
/// </summary>
public class RestoreDbTool(
	RestoreDbCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IDbOperationLogContextAccessor dbOperationLogContextAccessor)
	: BaseTool<RestoreDbCommandOptions>(
		command,
		logger,
		commandResolver,
		dbOperationLogContextAccessor) {
	/// <summary>
	/// Stable MCP tool name for restoring a database by configured environment.
	/// </summary>
	internal const string RestoreDbByEnvironmentToolName = "restore-db-by-environment";

	/// <summary>
	/// Stable MCP tool name for restoring a database by explicit database credentials.
	/// </summary>
	internal const string RestoreDbByCredentialsToolName = "restore-db-by-credentials";

	/// <summary>
	/// Stable MCP tool name for restoring a database to a configured local DB server.
	/// </summary>
	internal const string RestoreDbToLocalServerToolName = "restore-db-to-local-server";

	/// <summary>
	/// Restores a database using a configured clio environment.
	/// </summary>
	[McpServerTool(Name = RestoreDbByEnvironmentToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Restores a database by using a configured clio environment and returns the temp database-operation log path.")]
	public CommandExecutionResult RestoreByEnvironment(
		[Description("Restore parameters")] [Required] RestoreDbByEnvironmentArgs args) {
		RestoreDbCommandOptions options = new() {
			Environment = args.EnvironmentName,
			BackupPath = args.BackupPath,
			DbName = args.DbName,
			Force = args.Force,
			AsTemplate = args.AsTemplate,
			DisableResetPassword = args.DisableResetPassword
		};
		return InternalExecute<RestoreDbCommand>(options);
	}

	/// <summary>
	/// Restores a database using explicit database server URI and credentials.
	/// </summary>
	[McpServerTool(Name = RestoreDbByCredentialsToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Restores a database by using an explicit database server URI, credentials, and backup path, and returns the temp database-operation log path.")]
	public CommandExecutionResult RestoreByCredentials(
		[Description("Restore parameters")] [Required] RestoreDbByCredentialsArgs args) {
		RestoreDbCommandOptions options = new() {
			DbServerUri = args.DbServerUri,
			DbUser = args.DbUser,
			DbPassword = args.DbPassword,
			DbWorknigFolder = args.DbWorkingFolder,
			BackUpFilePath = args.BackupPath,
			DbName = args.DbName,
			Force = args.Force,
			AsTemplate = args.AsTemplate,
			DisableResetPassword = args.DisableResetPassword
		};
		return InternalExecute(options);
	}

	/// <summary>
	/// Restores a database to a configured local database server.
	/// </summary>
	[McpServerTool(Name = RestoreDbToLocalServerToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Restores a database to a configured local database server and returns the temp database-operation log path.")]
	public CommandExecutionResult RestoreToLocalServer(
		[Description("Restore parameters")] [Required] RestoreDbToLocalServerArgs args) {
		RestoreDbCommandOptions options = new() {
			DbServerName = args.DbServerName,
			BackupPath = args.BackupPath,
			DbName = args.DbName,
			DropIfExists = args.DropIfExists,
			AsTemplate = args.AsTemplate,
			DisableResetPassword = args.DisableResetPassword
		};
		return InternalExecute(options);
	}
}

/// <summary>
/// MCP arguments for restoring a database by configured environment.
/// </summary>
public sealed record RestoreDbByEnvironmentArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Configured clio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("backup-path")]
	[property: Description("Optional backup file path override")]
	string? BackupPath,

	[property: JsonPropertyName("db-name")]
	[property: Description("Optional database name override")]
	string? DbName,

	[property: JsonPropertyName("force")]
	[property: Description("Force overwrite behavior in legacy environment restore mode")]
	bool Force = false,

	[property: JsonPropertyName("as-template")]
	[property: Description("Create or refresh only the PostgreSQL template without creating a target database")]
	bool AsTemplate = false,

	[property: JsonPropertyName("disable-reset-password")]
	[property: Description("Attempt to disable forced password reset after a successful restore when the existing version and environment checks allow it")]
	bool DisableResetPassword = true);

/// <summary>
/// MCP arguments for restoring a database by explicit database credentials.
/// </summary>
public sealed record RestoreDbByCredentialsArgs(
	[property: JsonPropertyName("db-server-uri")]
	[property: Description("Database server URI, for example mssql://localhost:1433")]
	[property: Required]
	string DbServerUri,

	[property: JsonPropertyName("db-user")]
	[property: Description("Database user name")]
	string? DbUser,

	[property: JsonPropertyName("db-password")]
	[property: Description("Database password")]
	string? DbPassword,

	[property: JsonPropertyName("db-working-folder")]
	[property: Description("Optional database-visible working folder for MSSQL restore mode")]
	string? DbWorkingFolder,

	[property: JsonPropertyName("backup-path")]
	[property: Description("Backup file path")]
	[property: Required]
	string BackupPath,

	[property: JsonPropertyName("db-name")]
	[property: Description("Database name to create or restore")]
	[property: Required]
	string DbName,

	[property: JsonPropertyName("force")]
	[property: Description("Force overwrite behavior in legacy restore mode")]
	bool Force = false,

	[property: JsonPropertyName("as-template")]
	[property: Description("Create or refresh only the PostgreSQL template without creating a target database")]
	bool AsTemplate = false,

	[property: JsonPropertyName("disable-reset-password")]
	[property: Description("Attempt to disable forced password reset after a successful restore when the existing version and environment checks allow it")]
	bool DisableResetPassword = true);

/// <summary>
/// MCP arguments for restoring a database to a configured local database server.
/// </summary>
public sealed record RestoreDbToLocalServerArgs(
	[property: JsonPropertyName("db-server-name")]
	[property: Description("Configured local database server name from appsettings.json")]
	[property: Required]
	string DbServerName,

	[property: JsonPropertyName("backup-path")]
	[property: Description("Path to a backup file or Creatio zip archive")]
	[property: Required]
	string BackupPath,

	[property: JsonPropertyName("db-name")]
	[property: Description("Database name to create or restore")]
	[property: Required]
	string DbName,

	[property: JsonPropertyName("drop-if-exists")]
	[property: Description("Automatically drop an existing database if present")]
	bool DropIfExists = false,

	[property: JsonPropertyName("as-template")]
	[property: Description("Create or refresh only the PostgreSQL template without creating a target database")]
	bool AsTemplate = false,

	[property: JsonPropertyName("disable-reset-password")]
	[property: Description("Attempt to disable forced password reset after a successful restore when the existing version and environment checks allow it")]
	bool DisableResetPassword = true);
