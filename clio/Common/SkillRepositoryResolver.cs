using System;
using System.IO;
using System.Linq;
using Clio.Common.Skills;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Common;

/// <summary>
/// Resolves a skill source repository and its current git commit hash.
/// </summary>
/// <remarks>
/// Retained from the original skills implementation and reused by the Cursor
/// file-copy install path, which needs a local checkout of the toolkit source.
/// The CLI-based agents (Claude/Codex/Copilot) do not use this — they pass the
/// marketplace URL straight to their own CLIs.
/// </remarks>
public interface ISkillRepositoryResolver {
	/// <summary>
	/// Resolves the effective repository for a skill operation.
	/// </summary>
	/// <param name="repositoryLocator">Optional local path or git URL.</param>
	/// <param name="gitRef">Optional branch/ref to clone when the source is a remote git URL.</param>
	/// <returns>Resolved repository metadata and cleanup handle.</returns>
	ResolvedSkillRepository Resolve(string repositoryLocator, string gitRef = null);
}

/// <summary>
/// Performs git operations required for skill source resolution.
/// </summary>
public interface IGitCommandRunner {
	/// <summary>
	/// Clones a git repository into the target directory.
	/// </summary>
	/// <param name="repositoryLocator">Repository URL or local clone source.</param>
	/// <param name="targetDirectory">Target directory for the clone.</param>
	/// <param name="branch">Optional branch/ref to clone.</param>
	/// <returns>Git command result.</returns>
	GitCommandResult Clone(string repositoryLocator, string targetDirectory, string branch = null);

	/// <summary>
	/// Fetches the latest changes for an already cached git repository.
	/// </summary>
	/// <param name="repositoryPath">Local repository path.</param>
	/// <returns>Git command result.</returns>
	GitCommandResult Pull(string repositoryPath);

	/// <summary>
	/// Resolves the HEAD commit hash for a git repository.
	/// </summary>
	/// <param name="repositoryPath">Local repository path.</param>
	/// <returns>Git command result whose standard output is the HEAD hash.</returns>
	GitCommandResult GetHeadCommitHash(string repositoryPath);
}

/// <summary>
/// Result of a git command execution.
/// </summary>
/// <param name="Succeeded">True when the command completed successfully.</param>
/// <param name="StandardOutput">Captured standard output.</param>
/// <param name="StandardError">Captured standard error.</param>
public sealed record GitCommandResult(bool Succeeded, string StandardOutput, string StandardError);

/// <summary>
/// Resolved skill repository details for the current operation.
/// </summary>
public sealed class ResolvedSkillRepository : IDisposable {
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly string _temporaryDirectory;

	/// <summary>
	/// Initializes a new instance of the <see cref="ResolvedSkillRepository"/> class.
	/// </summary>
	/// <param name="sourceLocator">Normalized source repository locator.</param>
	/// <param name="repositoryRootPath">Resolved local repository root path.</param>
	/// <param name="commitHash">Resolved repository HEAD commit hash.</param>
	/// <param name="workingDirectoriesProvider">Temporary directory cleanup provider.</param>
	/// <param name="temporaryDirectory">Temporary clone directory, when applicable.</param>
	public ResolvedSkillRepository(
		string sourceLocator,
		string repositoryRootPath,
		string commitHash,
		IWorkingDirectoriesProvider workingDirectoriesProvider,
		string temporaryDirectory = null) {
		SourceLocator = sourceLocator;
		RepositoryRootPath = repositoryRootPath;
		CommitHash = commitHash;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_temporaryDirectory = temporaryDirectory;
	}

	/// <summary>
	/// Gets the normalized source repository locator.
	/// </summary>
	public string SourceLocator { get; }

	/// <summary>
	/// Gets the local repository root path.
	/// </summary>
	public string RepositoryRootPath { get; }

	/// <summary>
	/// Gets the resolved repository HEAD commit hash.
	/// </summary>
	public string CommitHash { get; }

	/// <inheritdoc />
	public void Dispose() {
		if (!string.IsNullOrWhiteSpace(_temporaryDirectory)) {
			_workingDirectoriesProvider.DeleteDirectoryIfExists(_temporaryDirectory);
		}
	}
}

