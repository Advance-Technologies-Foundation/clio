using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared workspace-path validation and working-directory execution flow for workspace MCP tools.
/// </summary>
public abstract class WorkspaceCommandToolBase<TOptions>(
	Command<TOptions> command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IFileSystem fileSystem)
	: BaseTool<TOptions>(command, logger, commandResolver)
	where TOptions : EnvironmentOptions {

	protected CommandExecutionResult ExecuteInWorkspace(string workspacePath, Func<CommandExecutionResult> execute) {
		if (string.IsNullOrWhiteSpace(workspacePath)) {
			return CreateFailureResult("Workspace path is required.");
		}

		if (IsNetworkPath(workspacePath)) {
			return CreateFailureResult($"Workspace path must be a local absolute path: {workspacePath}");
		}

		if (!IsAbsolutePath(workspacePath)) {
			return CreateFailureResult($"Workspace path must be absolute: {workspacePath}");
		}

		if (!fileSystem.Directory.Exists(workspacePath)) {
			return CreateFailureResult($"Workspace path not found: {workspacePath}");
		}

		lock (CommandExecutionSyncRoot) {
			string originalDirectory = fileSystem.Directory.GetCurrentDirectory();
			try {
				fileSystem.Directory.SetCurrentDirectory(workspacePath);
				return execute();
			}
			finally {
				fileSystem.Directory.SetCurrentDirectory(originalDirectory);
			}
		}
	}

	private static CommandExecutionResult CreateFailureResult(string message) =>
		new(1, [new ErrorMessage(message)]);

	private bool IsAbsolutePath(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return false;
		}

		string root = fileSystem.Path.GetPathRoot(path);
		return fileSystem.Path.IsPathRooted(path) &&
			!string.IsNullOrWhiteSpace(root) &&
			(root.EndsWith(fileSystem.Path.DirectorySeparatorChar) ||
			 root.EndsWith(fileSystem.Path.AltDirectorySeparatorChar));
	}

	private bool IsNetworkPath(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return false;
		}

		return path.StartsWith(@"\\", StringComparison.Ordinal) ||
			path.StartsWith("//", StringComparison.Ordinal);
	}
}

/// <summary>
/// MCP tool surface for the <c>push-workspace</c> command.
/// </summary>
public sealed class PushWorkspaceTool(
	PushWorkspaceCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IFileSystem fileSystem)
	: WorkspaceCommandToolBase<PushWorkspaceCommandOptions>(command, logger, commandResolver, fileSystem) {

	internal const string PushWorkspaceToolName = "push-workspace";

	/// <summary>
	/// Pushes the current local workspace to the specified Creatio environment.
	/// </summary>
	[McpServerTool(Name = PushWorkspaceToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Pushes the local workspace at `workspace-path` to the specified Creatio environment")]
	public CommandExecutionResult PushWorkspace(
		[Description("Push-workspace parameters")] [Required] PushWorkspaceArgs args
	) {
		PushWorkspaceCommandOptions options = new() {
			Environment = args.EnvironmentName,
			UseApplicationInstaller = true,
			SkipBackup = args.SkipBackup
		};
		return ExecuteInWorkspace(args.WorkspacePath, () => InternalExecute<PushWorkspaceCommand>(options));
	}
}

/// <summary>
/// MCP tool surface for the <c>restore-workspace</c> command.
/// </summary>
public sealed class RestoreWorkspaceTool(
	RestoreWorkspaceCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IFileSystem fileSystem)
	: WorkspaceCommandToolBase<RestoreWorkspaceOptions>(command, logger, commandResolver, fileSystem) {

	internal const string RestoreWorkspaceToolName = "restore-workspace";

	/// <summary>
	/// Restores the local workspace from the specified Creatio environment.
	/// </summary>
	[McpServerTool(Name = RestoreWorkspaceToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Restores the local workspace at `workspace-path` from the specified Creatio environment")]
	public CommandExecutionResult RestoreWorkspace(
		[Description("Restore-workspace parameters")] [Required] RestoreWorkspaceArgs args
	) {
		RestoreWorkspaceOptions options = new() {
			Environment = args.EnvironmentName
		};
		return ExecuteInWorkspace(args.WorkspacePath, () => InternalExecute<RestoreWorkspaceCommand>(options));
	}
}

/// <summary>
/// MCP arguments for the <c>push-workspace</c> tool.
/// </summary>
public sealed record PushWorkspaceArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("workspace-path")]
	[property: Description("Absolute path to the local workspace to push")]
	[property: Required]
	string WorkspacePath,

	[property: JsonPropertyName("skip-backup")]
	[property: Description("When true, skips package backup before workspace install")]
	bool? SkipBackup = null
);

/// <summary>
/// MCP arguments for the <c>restore-workspace</c> tool.
/// </summary>
public sealed record RestoreWorkspaceArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("workspace-path")]
	[property: Description("Absolute path to the local workspace to restore")]
	[property: Required]
	string WorkspacePath
);
