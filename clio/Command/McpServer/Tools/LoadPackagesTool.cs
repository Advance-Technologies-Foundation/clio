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

	/// <summary>
	/// Loads packages from the database into the file system for the selected environment.
	/// </summary>
	/// <param name="environmentName">Target environment name.</param>
	/// <returns>Execution result for the operation.</returns>
	[McpServerTool(Name = "pkg-to-file-system", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Loads packages from the database into the file system on a Creatio web application")]
	public CommandExecutionResult LoadPackagesToFileSystem(
		[Description("Target environment name")] [Required] string environmentName
	) {
		EnvironmentOptions options = new() {
			Environment = environmentName
		};
		return InternalExecute<LoadPackagesToFileSystemCommand>(options);
	}

	/// <summary>
	/// Loads packages from the file system into the database for the selected environment.
	/// </summary>
	/// <param name="environmentName">Target environment name.</param>
	/// <returns>Execution result for the operation.</returns>
	[McpServerTool(Name = "pkg-to-db", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Loads packages from the file system into the database on a Creatio web application")]
	public CommandExecutionResult LoadPackagesToDb(
		[Description("Target environment name")] [Required] string environmentName
	) {
		EnvironmentOptions options = new() {
			Environment = environmentName
		};
		return InternalExecute<LoadPackagesToDbCommand>(options);
	}
}
