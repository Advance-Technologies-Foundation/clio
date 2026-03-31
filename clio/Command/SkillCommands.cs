using System;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Shared defaults for workspace skill management commands.
/// </summary>
public static class WorkspaceSkillDefaults {
	/// <summary>
	/// Default repository used when callers omit <c>--repo</c>.
	/// </summary>
	public const string DefaultRepository = "https://creatio.ghe.com/engineering/bootstrap-composable-app-starter-kit";
}

/// <summary>
/// Supported install targets for managed skills.
/// </summary>
public enum SkillScope {
	/// <summary>
	/// Installs skills into the current clio workspace.
	/// </summary>
	Workspace,

	/// <summary>
	/// Installs skills into the user-level agent home.
	/// </summary>
	User
}

internal static class SkillScopeParser {
	internal const string Workspace = "workspace";
	internal const string User = "user";

	internal static bool TryParse(string scopeValue, out SkillScope scope, out string errorMessage) {
		string normalizedScope = scopeValue?.Trim();
		if (string.IsNullOrWhiteSpace(normalizedScope) ||
			string.Equals(normalizedScope, Workspace, StringComparison.OrdinalIgnoreCase)) {
			scope = SkillScope.Workspace;
			errorMessage = string.Empty;
			return true;
		}

		if (string.Equals(normalizedScope, User, StringComparison.OrdinalIgnoreCase)) {
			scope = SkillScope.User;
			errorMessage = string.Empty;
			return true;
		}

		scope = SkillScope.Workspace;
		errorMessage = $"Unsupported scope '{scopeValue}'. Supported values: {Workspace}, {User}.";
		return false;
	}

	internal static string ToOptionValue(SkillScope scope) => scope == SkillScope.User ? User : Workspace;
}

/// <summary>
/// Base options for commands that manage clio-managed skills.
/// </summary>
public abstract class SkillCommandOptions {
	/// <summary>
	/// Optional skill name used to limit the operation to a single skill.
	/// </summary>
	[Option("skill", Required = false, HelpText = "Specific skill name to process")]
	public string Skill { get; set; }

	/// <summary>
	/// Optional local repository path or git URL used as the source of skills.
	/// </summary>
	[Option("repo", Required = false,
		HelpText = "Optional local repository path or git URL. Defaults to the bootstrap workspace skills repository.")]
	public string Repo { get; set; }

	/// <summary>
	/// Skill target scope.
	/// </summary>
	[Option("scope", Required = false, HelpText = "Skill target scope: workspace or user. Defaults to workspace.")]
	public string Scope { get; set; } = SkillScopeParser.Workspace;
}

/// <summary>
/// Options for the <c>install-skills</c> command.
/// </summary>
[Verb("install-skills", HelpText = "Install managed skills from a repository")]
public class InstallSkillsOptions : SkillCommandOptions {
}

/// <summary>
/// Options for the <c>update-skill</c> command.
/// </summary>
[Verb("update-skill", HelpText = "Update managed skills from a repository")]
public class UpdateSkillOptions : SkillCommandOptions {
}

/// <summary>
/// Options for the <c>delete-skill</c> command.
/// </summary>
[Verb("delete-skill", HelpText = "Delete a managed skill")]
public class DeleteSkillOptions {
	/// <summary>
	/// Skill name to delete from the current workspace.
	/// </summary>
	[Option("skill", Required = true, HelpText = "Managed skill name to delete")]
	public string Skill { get; set; }

	/// <summary>
	/// Skill target scope.
	/// </summary>
	[Option("scope", Required = false, HelpText = "Skill target scope: workspace or user. Defaults to workspace.")]
	public string Scope { get; set; } = SkillScopeParser.Workspace;
}

/// <summary>
/// Installs workspace-local skills into the current clio workspace.
/// </summary>
public class InstallSkillsCommand(
	ISkillManagementService skillManagementService,
	IWorkspacePathBuilder workspacePathBuilder,
	ILogger logger)
	: Command<InstallSkillsOptions> {

	/// <inheritdoc />
	public override int Execute(InstallSkillsOptions options) {
		if (!SkillScopeParser.TryParse(options.Scope, out SkillScope scope, out string errorMessage)) {
			logger.WriteError(errorMessage);
			return 1;
		}

		string workspacePath = workspacePathBuilder.RootPath;
		if (scope == SkillScope.Workspace && !workspacePathBuilder.IsWorkspace) {
			logger.WriteError($"Current directory is not inside a clio workspace: {workspacePath}");
			return 1;
		}

		SkillOperationResult result = skillManagementService.Install(new InstallSkillsRequest(
			workspacePath,
			options.Skill,
			options.Repo,
			scope));
		WriteResult(result);
		return result.ExitCode;
	}

	private void WriteResult(SkillOperationResult result) {
		foreach (string message in result.InfoMessages) {
			logger.WriteInfo(message);
		}

		foreach (string message in result.ErrorMessages) {
			logger.WriteError(message);
		}
	}
}

/// <summary>
/// Updates managed workspace-local skills in the current clio workspace.
/// </summary>
public class UpdateSkillCommand(
	ISkillManagementService skillManagementService,
	IWorkspacePathBuilder workspacePathBuilder,
	ILogger logger)
	: Command<UpdateSkillOptions> {

	/// <inheritdoc />
	public override int Execute(UpdateSkillOptions options) {
		if (!SkillScopeParser.TryParse(options.Scope, out SkillScope scope, out string errorMessage)) {
			logger.WriteError(errorMessage);
			return 1;
		}

		string workspacePath = workspacePathBuilder.RootPath;
		if (scope == SkillScope.Workspace && !workspacePathBuilder.IsWorkspace) {
			logger.WriteError($"Current directory is not inside a clio workspace: {workspacePath}");
			return 1;
		}

		SkillOperationResult result = skillManagementService.Update(new UpdateSkillsRequest(
			workspacePath,
			options.Skill,
			options.Repo,
			scope));
		WriteResult(result);
		return result.ExitCode;
	}

	private void WriteResult(SkillOperationResult result) {
		foreach (string message in result.InfoMessages) {
			logger.WriteInfo(message);
		}

		foreach (string message in result.ErrorMessages) {
			logger.WriteError(message);
		}
	}
}

/// <summary>
/// Deletes a managed workspace-local skill from the current clio workspace.
/// </summary>
public class DeleteSkillCommand(
	ISkillManagementService skillManagementService,
	IWorkspacePathBuilder workspacePathBuilder,
	ILogger logger)
	: Command<DeleteSkillOptions> {

	/// <inheritdoc />
	public override int Execute(DeleteSkillOptions options) {
		if (!SkillScopeParser.TryParse(options.Scope, out SkillScope scope, out string errorMessage)) {
			logger.WriteError(errorMessage);
			return 1;
		}

		string workspacePath = workspacePathBuilder.RootPath;
		if (scope == SkillScope.Workspace && !workspacePathBuilder.IsWorkspace) {
			logger.WriteError($"Current directory is not inside a clio workspace: {workspacePath}");
			return 1;
		}

		SkillOperationResult result = skillManagementService.Delete(new DeleteSkillRequest(
			workspacePath,
			options.Skill,
			scope));
		WriteResult(result);
		return result.ExitCode;
	}

	private void WriteResult(SkillOperationResult result) {
		foreach (string message in result.InfoMessages) {
			logger.WriteInfo(message);
		}

		foreach (string message in result.ErrorMessages) {
			logger.WriteError(message);
		}
	}
}
