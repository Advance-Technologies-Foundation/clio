using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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

	internal const string LinkFromRepositoryToolName = "link-from-repository";

	internal const string ModeByEnv = "by-env";
	internal const string ModeByPkgPath = "by-pkg-path";
	internal const string ModeUnlocked = "unlocked";

	/// <summary>Legacy MCP tool name retained for prompt and e2e documentation surfaces. The capability now lives on <c>link-from-repository</c> with <c>mode=by-env</c>.</summary>
	internal const string LinkFromRepositoryByEnvironmentToolName = "link-from-repository-by-environment";
	/// <summary>Legacy MCP tool name retained for prompt and e2e documentation surfaces. The capability now lives on <c>link-from-repository</c> with <c>mode=by-pkg-path</c>.</summary>
	internal const string LinkFromRepositoryByEnvPackagePathToolName = "link-from-repository-by-env-package-path";
	/// <summary>Legacy MCP tool name retained for prompt and e2e documentation surfaces. The capability now lives on <c>link-from-repository</c> with <c>mode=unlocked</c>.</summary>
	internal const string LinkFromRepositoryUnlockedToolName = "link-from-repository-unlocked";

	[McpServerTool(Name = LinkFromRepositoryToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Links repository package content into a Creatio environment. mode='by-env' resolves the target via a registered environment name; mode='by-pkg-path' uses an explicit environment package directory; mode='unlocked' queries the site for unlocked packages and links only those.")]
	public CommandExecutionResult LinkFromRepository(
		[Description("Link-from-repository parameters")] [Required] LinkFromRepositoryArgs args
	) {
		CommandExecutionResult modeError = CommandExecutionResult.ValidateExactlyOneMode(
			"mode", args.Mode, ModeByEnv, ModeByPkgPath, ModeUnlocked);
		if (modeError != null) {
			return modeError;
		}

		CommandExecutionResult missingRepo = CommandExecutionResult.ValidateRequiredForMode(
			"repo-path", args.RepoPath, args.Mode);
		if (missingRepo != null) {
			return missingRepo;
		}

		if (string.Equals(args.Mode, ModeByEnv, StringComparison.OrdinalIgnoreCase)) {
			return LinkByEnvironment(args);
		}
		if (string.Equals(args.Mode, ModeByPkgPath, StringComparison.OrdinalIgnoreCase)) {
			return LinkByPackagePath(args);
		}
		return LinkUnlocked(args);
	}

	private CommandExecutionResult LinkByEnvironment(LinkFromRepositoryArgs args) {
		CommandExecutionResult missingEnv = CommandExecutionResult.ValidateRequiredForMode(
			"environment-name", args.EnvironmentName, ModeByEnv);
		if (missingEnv != null) {
			return missingEnv;
		}
		CommandExecutionResult missingPackages = CommandExecutionResult.ValidateRequiredForMode(
			"packages", args.Packages, ModeByEnv);
		if (missingPackages != null) {
			return missingPackages;
		}
		Link4RepoOptions options = new() {
			Environment = args.EnvironmentName,
			RepoPath = args.RepoPath,
			Packages = args.Packages,
			DryRun = args.DryRun ?? false,
			SkipPreparation = args.SkipPreparation ?? false
		};
		return InternalExecute(options);
	}

	private CommandExecutionResult LinkByPackagePath(LinkFromRepositoryArgs args) {
		CommandExecutionResult missingPath = CommandExecutionResult.ValidateRequiredForMode(
			"env-pkg-path", args.EnvPkgPath, ModeByPkgPath);
		if (missingPath != null) {
			return missingPath;
		}
		CommandExecutionResult missingPackages = CommandExecutionResult.ValidateRequiredForMode(
			"packages", args.Packages, ModeByPkgPath);
		if (missingPackages != null) {
			return missingPackages;
		}
		Link4RepoOptions options = new() {
			EnvPkgPath = args.EnvPkgPath,
			RepoPath = args.RepoPath,
			Packages = args.Packages,
			DryRun = args.DryRun ?? false,
			SkipPreparation = args.SkipPreparation ?? false
		};
		return InternalExecute(options);
	}

	private CommandExecutionResult LinkUnlocked(LinkFromRepositoryArgs args) {
		CommandExecutionResult missingEnv = CommandExecutionResult.ValidateRequiredForMode(
			"environment-name", args.EnvironmentName, ModeUnlocked);
		if (missingEnv != null) {
			return missingEnv;
		}
		Link4RepoOptions options = new() {
			Environment = args.EnvironmentName,
			RepoPath = args.RepoPath,
			Unlocked = true,
			DryRun = args.DryRun ?? false
		};
		return InternalExecute(options);
	}
}

/// <summary>
/// MCP arguments for the consolidated <c>link-from-repository</c> tool.
/// </summary>
public sealed record LinkFromRepositoryArgs(
	[property: JsonPropertyName("mode")]
	[property: Description("Discriminator: 'by-env' resolves the target Creatio package directory via a registered environment; 'by-pkg-path' takes the package directory explicitly; 'unlocked' queries the site for unlocked packages and links only those.")]
	[property: Required]
	string Mode,

	[property: JsonPropertyName("repo-path")]
	[property: Description("Path to the package repository folder. Required in every mode.")]
	[property: Required]
	string RepoPath,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Required when mode='by-env' or mode='unlocked'. Registered clio environment name.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("env-pkg-path")]
	[property: Description("Required when mode='by-pkg-path'. Absolute path to the target Creatio environment package directory.")]
	string? EnvPkgPath = null,

	[property: JsonPropertyName("packages")]
	[property: Description("Required when mode='by-env' or mode='by-pkg-path'. Packages to link: '*' for all packages or a comma-separated list.")]
	string? Packages = null,

	[property: JsonPropertyName("dry-run")]
	[property: Description("Print a summary of what would happen without executing any mutations.")]
	bool? DryRun = null,

	[property: JsonPropertyName("skip-preparation")]
	[property: Description("Skip the automatic preparation step (Maintainer check, unlock, 2fs). Honored in mode='by-env' and mode='by-pkg-path'.")]
	bool? SkipPreparation = null
);
