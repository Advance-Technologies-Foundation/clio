using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>create-workspace</c> command.
/// </summary>
public class CreateWorkspaceTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateWorkspaceCommandOptions>(null, logger, commandResolver) {

	internal const string CreateWorkspaceToolName = "create-workspace";

	/// <summary>
	/// Creates a new empty workspace under an explicit directory or the configured global workspaces root.
	/// </summary>
	[McpServerTool(Name = CreateWorkspaceToolName, ReadOnly = false, Destructive = false, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Creates a new empty clio workspace in a local directory.
				 
				 To create a workspace in C:\Projects\Workspaces\son directory, use:
				 workspaceName: son, directory: C:\Projects\Workspaces
				 """)]
	public CommandExecutionResult CreateWorkspace(
		[Description("Workspace creation parameters")] [Required] CreateWorkspaceArgs args
	) {
		if (args is null) {
			return new CommandExecutionResult(1, [new ErrorMessage("Workspace creation parameters are required.")], null);
		}
		CreateWorkspaceCommandOptions options = new() {
			WorkspaceName = args.WorkspaceName,
			Directory = args.Directory,
			Empty = true
		};
		return InternalExecute<CreateWorkspaceCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>create-workspace</c> tool.
/// </summary>
public sealed record CreateWorkspaceArgs(
	[property: JsonPropertyName("workspaceName")]
	[property: Description("Relative workspace folder name to create")]
	[property: Required]
	string WorkspaceName,

	[property: JsonPropertyName("directory")]
	[property: Description("Optional absolute directory where the new workspace folder should be created")]
	string Directory = null
);
