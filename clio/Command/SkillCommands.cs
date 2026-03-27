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
/// Base options for commands that manage workspace-local skills.
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
}

/// <summary>
/// Options for the <c>install-skills</c> command.
/// </summary>
[Verb("install-skills", HelpText = "Install workspace-local skills from a repository")]
public class InstallSkillsOptions : SkillCommandOptions {
}

/// <summary>
/// Options for the <c>update-skill</c> command.
/// </summary>
[Verb("update-skill", HelpText = "Update managed workspace-local skills from a repository")]
public class UpdateSkillOptions : SkillCommandOptions {
}

/// <summary>
/// Options for the <c>delete-skill</c> command.
/// </summary>
[Verb("delete-skill", HelpText = "Delete a managed workspace-local skill")]
public class DeleteSkillOptions {
	/// <summary>
	/// Skill name to delete from the current workspace.
	/// </summary>
	[Option("skill", Required = true, HelpText = "Managed skill name to delete")]
	public string Skill { get; set; }
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
		string workspacePath = workspacePathBuilder.RootPath;
		if (!workspacePathBuilder.IsWorkspace) {
			logger.WriteError($"Current directory is not inside a clio workspace: {workspacePath}");
			return 1;
		}

		SkillOperationResult result = skillManagementService.Install(new InstallSkillsRequest(
			workspacePath,
			options.Skill,
			options.Repo));
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
		string workspacePath = workspacePathBuilder.RootPath;
		if (!workspacePathBuilder.IsWorkspace) {
			logger.WriteError($"Current directory is not inside a clio workspace: {workspacePath}");
			return 1;
		}

		SkillOperationResult result = skillManagementService.Update(new UpdateSkillsRequest(
			workspacePath,
			options.Skill,
			options.Repo));
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
		string workspacePath = workspacePathBuilder.RootPath;
		if (!workspacePathBuilder.IsWorkspace) {
			logger.WriteError($"Current directory is not inside a clio workspace: {workspacePath}");
			return 1;
		}

		SkillOperationResult result = skillManagementService.Delete(new DeleteSkillRequest(
			workspacePath,
			options.Skill));
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
