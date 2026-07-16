using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tools for downloading workspace configuration with the <c>dconf</c> command.
/// </summary>
public class DownloadConfigurationTool(
	DownloadConfigurationCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IFileSystem fileSystem)
	: BaseTool<DownloadConfigurationCommandOptions>(command, logger, commandResolver) {

	internal const string DownloadConfigurationByEnvironmentToolName = "download-configuration-by-environment";
	internal const string DownloadConfigurationByBuildToolName = "download-configuration-by-build";

	/// <summary>
	/// Downloads Creatio configuration into the specified local workspace from a registered environment.
	/// </summary>
	[McpServerTool(Name = DownloadConfigurationByEnvironmentToolName, ReadOnly = false, Destructive = false,
		Idempotent = false, OpenWorld = false)]
	[Description("Downloads Creatio configuration into the workspace `.application` folder from a registered environment name")]
	public CommandExecutionResult DownloadConfigurationByEnvironment(
		[Description("Download-configuration parameters")] [Required] DownloadConfigurationByEnvironmentArgs args
	) {
		DownloadConfigurationCommandOptions options = new() {
			Environment = args.EnvironmentName
		};
		return ExecuteInWorkspace(args.WorkspacePath, options, () => InternalExecute<DownloadConfigurationCommand>(options));
	}

	/// <summary>
	/// Downloads Creatio configuration into the specified local workspace from a zip file or extracted build directory.
	/// </summary>
	[McpServerTool(Name = DownloadConfigurationByBuildToolName, ReadOnly = false, Destructive = false,
		Idempotent = false, OpenWorld = false)]
	[Description("Downloads Creatio configuration into the workspace `.application` folder from a Creatio zip file or extracted directory")]
	public CommandExecutionResult DownloadConfigurationByBuild(
		[Description("Download-configuration parameters")] [Required] DownloadConfigurationByBuildArgs args
	) {
		if (!IsAbsolutePath(args.BuildPath)) {
			return CreateFailureResult($"Build path must be absolute: {args.BuildPath}");
		}

		DownloadConfigurationCommandOptions options = new() {
			BuildZipPath = args.BuildPath
		};
		return ExecuteInWorkspace(args.WorkspacePath, options, () => InternalExecute(options));
	}

	private static CommandExecutionResult CreateFailureResult(string message) =>
		new(1, [new ErrorMessage(message)]);

	private CommandExecutionResult ExecuteInWorkspace(
		string workspacePath, DownloadConfigurationCommandOptions options, Func<CommandExecutionResult> execute) {
		if (string.IsNullOrWhiteSpace(workspacePath)) {
			return CreateFailureResult("Workspace path is required.");
		}

		if (!IsAbsolutePath(workspacePath)) {
			return CreateFailureResult($"Workspace path must be absolute: {workspacePath}");
		}

		if (!fileSystem.Directory.Exists(workspacePath)) {
			return CreateFailureResult($"Workspace path not found: {workspacePath}");
		}

		// The working-directory pin mutates PROCESS-WIDE cwd, so it holds the single global CwdLock (H2 —
		// this replaces the former private WorkspaceExecutionLock, which serialized only this tool and did
		// not exclude the other cwd writers/readers). Lock ordering is per-tenant → CwdLock: take the
		// per-tenant lock FIRST via ExecuteUnderTenantLock(options) — the same key the inner execute()
		// resolves under, so the inner acquire is reentrant — THEN CwdLock around pin/execute/restore.
		return ExecuteUnderTenantLock(options, () => {
			lock (McpToolExecutionLock.CwdLock) {
				string originalDirectory = fileSystem.Directory.GetCurrentDirectory();
				try {
					fileSystem.Directory.SetCurrentDirectory(workspacePath);
					return execute();
				}
				finally {
					fileSystem.Directory.SetCurrentDirectory(originalDirectory);
				}
			}
		});
	}

	private bool IsAbsolutePath(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return false;
		}

		string? root = fileSystem.Path.GetPathRoot(path);
		return fileSystem.Path.IsPathRooted(path) &&
			!string.IsNullOrWhiteSpace(root) &&
			(root.EndsWith(fileSystem.Path.DirectorySeparatorChar) ||
			root.EndsWith(fileSystem.Path.AltDirectorySeparatorChar));
	}
}

/// <summary>
/// MCP arguments for downloading configuration from a registered environment.
/// </summary>
public sealed record DownloadConfigurationByEnvironmentArgs(
	[property: JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("workspace-path")]
	[Description("Absolute path to the local workspace that should receive the `.application` content")]
	[Required]
	string WorkspacePath
);

/// <summary>
/// MCP arguments for downloading configuration from a local build artifact or extracted directory.
/// </summary>
public sealed record DownloadConfigurationByBuildArgs(
	[property: JsonPropertyName("build-path")]
	[Description("Absolute path to the Creatio zip file or extracted directory")]
	[Required]
	string BuildPath,

	[property: JsonPropertyName("workspace-path")]
	[Description("Absolute path to the local workspace that should receive the `.application` content")]
	[Required]
	string WorkspacePath
);
