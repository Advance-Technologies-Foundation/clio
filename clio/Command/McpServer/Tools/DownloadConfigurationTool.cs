using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for downloading workspace configuration with the <c>dconf</c> command.
/// </summary>
public class DownloadConfigurationTool(
	DownloadConfigurationCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver,
	IFileSystem fileSystem)
	: BaseTool<DownloadConfigurationCommandOptions>(command, logger, commandResolver) {

	private static readonly object WorkspaceExecutionLock = new();

	internal const string DownloadConfigurationToolName = "download-configuration";
	internal const string SourceEnvironment = "environment";
	internal const string SourceBuild = "build";

	/// <summary>Legacy MCP tool name retained for prompt and e2e documentation surfaces. The capability now lives on <c>download-configuration</c> with <c>source=environment</c>.</summary>
	internal const string DownloadConfigurationByEnvironmentToolName = "download-configuration-by-environment";
	/// <summary>Legacy MCP tool name retained for prompt and e2e documentation surfaces. The capability now lives on <c>download-configuration</c> with <c>source=build</c>.</summary>
	internal const string DownloadConfigurationByBuildToolName = "download-configuration-by-build";

		[Description("Downloads Creatio configuration into the workspace `.application` folder. source='environment' uses a registered environment name; source='build' uses a Creatio zip file or extracted directory.")]
	public CommandExecutionResult DownloadConfiguration(
		[Description("Download-configuration parameters")] [Required] DownloadConfigurationRunArgs args
	) {
		CommandExecutionResult sourceError = CommandExecutionResult.ValidateExactlyOneMode(
			"source", args.Source, SourceEnvironment, SourceBuild);
		if (sourceError != null) {
			return sourceError;
		}

		if (string.Equals(args.Source, SourceEnvironment, StringComparison.OrdinalIgnoreCase)) {
			CommandExecutionResult missing = CommandExecutionResult.ValidateRequiredForMode(
				"environment-name", args.EnvironmentName, SourceEnvironment);
			if (missing != null) {
				return missing;
			}
			DownloadConfigurationCommandOptions options = new() {
				Environment = args.EnvironmentName
			};
			return ExecuteInWorkspace(args.WorkspacePath, () => InternalExecute<DownloadConfigurationCommand>(options));
		}

		CommandExecutionResult missingBuild = CommandExecutionResult.ValidateRequiredForMode(
			"build-path", args.BuildPath, SourceBuild);
		if (missingBuild != null) {
			return missingBuild;
		}
		if (!IsAbsolutePath(args.BuildPath)) {
			return CreateFailureResult($"Build path must be absolute: {args.BuildPath}");
		}
		DownloadConfigurationCommandOptions buildOptions = new() {
			BuildZipPath = args.BuildPath
		};
		return ExecuteInWorkspace(args.WorkspacePath, () => InternalExecute(buildOptions));
	}

	private static CommandExecutionResult CreateFailureResult(string message) =>
		new(1, [new ErrorMessage(message)]);

	private CommandExecutionResult ExecuteInWorkspace(string workspacePath, Func<CommandExecutionResult> execute) {
		if (string.IsNullOrWhiteSpace(workspacePath)) {
			return CreateFailureResult("Workspace path is required.");
		}

		if (!IsAbsolutePath(workspacePath)) {
			return CreateFailureResult($"Workspace path must be absolute: {workspacePath}");
		}

		if (!fileSystem.Directory.Exists(workspacePath)) {
			return CreateFailureResult($"Workspace path not found: {workspacePath}");
		}

		lock (WorkspaceExecutionLock) {
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
/// MCP arguments for the consolidated <c>download-configuration</c> tool.
/// Exactly one source is active per call: <c>environment</c> or <c>build</c>.
/// </summary>
public sealed record DownloadConfigurationRunArgs(
	[property: JsonPropertyName("source")]
	[property: Description("Discriminator: 'environment' uses a registered clio environment name; 'build' uses a local Creatio zip file or extracted directory.")]
	[property: Required]
	string Source,

	[property: JsonPropertyName("workspace-path")]
	[property: Description("Absolute path to the local workspace that should receive the `.application` content.")]
	[property: Required]
	string WorkspacePath,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Required when source='environment'. Registered clio environment name.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("build-path")]
	[property: Description("Required when source='build'. Absolute path to the Creatio zip file or extracted directory.")]
	string? BuildPath = null
) : ClioRunArgs;
