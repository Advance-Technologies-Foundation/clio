using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Workspaces;
using ModelContextProtocol.Server;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared scope-aware execution flow for skill management MCP tools.
/// </summary>
public abstract class WorkspaceSkillToolBase<TOptions>(
	Command<TOptions> command,
	ILogger logger,
	IFileSystem fileSystem)
	: BaseTool<TOptions>(command, logger) {
	private readonly IFileSystem _fileSystem = fileSystem;

	protected CommandExecutionResult ExecuteForScope(string scopeValue, string workspacePath, Func<CommandExecutionResult> execute) {
		if (!SkillScopeParser.TryParse(scopeValue, out SkillScope scope, out string errorMessage)) {
			return CreateFailureResult(errorMessage);
		}

		return scope == SkillScope.User
			? execute()
			: ExecuteInWorkspace(workspacePath, execute);
	}

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

		if (!_fileSystem.Directory.Exists(workspacePath)) {
			return CreateFailureResult($"Workspace path not found: {workspacePath}");
		}

		string workspaceSettingsPath = _fileSystem.Path.Combine(workspacePath, ".clio", "workspaceSettings.json");
		if (!_fileSystem.File.Exists(workspaceSettingsPath)) {
			return CreateFailureResult($"Workspace path is not a clio workspace: {workspacePath}");
		}

		lock (CommandExecutionSyncRoot) {
			string originalDirectory = _fileSystem.Directory.GetCurrentDirectory();
			try {
				_fileSystem.Directory.SetCurrentDirectory(workspacePath);
				return execute();
			}
			finally {
				_fileSystem.Directory.SetCurrentDirectory(originalDirectory);
			}
		}
	}

	private static CommandExecutionResult CreateFailureResult(string message) =>
		new(1, [new ErrorMessage(message)]);

	private bool IsAbsolutePath(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return false;
		}

		string root = _fileSystem.Path.GetPathRoot(path);
		return _fileSystem.Path.IsPathRooted(path) &&
			!string.IsNullOrWhiteSpace(root) &&
			(root.EndsWith(_fileSystem.Path.DirectorySeparatorChar) ||
			 root.EndsWith(_fileSystem.Path.AltDirectorySeparatorChar));
	}

	private static bool IsNetworkPath(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return false;
		}

		return path.StartsWith(@"\\", StringComparison.Ordinal) ||
			path.StartsWith("//", StringComparison.Ordinal);
	}
}

/// <summary>
/// MCP tool surface for the <c>install-skills</c> command.
/// </summary>
[McpServerToolType]
public sealed class InstallSkillsTool(
	InstallSkillsCommand command,
	ILogger logger,
	IFileSystem fileSystem)
	: WorkspaceSkillToolBase<InstallSkillsOptions>(command, logger, fileSystem) {
	internal const string ToolName = "install-skills";

	/// <summary>
	/// Installs new managed skills into the selected scope from the selected repository.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Installs one or more new managed skills into workspace or user scope from a local repository path or git URL")]
	public CommandExecutionResult InstallSkills(
		[Description("Install-skills parameters")] [Required] InstallSkillsArgs args) {
		InstallSkillsOptions options = new() {
			Skill = args.SkillName,
			Repo = args.Repo,
			Scope = args.Scope
		};
		return ExecuteForScope(args.Scope, args.WorkspacePath, () => InternalExecute(options));
	}
}

/// <summary>
/// MCP tool surface for the <c>update-skill</c> command.
/// </summary>
[McpServerToolType]
public sealed class UpdateSkillTool(
	UpdateSkillCommand command,
	ILogger logger,
	IFileSystem fileSystem)
	: WorkspaceSkillToolBase<UpdateSkillOptions>(command, logger, fileSystem) {
	internal const string ToolName = "update-skill";

	/// <summary>
	/// Updates managed skills in the selected scope when the source commit hash has changed.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Updates one or more managed skills in workspace or user scope from a local repository path or git URL")]
	public CommandExecutionResult UpdateSkill(
		[Description("Update-skill parameters")] [Required] UpdateSkillArgs args) {
		UpdateSkillOptions options = new() {
			Skill = args.SkillName,
			Repo = args.Repo,
			Scope = args.Scope
		};
		return ExecuteForScope(args.Scope, args.WorkspacePath, () => InternalExecute(options));
	}
}

/// <summary>
/// MCP tool surface for the <c>delete-skill</c> command.
/// </summary>
[McpServerToolType]
public sealed class DeleteSkillTool(
	DeleteSkillCommand command,
	ILogger logger,
	IFileSystem fileSystem)
	: WorkspaceSkillToolBase<DeleteSkillOptions>(command, logger, fileSystem) {
	internal const string ToolName = "delete-skill";

	/// <summary>
	/// Deletes a managed skill from the selected scope.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Deletes a managed skill from workspace or user scope")]
	public CommandExecutionResult DeleteSkill(
		[Description("Delete-skill parameters")] [Required] DeleteSkillArgs args) {
		DeleteSkillOptions options = new() {
			Skill = args.SkillName,
			Scope = args.Scope
		};
		return ExecuteForScope(args.Scope, args.WorkspacePath, () => InternalExecute(options));
	}
}

/// <summary>
/// MCP arguments for the <c>install-skills</c> tool.
/// </summary>
public sealed record InstallSkillsArgs(
	[property: JsonPropertyName("workspacePath")]
	[property: Description("Absolute path to the local clio workspace when scope is workspace")]
	string WorkspacePath = null,

	[property: JsonPropertyName("scope")]
	[property: Description("Skill target scope: workspace or user")]
	string Scope = SkillScopeParser.Workspace,

	[property: JsonPropertyName("skillName")]
	[property: Description("Optional specific skill name to install")]
	string SkillName = null,

	[property: JsonPropertyName("repo")]
	[property: Description("Optional local repository path or git URL. Defaults to the bootstrap skills repository")]
	string Repo = null
);

/// <summary>
/// MCP arguments for the <c>update-skill</c> tool.
/// </summary>
public sealed record UpdateSkillArgs(
	[property: JsonPropertyName("workspacePath")]
	[property: Description("Absolute path to the local clio workspace when scope is workspace")]
	string WorkspacePath = null,

	[property: JsonPropertyName("scope")]
	[property: Description("Skill target scope: workspace or user")]
	string Scope = SkillScopeParser.Workspace,

	[property: JsonPropertyName("skillName")]
	[property: Description("Optional specific managed skill name to update")]
	string SkillName = null,

	[property: JsonPropertyName("repo")]
	[property: Description("Optional local repository path or git URL. Defaults to the bootstrap skills repository")]
	string Repo = null
);

/// <summary>
/// MCP arguments for the <c>delete-skill</c> tool.
/// </summary>
public sealed record DeleteSkillArgs(
	[property: JsonPropertyName("skillName")]
	[property: Description("Managed skill name to delete")]
	[property: Required]
	string SkillName,

	[property: JsonPropertyName("scope")]
	[property: Description("Skill target scope: workspace or user")]
	string Scope = SkillScopeParser.Workspace,

	[property: JsonPropertyName("workspacePath")]
	[property: Description("Absolute path to the local clio workspace when scope is workspace")]
	string WorkspacePath = null
);
