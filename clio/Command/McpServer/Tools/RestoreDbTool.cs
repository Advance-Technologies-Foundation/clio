using System;
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
	/// Stable MCP tool name for the consolidated restore-db tool.
	/// </summary>
	internal const string RestoreDbToolName = "restore-db";

	internal const string ModeEnvironment = "environment";
	internal const string ModeDbCredentials = "db-credentials";
	internal const string ModeLocalServer = "local-server";

	/// <summary>Legacy MCP tool name retained for prompt and e2e documentation surfaces. The capability now lives on <c>restore-db</c> with <c>mode=environment</c>.</summary>
	internal const string RestoreDbByEnvironmentToolName = "restore-db-by-environment";
	/// <summary>Legacy MCP tool name retained for prompt and e2e documentation surfaces. The capability now lives on <c>restore-db</c> with <c>mode=db-credentials</c>.</summary>
	internal const string RestoreDbByCredentialsToolName = "restore-db-by-credentials";
	/// <summary>Legacy MCP tool name retained for prompt and e2e documentation surfaces. The capability now lives on <c>restore-db</c> with <c>mode=local-server</c>.</summary>
	internal const string RestoreDbToLocalServerToolName = "restore-db-to-local-server";

		[Description("Restores a database. mode='environment' restores via a configured clio environment; mode='db-credentials' restores using explicit database server URI + credentials; mode='local-server' restores to a configured local DB server. Returns the temp database-operation log path.")]
	public CommandExecutionResult Restore(
		[Description("Restore parameters")] [Required] RestoreDbRunArgs args
	) {
		CommandExecutionResult modeError = CommandExecutionResult.ValidateExactlyOneMode(
			"mode", args.Mode, ModeEnvironment, ModeDbCredentials, ModeLocalServer);
		if (modeError != null) {
			return modeError;
		}

		if (string.Equals(args.Mode, ModeEnvironment, StringComparison.OrdinalIgnoreCase)) {
			return RestoreByEnvironment(args);
		}
		if (string.Equals(args.Mode, ModeDbCredentials, StringComparison.OrdinalIgnoreCase)) {
			return RestoreByDbCredentials(args);
		}
		return RestoreToLocalServer(args);
	}

	private CommandExecutionResult RestoreByEnvironment(RestoreDbRunArgs args) {
		CommandExecutionResult missing = CommandExecutionResult.ValidateRequiredForMode(
			"environment-name", args.EnvironmentName, ModeEnvironment);
		if (missing != null) {
			return missing;
		}
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

	private CommandExecutionResult RestoreByDbCredentials(RestoreDbRunArgs args) {
		CommandExecutionResult missingServer = CommandExecutionResult.ValidateRequiredForMode(
			"db-server-uri", args.DbServerUri, ModeDbCredentials);
		if (missingServer != null) {
			return missingServer;
		}
		CommandExecutionResult missingBackup = CommandExecutionResult.ValidateRequiredForMode(
			"backup-path", args.BackupPath, ModeDbCredentials);
		if (missingBackup != null) {
			return missingBackup;
		}
		CommandExecutionResult missingDbName = CommandExecutionResult.ValidateRequiredForMode(
			"db-name", args.DbName, ModeDbCredentials);
		if (missingDbName != null) {
			return missingDbName;
		}
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

	private CommandExecutionResult RestoreToLocalServer(RestoreDbRunArgs args) {
		CommandExecutionResult missingServer = CommandExecutionResult.ValidateRequiredForMode(
			"db-server-name", args.DbServerName, ModeLocalServer);
		if (missingServer != null) {
			return missingServer;
		}
		CommandExecutionResult missingBackup = CommandExecutionResult.ValidateRequiredForMode(
			"backup-path", args.BackupPath, ModeLocalServer);
		if (missingBackup != null) {
			return missingBackup;
		}
		CommandExecutionResult missingDbName = CommandExecutionResult.ValidateRequiredForMode(
			"db-name", args.DbName, ModeLocalServer);
		if (missingDbName != null) {
			return missingDbName;
		}
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
/// MCP arguments for the consolidated <c>restore-db</c> tool. Exactly one mode is active per call.
/// </summary>
public sealed record RestoreDbRunArgs(
	[property: JsonPropertyName("mode")]
	[property: Description("Discriminator: 'environment' restores via a configured clio environment; 'db-credentials' uses explicit database server URI + credentials; 'local-server' targets a configured local DB server.")]
	[property: Required]
	string Mode,

	// Environment mode
	[property: JsonPropertyName("environment-name")]
	[property: Description("Required when mode='environment'. Configured clio environment name.")]
	string? EnvironmentName = null,

	// DB-credentials mode
	[property: JsonPropertyName("db-server-uri")]
	[property: Description("Required when mode='db-credentials'. Database server URI, for example mssql://localhost:1433.")]
	string? DbServerUri = null,

	[property: JsonPropertyName("db-user")]
	[property: Description("Optional when mode='db-credentials'. Database user name.")]
	string? DbUser = null,

	[property: JsonPropertyName("db-password")]
	[property: Description("Optional when mode='db-credentials'. Database password.")]
	string? DbPassword = null,

	[property: JsonPropertyName("db-working-folder")]
	[property: Description("Optional when mode='db-credentials'. Database-visible working folder for MSSQL restore mode.")]
	string? DbWorkingFolder = null,

	// Local-server mode
	[property: JsonPropertyName("db-server-name")]
	[property: Description("Required when mode='local-server'. Configured local database server name from appsettings.json.")]
	string? DbServerName = null,

	[property: JsonPropertyName("drop-if-exists")]
	[property: Description("Optional when mode='local-server'. Automatically drop an existing database if present.")]
	bool DropIfExists = false,

	// Shared
	[property: JsonPropertyName("backup-path")]
	[property: Description("Backup file path. Required for mode='db-credentials' and mode='local-server'; optional override when mode='environment'.")]
	string? BackupPath = null,

	[property: JsonPropertyName("db-name")]
	[property: Description("Database name. Required for mode='db-credentials' and mode='local-server'; optional override when mode='environment'.")]
	string? DbName = null,

	[property: JsonPropertyName("force")]
	[property: Description("Force overwrite behavior. Honored in mode='environment' and mode='db-credentials'.")]
	bool Force = false,

	[property: JsonPropertyName("as-template")]
	[property: Description("Create or refresh only the PostgreSQL template without creating a target database.")]
	bool AsTemplate = false,

	[property: JsonPropertyName("disable-reset-password")]
	[property: Description("Attempt to disable forced password reset after a successful restore when version and environment checks allow it.")]
	bool DisableResetPassword = true
) : ClioRunArgs;