/// <summary>
/// Default git-backed repository resolver for toolkit skill sources.
/// </summary>
public class SkillRepositoryResolver(
	Clio.Common.IFileSystem fileSystem,
	IWorkingDirectoriesProvider workingDirectoriesProvider,
	IGitCommandRunner gitCommandRunner,
	IAbstractionsFileSystem abstractionsFileSystem)
	: ISkillRepositoryResolver {
	private readonly Clio.Common.IFileSystem _fileSystem = fileSystem;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider = workingDirectoriesProvider;
	private readonly IGitCommandRunner _gitCommandRunner = gitCommandRunner;
	private readonly IAbstractionsFileSystem _abstractionsFileSystem = abstractionsFileSystem;

	/// <inheritdoc />
	public ResolvedSkillRepository Resolve(string repositoryLocator, string gitRef = null) {
		string effectiveLocator = string.IsNullOrWhiteSpace(repositoryLocator)
			? ToolkitDistribution.MarketplaceGitUrl
			: repositoryLocator.Trim();

		if (_fileSystem.ExistsDirectory(effectiveLocator)) {
			string fullPath = _fileSystem.GetFullPath(effectiveLocator);
			string localCommitHash = ResolveHeadCommitHash(fullPath);
			return new ResolvedSkillRepository(fullPath, fullPath, localCommitHash, _workingDirectoriesProvider);
		}

		string cachedRepositoryPath = GetCachedRepositoryPath(effectiveLocator);
		_fileSystem.CreateDirectoryIfNotExists(SettingsRepository.AppSettingsFolderPath);

		if (_fileSystem.ExistsDirectory(cachedRepositoryPath)) {
			// The cache was cloned tracking the requested branch, so a fast-forward
			// pull keeps it on that branch.
			GitCommandResult pullResult = _gitCommandRunner.Pull(cachedRepositoryPath);
			if (!pullResult.Succeeded) {
				throw new InvalidOperationException(
					$"Unable to update cached skills repository '{effectiveLocator}' at '{cachedRepositoryPath}'. {GetGitError(pullResult)}");
			}
		}
		else {
			GitCommandResult cloneResult = _gitCommandRunner.Clone(effectiveLocator, cachedRepositoryPath, gitRef);
			if (!cloneResult.Succeeded) {
				throw new InvalidOperationException(
					$"Unable to clone skills repository '{effectiveLocator}'. {GetGitError(cloneResult)}");
			}
		}

		string commitHash = ResolveHeadCommitHash(cachedRepositoryPath);
		return new ResolvedSkillRepository(
			effectiveLocator,
			cachedRepositoryPath,
			commitHash,
			_workingDirectoriesProvider);
	}

	private string GetCachedRepositoryPath(string repositoryLocator) {
		string repositoryName = GetRepositoryName(repositoryLocator);
		return _fileSystem.Combine(SettingsRepository.AppSettingsFolderPath, repositoryName);
	}

	private string GetRepositoryName(string repositoryLocator) {
		string trimmedLocator = repositoryLocator.Trim().TrimEnd('/', '\\');
		string repositoryName = trimmedLocator;
		int lastSlashIndex = trimmedLocator.LastIndexOfAny(['/', '\\']);
		if (lastSlashIndex >= 0 && lastSlashIndex < trimmedLocator.Length - 1) {
			repositoryName = trimmedLocator[(lastSlashIndex + 1)..];
		}

		if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
			repositoryName = repositoryName[..^4];
		}

		char[] invalidCharacters = _abstractionsFileSystem.Path.GetInvalidFileNameChars();
		return string.Concat(repositoryName.Select(ch => invalidCharacters.Contains(ch) ? '-' : ch)).Trim();
	}

	private string ResolveHeadCommitHash(string repositoryPath) {
		GitCommandResult headResult = _gitCommandRunner.GetHeadCommitHash(repositoryPath);
		if (!headResult.Succeeded || string.IsNullOrWhiteSpace(headResult.StandardOutput)) {
			throw new InvalidOperationException(
				$"Unable to resolve git HEAD for skills repository '{repositoryPath}'. {GetGitError(headResult)}");
		}

		return headResult.StandardOutput.Trim();
	}

	private static string GetGitError(GitCommandResult result) {
		string stderr = result.StandardError?.Trim();
		string stdout = result.StandardOutput?.Trim();
		return !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
	}
}

/// <summary>
/// Default git command runner for toolkit skill repository resolution.
/// </summary>
public class GitCommandRunner(IProcessExecutor processExecutor) : IGitCommandRunner {
	private readonly IProcessExecutor _processExecutor = processExecutor;

	/// <inheritdoc />
	public GitCommandResult Clone(string repositoryLocator, string targetDirectory, string branch = null) {
		// Hardening: disable the ext:: transport (arbitrary command execution) and
		// use `--` so a locator beginning with '-' cannot be parsed as a git option.
		string branchArgument = string.IsNullOrWhiteSpace(branch) ? string.Empty : $"--branch {Quote(branch)} ";
		return ExecuteGit(
			$"-c protocol.ext.allow=never clone --depth 1 {branchArgument}-- {Quote(repositoryLocator)} {Quote(targetDirectory)}");
	}

	/// <inheritdoc />
	public GitCommandResult Pull(string repositoryPath) {
		return ExecuteGit($"-C {Quote(repositoryPath)} pull --ff-only");
	}

	/// <inheritdoc />
	public GitCommandResult GetHeadCommitHash(string repositoryPath) {
		return ExecuteGit($"-C {Quote(repositoryPath)} rev-parse HEAD");
	}

	private GitCommandResult ExecuteGit(string arguments) {
		ProcessExecutionResult result = _processExecutor.ExecuteAndCaptureAsync(new ProcessExecutionOptions("git", arguments))
			.GetAwaiter()
			.GetResult();
		return new GitCommandResult(
			result.Started && result.ExitCode == 0 && !result.Canceled && !result.TimedOut,
			result.StandardOutput,
			result.StandardError);
	}

	private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
