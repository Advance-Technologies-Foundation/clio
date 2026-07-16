using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Workspaces;
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

	protected CommandExecutionResult ExecuteInWorkspace(
		string workspacePath, TOptions options, Func<CommandExecutionResult> execute) {
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

		// ENG-93208 (H1 fix): route the workspace root through IWorkspacePathBuilder.RootPath — the same
		// settable seam Workspace.PublishToFile/PublishToFolder already use — instead of pinning the
		// PROCESS-WIDE working directory. IWorkspacePathBuilder is resolved from the per-tenant session
		// container (ToolCommandResolver/SessionContainerCache), the SAME container execute()'s
		// InternalExecute<TCommand> resolves the command from, so different tenants never share this
		// instance; only THIS tenant's own concurrent calls could race on it, which ExecuteUnderTenantLock
		// below serializes (same key InternalExecute<TCommand> reacquires reentrantly). The push/restore
		// network operation therefore no longer touches process cwd at all, so it no longer contends with
		// McpToolExecutionLock.CwdLock (held by PageSyncTool/PageFileWriter/PageBaselineGuard while
		// anchoring page output to cwd) — removing the cross-tenant head-of-line blocking (review #4).
		return ExecuteUnderTenantLock(options, () => {
			// An unknown or unreachable environment must fail the SAME graceful way whether it surfaces
			// resolving IWorkspacePathBuilder right below, or a moment later resolving the command itself
			// inside execute() (BaseTool's own resolve path) — both go through the same per-tenant
			// container and environment-settings lookup, so the exception shapes here mirror that path.
			IWorkspacePathBuilder workspacePathBuilder;
			try {
				workspacePathBuilder = ResolveFromCallContainer<IWorkspacePathBuilder>(options);
			}
			catch (EnvironmentResolutionException e) {
				return CommandExecutionResult.FromResolverError(e);
			}
			catch (Exception e) {
				return CommandExecutionResult.FromException(e);
			}

			try {
				workspacePathBuilder.RootPath = workspacePath;
				return execute();
			}
			finally {
				workspacePathBuilder.RootPath = null;
			}
		});
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
		return ExecuteInWorkspace(args.WorkspacePath, options, () => InternalExecute<PushWorkspaceCommand>(options));
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
		return ExecuteInWorkspace(args.WorkspacePath, options, () => InternalExecute<RestoreWorkspaceCommand>(options));
	}
}

/// <summary>
/// MCP arguments for the <c>push-workspace</c> tool.
/// </summary>
public sealed record PushWorkspaceArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
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
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("workspace-path")]
	[property: Description("Absolute path to the local workspace to restore")]
	[property: Required]
	string WorkspacePath
);
