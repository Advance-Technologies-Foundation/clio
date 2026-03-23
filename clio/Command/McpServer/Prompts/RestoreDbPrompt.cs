using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the <c>restore-db</c> MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for restoring databases through MCP")]
public static class RestoreDbPrompt {
	/// <summary>
	/// Builds a prompt for environment-based database restore.
	/// </summary>
	[McpServerPrompt(Name = RestoreDbTool.RestoreDbByEnvironmentToolName),
	 Description("Prompt to restore a database by configured environment")]
	public static string RestoreByEnvironmentPrompt(
		[Required]
		[Description("Configured clio environment name")]
		string environmentName) =>
		$"""
		 Call `{RestoreDbTool.RestoreDbByEnvironmentToolName}` for environment `{environmentName}`.
		 Include `backupPath` or `dbName` only when you need to override the stored environment configuration.
		 Set `asTemplate` to `true` only when you want a PostgreSQL `.backup` or ZIP source to create or refresh the reusable template without creating the target database.
		 Set `disableResetPassword` to `false` only when you explicitly want to skip the post-restore password-reset disabling step.
		 The result will include `log-file-path`; use that temp artifact for detailed PostgreSQL or MSSQL troubleshooting.
		 """;

	/// <summary>
	/// Builds a prompt for explicit-credential database restore.
	/// </summary>
	[McpServerPrompt(Name = RestoreDbTool.RestoreDbByCredentialsToolName),
	 Description("Prompt to restore a database by explicit database credentials")]
	public static string RestoreByCredentialsPrompt(
		[Required]
		[Description("Database server URI")]
		string dbServerUri,
		[Required]
		[Description("Backup file path")]
		string backupPath,
		[Required]
		[Description("Database name")]
		string dbName) =>
		$"""
		 Call `{RestoreDbTool.RestoreDbByCredentialsToolName}` with database server URI `{dbServerUri}`,
		 backup path `{backupPath}`, and target database `{dbName}`.
		 Include `dbWorkingFolder` for MSSQL restore flows when the SQL Server host must see the copied backup file.
		 Set `asTemplate` to `true` only when you want a PostgreSQL `.backup` or ZIP source to create or refresh the reusable template without creating the target database.
		 Set `disableResetPassword` to `false` only when you explicitly want to skip the post-restore password-reset disabling step.
		 The result will include `log-file-path`; use that temp artifact for detailed database-engine troubleshooting.
		 """;

	/// <summary>
	/// Builds a prompt for configured local-server database restore.
	/// </summary>
	[McpServerPrompt(Name = RestoreDbTool.RestoreDbToLocalServerToolName),
	 Description("Prompt to restore a database to a configured local DB server")]
	public static string RestoreToLocalServerPrompt(
		[Required]
		[Description("Configured local DB server name")]
		string dbServerName,
		[Required]
		[Description("Backup path")]
		string backupPath,
		[Required]
		[Description("Database name")]
		string dbName) =>
		$"""
		 Call `{RestoreDbTool.RestoreDbToLocalServerToolName}` with local server `{dbServerName}`,
		 backup path `{backupPath}`, and database `{dbName}`.
		 Use `dropIfExists` only when replacing an existing local database is intentional.
		 Set `asTemplate` to `true` only when you want a PostgreSQL `.backup` or ZIP source to create or refresh the reusable template without creating the target database.
		 Set `disableResetPassword` to `false` only when you explicitly want to skip the post-restore password-reset disabling step.
		 The result will include `log-file-path`; use that temp artifact for detailed PostgreSQL or MSSQL troubleshooting.
		 """;
}
