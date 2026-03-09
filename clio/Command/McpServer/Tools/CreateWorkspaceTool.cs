using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>create-workspace</c> command.
/// </summary>
public class CreateWorkspaceTool(
	CreateWorkspaceCommand command,
	ILogger logger)
	: BaseTool<CreateWorkspaceCommandOptions>(command, logger) {

	internal const string CreateWorkspaceToolName = "create-workspace";

	/// <summary>
	/// Creates a new empty workspace under an explicit directory or the configured global workspaces root.
	/// </summary>
	[McpServerTool(Name = CreateWorkspaceToolName, ReadOnly = false, Destructive = false, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Creates a new empty clio workspace in a local directory.
				 
				 The tool maps directly to `clio create-workspace <workspace-name> --empty`.
				 When `directory` is provided, the workspace is created under that absolute path.
				 When `directory` is omitted, clio uses the global `workspaces-root` setting from appsettings.json.
				 """)]
	public CommandExecutionResult CreateWorkspace(
		[Description("Relative workspace folder name to create")] [Required] string workspaceName,
		[Description("Optional absolute directory where the new workspace folder should be created")] string directory = null
	) {
		CreateWorkspaceCommandOptions options = new() {
			WorkspaceName = workspaceName,
			Directory = directory,
			Empty = true
		};
		return InternalExecute(options);
	}
}
