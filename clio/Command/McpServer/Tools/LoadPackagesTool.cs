using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tools for package storage synchronization operations.
/// </summary>
public class LoadPackagesTool(
	LoadPackagesToFileSystemCommand loadPackagesToFileSystemCommand,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<EnvironmentOptions>(loadPackagesToFileSystemCommand, logger, commandResolver) {

	/// <summary>Name of the database import tool.</summary>
	public const string LoadPackagesToDbToolName = "pkg-to-db";

	/// <summary>Name of the file-system export tool.</summary>
	public const string LoadPackagesToFileSystemToolName = "pkg-to-file-system";

	/// <summary>
	/// Loads package definitions from the configuration database into the file system for the selected
	/// environment. Requires file system development mode (FSM) on the environment.
	/// </summary>
	/// <param name="environmentName">Target environment name.</param>
	/// <returns>Execution result for the operation.</returns>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = LoadPackagesToFileSystemToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Loads package definitions from the configuration database into the file system on a Creatio web application. Requires file system development mode (FSM): the call fails when FSM is disabled or the platform reports per-item synchronization errors.")]
	public CommandExecutionResult LoadPackagesToFileSystem(
		[Description("Target environment name")] [Required] string environmentName
	) {
		EnvironmentOptions options = new() {
			Environment = environmentName
		};
		return InternalExecute<LoadPackagesToFileSystemCommand>(options);
	}

	/// <summary>
	/// Loads package definitions from the file system into the configuration database for the selected
	/// environment. Requires file system development mode (FSM) on the environment and never installs
	/// package data rows.
	/// </summary>
	/// <param name="environmentName">Target environment name.</param>
	/// <returns>Execution result for the operation.</returns>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = LoadPackagesToDbToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Loads package definitions (schemas, resources, descriptors) from the file system into the configuration database on a Creatio web application. It does not install package data: a Data/ binding folder is applied only by package installation (push-pkg, push-workspace) or by the DB-first tools create-data-binding-db / upsert-data-binding-row-db. Requires file system development mode (FSM): the call fails when FSM is disabled or the platform reports per-item synchronization errors.")]
	public CommandExecutionResult LoadPackagesToDb(
		[Description("Target environment name")] [Required] string environmentName
	) {
		EnvironmentOptions options = new() {
			Environment = environmentName
		};
		return InternalExecute<LoadPackagesToDbCommand>(options);
	}
}
