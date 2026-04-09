using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>link-from-repository</c> command.
/// </summary>
public class LinkFromRepositoryTool(
	Link4RepoCommand command,
	ILogger logger)
	: BaseTool<Link4RepoOptions>(command, logger) {

	internal const string LinkFromRepositoryByEnvironmentToolName = "link-from-repository-by-environment";
	internal const string LinkFromRepositoryByEnvPackagePathToolName = "link-from-repository-by-env-package-path";
	internal const string LinkFromRepositoryUnlockedToolName = "link-from-repository-unlocked";

	/// <summary>
	/// Links repository packages into a Creatio environment resolved by registered environment name.
	/// </summary>
	[McpServerTool(Name = LinkFromRepositoryByEnvironmentToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Links repository package content into a Creatio environment package directory resolved by registered environment name")]
	public CommandExecutionResult LinkFromRepositoryByEnvironment(
		[Description("Registered clio environment name")] [Required] string environmentName,
		[Description("Path to the package repository folder")] [Required] string repoPath,
		[Description("Packages to link: `*` for all packages or a comma-separated package list")] [Required] string packages,
		[Description("Print a summary of what would happen without executing any mutations")] bool? dryRun = null,
		[Description("Skip the automatic preparation step (Maintainer check, unlock, 2fs)")] bool? skipPreparation = null
	) {
		Link4RepoOptions options = new() {
			Environment = environmentName,
			RepoPath = repoPath,
			Packages = packages,
			DryRun = dryRun ?? false,
			SkipPreparation = skipPreparation ?? false
		};
		return InternalExecute(options);
	}

	/// <summary>
	/// Links repository packages into a Creatio environment package directory resolved by explicit package path.
	/// </summary>
	[McpServerTool(Name = LinkFromRepositoryByEnvPackagePathToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Links repository package content into a Creatio environment package directory resolved by explicit environment package path")]
	public CommandExecutionResult LinkFromRepositoryByEnvPackagePath(
		[Description("Path to the target Creatio environment package directory")] [Required] string envPkgPath,
		[Description("Path to the package repository folder")] [Required] string repoPath,
		[Description("Packages to link: `*` for all packages or a comma-separated package list")] [Required] string packages,
		[Description("Print a summary of what would happen without executing any mutations")] bool? dryRun = null,
		[Description("Skip the automatic preparation step (Maintainer check, unlock, 2fs)")] bool? skipPreparation = null
	) {
		Link4RepoOptions options = new() {
			EnvPkgPath = envPkgPath,
			RepoPath = repoPath,
			Packages = packages,
			DryRun = dryRun ?? false,
			SkipPreparation = skipPreparation ?? false
		};
		return InternalExecute(options);
	}

	/// <summary>
	/// Queries the Creatio site for unlocked packages and links them from the repository.
	/// Supports both flat repo structure (PackageName/) and versioned PackageStore (PackageName/branch/version/).
	/// </summary>
	[McpServerTool(Name = LinkFromRepositoryUnlockedToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Queries the Creatio site for unlocked packages and links only those from the repository. " +
		"Supports flat (repo/PackageName/) and versioned (repo/PackageName/branch/version/) repo structures.")]
	public CommandExecutionResult LinkFromRepositoryUnlocked(
		[Description("Registered clio environment name (required for API connection to query unlocked packages)")] [Required] string environmentName,
		[Description("Path to the package repository folder")] [Required] string repoPath,
		[Description("Print a summary of what would happen without executing any mutations")] bool? dryRun = null
	) {
		Link4RepoOptions options = new() {
			Environment = environmentName,
			RepoPath = repoPath,
			Unlocked = true,
			DryRun = dryRun ?? false
		};
		return InternalExecute(options);
	}
}
