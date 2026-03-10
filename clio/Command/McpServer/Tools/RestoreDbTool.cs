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
			Force = args.Force
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
			Force = args.Force
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
			DropIfExists = args.DropIfExists
		};
		return InternalExecute(options);
	}
}

/// <summary>
/// MCP arguments for restoring a database by configured environment.
/// </summary>
public sealed record RestoreDbByEnvironmentArgs(
	[property: JsonPropertyName("environmentName")]
	[property: Description("Configured clio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("backupPath")]
	[property: Description("Optional backup file path override")]
	string? BackupPath,

	[property: JsonPropertyName("dbName")]
	[property: Description("Optional database name override")]
	string? DbName,

	[property: JsonPropertyName("force")]
	[property: Description("Force overwrite behavior in legacy environment restore mode")]
	bool Force = false);

/// <summary>
/// MCP arguments for restoring a database by explicit database credentials.
/// </summary>
public sealed record RestoreDbByCredentialsArgs(
	[property: JsonPropertyName("dbServerUri")]
	[property: Description("Database server URI, for example mssql://localhost:1433")]
	[property: Required]
	string DbServerUri,

	[property: JsonPropertyName("dbUser")]
	[property: Description("Database user name")]
	string? DbUser,

	[property: JsonPropertyName("dbPassword")]
	[property: Description("Database password")]
	string? DbPassword,

	[property: JsonPropertyName("dbWorkingFolder")]
	[property: Description("Optional database-visible working folder for MSSQL restore mode")]
	string? DbWorkingFolder,

	[property: JsonPropertyName("backupPath")]
	[property: Description("Backup file path")]
	[property: Required]
	string BackupPath,

	[property: JsonPropertyName("dbName")]
	[property: Description("Database name to create or restore")]
	[property: Required]
	string DbName,

	[property: JsonPropertyName("force")]
	[property: Description("Force overwrite behavior in legacy restore mode")]
	bool Force = false);

/// <summary>
/// MCP arguments for restoring a database to a configured local database server.
/// </summary>
public sealed record RestoreDbToLocalServerArgs(
	[property: JsonPropertyName("dbServerName")]
	[property: Description("Configured local database server name from appsettings.json")]
	[property: Required]
	string DbServerName,

	[property: JsonPropertyName("backupPath")]
	[property: Description("Path to a backup file or Creatio zip archive")]
	[property: Required]
	string BackupPath,

	[property: JsonPropertyName("dbName")]
	[property: Description("Database name to create or restore")]
	[property: Required]
	string DbName,

	[property: JsonPropertyName("dropIfExists")]
	[property: Description("Automatically drop an existing database if present")]
	bool DropIfExists = false);
