using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>init-workspace</c> command.
/// </summary>
public class InitWorkspaceTool(
	InitWorkspaceCommand command,
	ILogger logger)
	: BaseTool<InitWorkspaceCommandOptions>(command, logger) {

	internal const string InitWorkspaceToolName = "init-workspace";

	/// <summary>
	/// Initializes a clio workspace in the current directory without overwriting existing files.
	/// </summary>
	[McpServerTool(Name = InitWorkspaceToolName, ReadOnly = false, Destructive = false, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Initializes a clio workspace in the current directory without overwriting existing files.

				 Use this when the target directory already contains code or package folders that should remain intact.
				 """)]
	public CommandExecutionResult InitWorkspace() {
		InitWorkspaceCommandOptions options = new();
		return InternalExecute(options);
	}
}
