using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>link-from-repository</c> command.
/// </summary>
/// <remarks>
/// The <c>link-from-repository-*</c> family is NOT supported under credential passthrough (class c3,
/// ENG-93347): the environment name doubles as a local-package-directory selector with no passthrough
/// equivalent, so every Creatio-reaching branch fails fast via
/// <see cref="ICredentialPassthroughToolGuard"/> BEFORE any preparation call. The only allowed
/// passthrough path is the local-only <c>link-from-repository-by-env-package-path</c> branch with
/// <c>skip-preparation=true</c>, which makes no Creatio call.
/// </remarks>
public class LinkFromRepositoryTool(
	Link4RepoCommand command,
	ILogger logger,
	ICredentialPassthroughToolGuard passthroughGuard = null)
	: BaseTool<Link4RepoOptions>(command, logger, passthroughGuard: passthroughGuard) {

	internal const string LinkFromRepositoryByEnvironmentToolName = "link-from-repository-by-environment";
	internal const string LinkFromRepositoryByEnvPackagePathToolName = "link-from-repository-by-env-package-path";
	internal const string LinkFromRepositoryUnlockedToolName = "link-from-repository-unlocked";

	private const string PassthroughAlternativeGuidance =
		"Register the target environment and use the stdio path, or a non-passthrough mcp-http request.";

	/// <summary>
	/// Links repository packages into a Creatio environment resolved by registered environment name.
	/// </summary>
	[McpServerTool(Name = LinkFromRepositoryByEnvironmentToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("Links repository package content into a Creatio environment package directory resolved by registered environment name. Not supported under credential passthrough.")]
	public CommandExecutionResult LinkFromRepositoryByEnvironment(
		[Description("Path to the package repository folder")] [Required] string repoPath,
		[Description("Packages to link: `*` for all packages or a comma-separated package list")] [Required] string packages,
		[Description(McpToolDescriptions.EnvironmentName + " Required outside credential passthrough.")] string environmentName = null,
		[Description("Print a summary of what would happen without executing any mutations")] bool? dryRun = null,
		[Description("Skip the automatic preparation step (Maintainer check, unlock, 2fs)")] bool? skipPreparation = null
	) {
		// Guard FIRST (AC-01/AC-02): under passthrough this tool always reaches Creatio via the
		// registered environment's stored credentials, so it is rejected before any Creatio call —
		// even when an explicit environment-name is supplied (confused-deputy, Security mode iii).
		CommandExecutionResult rejection = RejectIfPassthroughUnsupported(
			LinkFromRepositoryByEnvironmentToolName, PassthroughAlternativeGuidance);
		if (rejection is not null) {
			return rejection;
		}
		// Not under passthrough (the guard above would have returned otherwise): environment-name is
		// required here exactly as it always has been. Enforce it explicitly — do NOT rely on
		// Link4RepoOptionsValidator's generic "path or environment" message to carry this contract (OQ-03).
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromValidationError(
				$"environment-name is required for {LinkFromRepositoryByEnvironmentToolName} outside credential passthrough.");
		}
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
	[Description("Links repository package content into a Creatio environment package directory resolved by explicit environment package path. Under credential passthrough only the local-only skip-preparation=true branch is supported.")]
	public CommandExecutionResult LinkFromRepositoryByEnvPackagePath(
		[Description("Path to the target Creatio environment package directory")] [Required] string envPkgPath,
		[Description("Path to the package repository folder")] [Required] string repoPath,
		[Description("Packages to link: `*` for all packages or a comma-separated package list")] [Required] string packages,
		[Description("Print a summary of what would happen without executing any mutations")] bool? dryRun = null,
		[Description("Skip the automatic preparation step (Maintainer check, unlock, 2fs)")] bool? skipPreparation = null
	) {
		// Guard fires exactly when the preparation branch would reach Creatio (AC-03/AC-04):
		// skip-preparation=false/absent runs maintainer read/write + lock/design-mode calls
		// (Link4RepoCommand preparation path). skip-preparation=true is local-only and stays
		// allowed under passthrough (AC-05) — do not gate the whole method.
		if (!(skipPreparation ?? false)) {
			CommandExecutionResult rejection = RejectIfPassthroughUnsupported(
				LinkFromRepositoryByEnvPackagePathToolName,
				"Use skip-preparation=true for the local-only branch, or register the target environment and use the stdio path.");
			if (rejection is not null) {
				return rejection;
			}
		}
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
		"Supports flat (repo/PackageName/) and versioned (repo/PackageName/branch/version/) repo structures. " +
		"Not supported under credential passthrough.")]
	public CommandExecutionResult LinkFromRepositoryUnlocked(
		[Description("Path to the package repository folder")] [Required] string repoPath,
		[Description("Registered clio environment name (required for API connection to query unlocked packages). Required outside credential passthrough.")] string environmentName = null,
		[Description("Print a summary of what would happen without executing any mutations")] bool? dryRun = null
	) {
		// Guard FIRST (AC-01/AC-02): the unlocked variant always queries the Creatio site, so it is
		// always fail-fast under passthrough, mixed input included.
		CommandExecutionResult rejection = RejectIfPassthroughUnsupported(
			LinkFromRepositoryUnlockedToolName, PassthroughAlternativeGuidance);
		if (rejection is not null) {
			return rejection;
		}
		// Explicit non-passthrough requiredness (OQ-03) — never Link4RepoOptionsValidator's generic message.
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromValidationError(
				$"environment-name is required for {LinkFromRepositoryUnlockedToolName} outside credential passthrough.");
		}
		Link4RepoOptions options = new() {
			Environment = environmentName,
			RepoPath = repoPath,
			Unlocked = true,
			DryRun = dryRun ?? false
		};
		return InternalExecute(options);
	}
}
