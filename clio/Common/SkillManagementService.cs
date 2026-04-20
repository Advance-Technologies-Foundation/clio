using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Common;

/// <summary>
/// Provides workspace-local skill install, update, and delete operations.
/// </summary>
public interface ISkillManagementService {
	/// <summary>
	/// Installs one or more skills into the target workspace.
	/// </summary>
	/// <param name="request">Install request details.</param>
	/// <returns>Operation result with user-facing diagnostics.</returns>
	SkillOperationResult Install(InstallSkillsRequest request);

	/// <summary>
	/// Updates one or more managed skills in the target workspace.
	/// </summary>
	/// <param name="request">Update request details.</param>
	/// <returns>Operation result with user-facing diagnostics.</returns>
	SkillOperationResult Update(UpdateSkillsRequest request);

	/// <summary>
	/// Deletes a managed skill from the target workspace.
	/// </summary>
	/// <param name="request">Delete request details.</param>
	/// <returns>Operation result with user-facing diagnostics.</returns>
	SkillOperationResult Delete(DeleteSkillRequest request);
}

/// <summary>
/// Resolves a skill source repository and its current git commit hash.
/// </summary>
public interface ISkillRepositoryResolver {
	/// <summary>
	/// Resolves the effective repository for a skill operation.
	/// </summary>
	/// <param name="repositoryLocator">Optional local path or git URL.</param>
	/// <returns>Resolved repository metadata and cleanup handle.</returns>
	ResolvedSkillRepository Resolve(string repositoryLocator);
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
	/// <returns>Git command result.</returns>
	GitCommandResult Clone(string repositoryLocator, string targetDirectory);

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
/// Resolves the user-level agent home path for global skills and plugins.
/// </summary>
public interface IAgentHomePathProvider {
	/// <summary>
	/// Gets the absolute agent home path.
	/// </summary>
	/// <returns>Absolute agent home path.</returns>
	string GetAgentHomePath();
}

/// <summary>
/// Default provider for the user-level agent home path.
/// </summary>
public sealed class AgentHomePathProvider(Clio.Common.IFileSystem fileSystem, IAbstractionsFileSystem abstractionsFileSystem) : IAgentHomePathProvider {
	private readonly Clio.Common.IFileSystem _fileSystem = fileSystem;
	private readonly IAbstractionsFileSystem _abstractionsFileSystem = abstractionsFileSystem;

	/// <inheritdoc />
	public string GetAgentHomePath() {
		string configuredAgentHome = Environment.GetEnvironmentVariable("CODEX_HOME")?.Trim();
		string agentHomePath = string.IsNullOrWhiteSpace(configuredAgentHome)
			? _abstractionsFileSystem.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
			: configuredAgentHome;
		return _fileSystem.GetFullPath(agentHomePath);
	}
}

/// <summary>
/// Install request for managed skills.
/// </summary>
/// <param name="WorkspacePath">Absolute workspace root path.</param>
/// <param name="SkillName">Optional specific skill name.</param>
/// <param name="RepositoryLocator">Optional local repository path or git URL.</param>
/// <param name="Scope">Skill target scope.</param>
public sealed record InstallSkillsRequest(
	string WorkspacePath,
	string SkillName,
	string RepositoryLocator,
	SkillScope Scope = SkillScope.Workspace);

/// <summary>
/// Update request for managed skills.
/// </summary>
/// <param name="WorkspacePath">Absolute workspace root path.</param>
/// <param name="SkillName">Optional specific skill name.</param>
/// <param name="RepositoryLocator">Optional local repository path or git URL.</param>
/// <param name="Scope">Skill target scope.</param>
public sealed record UpdateSkillsRequest(
	string WorkspacePath,
	string SkillName,
	string RepositoryLocator,
	SkillScope Scope = SkillScope.Workspace);

/// <summary>
/// Delete request for a managed skill.
/// </summary>
/// <param name="WorkspacePath">Absolute workspace root path.</param>
/// <param name="SkillName">Managed skill name to delete.</param>
/// <param name="Scope">Skill target scope.</param>
public sealed record DeleteSkillRequest(
	string WorkspacePath,
	string SkillName,
	SkillScope Scope = SkillScope.Workspace);

/// <summary>
/// User-facing result for workspace skill operations.
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="InfoMessages">Info messages to log.</param>
/// <param name="ErrorMessages">Error messages to log.</param>
public sealed record SkillOperationResult(
	int ExitCode,
	IReadOnlyList<string> InfoMessages,
	IReadOnlyList<string> ErrorMessages);

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
/// Default git-backed repository resolver for workspace-local skills.
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
	public ResolvedSkillRepository Resolve(string repositoryLocator) {
		string effectiveLocator = string.IsNullOrWhiteSpace(repositoryLocator)
			? WorkspaceSkillDefaults.DefaultRepository
			: repositoryLocator.Trim();

		if (_fileSystem.ExistsDirectory(effectiveLocator)) {
			string fullPath = _fileSystem.GetFullPath(effectiveLocator);
			string localCommitHash = ResolveHeadCommitHash(fullPath);
			return new ResolvedSkillRepository(fullPath, fullPath, localCommitHash, _workingDirectoriesProvider);
		}

		string cachedRepositoryPath = GetCachedRepositoryPath(effectiveLocator);
		_fileSystem.CreateDirectoryIfNotExists(SettingsRepository.AppSettingsFolderPath);

		if (_fileSystem.ExistsDirectory(cachedRepositoryPath)) {
			GitCommandResult pullResult = _gitCommandRunner.Pull(cachedRepositoryPath);
			if (!pullResult.Succeeded) {
				throw new InvalidOperationException(
					$"Unable to update cached skills repository '{effectiveLocator}' at '{cachedRepositoryPath}'. {GetGitError(pullResult)}");
			}
		}
		else {
			GitCommandResult cloneResult = _gitCommandRunner.Clone(effectiveLocator, cachedRepositoryPath);
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
/// Default git command runner for workspace-local skill repository resolution.
/// </summary>
public class GitCommandRunner(IProcessExecutor processExecutor) : IGitCommandRunner {
	private readonly IProcessExecutor _processExecutor = processExecutor;

	/// <inheritdoc />
	public GitCommandResult Clone(string repositoryLocator, string targetDirectory) {
		return ExecuteGit($"clone --depth 1 {Quote(repositoryLocator)} {Quote(targetDirectory)}");
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

/// <summary>
/// Default workspace-local skill management implementation.
/// </summary>
public class SkillManagementService(
	Clio.Common.IFileSystem fileSystem,
	ISkillRepositoryResolver skillRepositoryResolver,
	IAgentHomePathProvider agentHomePathProvider)
	: ISkillManagementService {
	private const string SkillsRootRelativePath = ".agents/skills";
	private const string UserSkillsRootRelativePath = "skills";
	private const string ManifestFileName = ".clio-managed.json";

	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly Clio.Common.IFileSystem _fileSystem = fileSystem;
	private readonly ISkillRepositoryResolver _skillRepositoryResolver = skillRepositoryResolver;
	private readonly IAgentHomePathProvider _agentHomePathProvider = agentHomePathProvider;

	/// <inheritdoc />
	public SkillOperationResult Install(InstallSkillsRequest request) {
		try {
			return InstallInternal(request);
		}
		catch (Exception exception) {
			return Failure(exception.Message);
		}
	}

	/// <inheritdoc />
	public SkillOperationResult Update(UpdateSkillsRequest request) {
		try {
			return UpdateInternal(request);
		}
		catch (Exception exception) {
			return Failure(exception.Message);
		}
	}

	/// <inheritdoc />
	public SkillOperationResult Delete(DeleteSkillRequest request) {
		try {
			return DeleteInternal(request);
		}
		catch (Exception exception) {
			return Failure(exception.Message);
		}
	}

	private SkillOperationResult InstallInternal(InstallSkillsRequest request) {
		string targetRootPath = ResolveTargetRootPath(request.Scope, request.WorkspacePath);
		using ResolvedSkillRepository repository = _skillRepositoryResolver.Resolve(request.RepositoryLocator);
		Dictionary<string, SkillSourceDefinition> discoveredSkills = DiscoverSkills(repository);
		List<SkillSourceDefinition> selectedSkills = SelectSkills(discoveredSkills, request.SkillName);
		ManagedSkillsManifest manifest = LoadManifest(targetRootPath, request.Scope);
		string skillsRootPath = GetSkillsRootPath(targetRootPath, request.Scope);

		foreach (SkillSourceDefinition skill in selectedSkills) {
			string targetPath = GetSkillTargetPath(targetRootPath, request.Scope, skill.Name);
			ManagedSkillEntry existingManagedEntry = FindEntry(manifest, skill.Name);
			bool targetExists = _fileSystem.ExistsDirectory(targetPath);
			if (existingManagedEntry is not null) {
				return Failure($"Skill '{skill.Name}' is already managed in {GetScopeDisplayName(request.Scope)}. Use update-skill instead.");
			}

			if (targetExists) {
				return Failure($"Skill '{skill.Name}' already exists in {GetScopeDisplayName(request.Scope)} but is not managed by clio.");
			}
		}

		_fileSystem.CreateDirectoryIfNotExists(skillsRootPath);
		List<string> infoMessages = [];
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (SkillSourceDefinition skill in selectedSkills) {
			string targetPath = GetSkillTargetPath(targetRootPath, request.Scope, skill.Name);
			_fileSystem.CopyDirectory(skill.DirectoryPath, targetPath, false);
			manifest.Entries.Add(new ManagedSkillEntry {
				SkillName = skill.Name,
				TargetPath = BuildTargetRelativeSkillPath(request.Scope, skill.Name),
				SourceRepo = repository.SourceLocator,
				SourceRelativePath = skill.SourceRelativePath,
				CommitHash = repository.CommitHash,
				InstalledAtUtc = now,
				UpdatedAtUtc = now
			});
			infoMessages.Add($"Installed skill '{skill.Name}' from '{repository.SourceLocator}' at commit '{repository.CommitHash}'.");
		}

		SaveManifest(targetRootPath, request.Scope, manifest);
		infoMessages.Add("Done");
		return Success(infoMessages);
	}

	private SkillOperationResult UpdateInternal(UpdateSkillsRequest request) {
		string targetRootPath = ResolveTargetRootPath(request.Scope, request.WorkspacePath);
		ManagedSkillsManifest manifest = LoadManifest(targetRootPath, request.Scope);
		if (manifest.Entries.Count == 0) {
			return Failure($"There are no clio-managed skills in {GetScopeDisplayName(request.Scope)}.");
		}

		using ResolvedSkillRepository repository = _skillRepositoryResolver.Resolve(request.RepositoryLocator);
		List<ManagedSkillEntry> selectedEntries = SelectEntriesForUpdate(manifest, repository.SourceLocator, request.SkillName, request.Scope);
		Dictionary<string, SkillSourceDefinition> discoveredSkills = DiscoverSkills(repository);
		List<string> infoMessages = [];
		List<string> errorMessages = [];
		bool updatedAny = false;

		foreach (ManagedSkillEntry entry in selectedEntries) {
			if (string.Equals(entry.CommitHash, repository.CommitHash, StringComparison.Ordinal)) {
				infoMessages.Add($"Skill '{entry.SkillName}' is already up to date at commit '{repository.CommitHash}'.");
				continue;
			}

			if (!discoveredSkills.TryGetValue(entry.SkillName, out SkillSourceDefinition discoveredSkill) ||
				!AreEquivalentRelativePaths(discoveredSkill.SourceRelativePath, entry.SourceRelativePath)) {
				errorMessages.Add(
					$"Managed skill '{entry.SkillName}' is no longer available at '{entry.SourceRelativePath}' in '{repository.SourceLocator}'.");
				continue;
			}

			string targetPath = GetSkillTargetPath(targetRootPath, request.Scope, entry.SkillName);
			_fileSystem.DeleteDirectoryIfExists(targetPath);
			_fileSystem.CopyDirectory(discoveredSkill.DirectoryPath, targetPath, false);
			entry.CommitHash = repository.CommitHash;
			entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
			updatedAny = true;
			infoMessages.Add($"Updated skill '{entry.SkillName}' to commit '{repository.CommitHash}'.");
		}

		if (updatedAny) {
			SaveManifest(targetRootPath, request.Scope, manifest);
		}

		if (errorMessages.Count > 0) {
			return new SkillOperationResult(1, infoMessages, errorMessages);
		}

		if (infoMessages.Count == 0) {
			return Failure("No managed skills matched the requested source repository.");
		}

		infoMessages.Add("Done");
		return Success(infoMessages);
	}

	private SkillOperationResult DeleteInternal(DeleteSkillRequest request) {
		string targetRootPath = ResolveTargetRootPath(request.Scope, request.WorkspacePath);
		ManagedSkillsManifest manifest = LoadManifest(targetRootPath, request.Scope);
		ManagedSkillEntry entry = FindEntry(manifest, request.SkillName);
		string targetPath = GetSkillTargetPath(targetRootPath, request.Scope, request.SkillName);

		if (entry is null) {
			if (_fileSystem.ExistsDirectory(targetPath)) {
				return Failure($"Skill '{request.SkillName}' exists in {GetScopeDisplayName(request.Scope)} but is not managed by clio.");
			}

			return Failure($"Managed skill '{request.SkillName}' was not found in {GetScopeDisplayName(request.Scope)}.");
		}

		_fileSystem.DeleteDirectoryIfExists(targetPath);
		manifest.Entries.Remove(entry);
		SaveManifest(targetRootPath, request.Scope, manifest);
		return Success([
			$"Deleted managed skill '{request.SkillName}'.",
			"Done"
		]);
	}

	private Dictionary<string, SkillSourceDefinition> DiscoverSkills(ResolvedSkillRepository repository) {
		string skillsRoot = _fileSystem.Combine(repository.RepositoryRootPath, ".agents", "skills");
		if (!_fileSystem.ExistsDirectory(skillsRoot)) {
			throw new InvalidOperationException(
				$"Skills repository '{repository.SourceLocator}' does not contain '.agents/skills'.");
		}

		string[] skillFiles = _fileSystem.GetFiles(skillsRoot, "SKILL.md", SearchOption.AllDirectories);
		if (skillFiles.Length == 0) {
			throw new InvalidOperationException(
				$"Skills repository '{repository.SourceLocator}' does not contain any SKILL.md files under '.agents/skills'.");
		}

		Dictionary<string, SkillSourceDefinition> skills = new(StringComparer.OrdinalIgnoreCase);
		foreach (string skillFile in skillFiles) {
			string skillDirectory = _fileSystem.GetDirectoryInfo(skillFile).Parent?.FullName
				?? throw new InvalidOperationException($"Unable to resolve the skill directory for '{skillFile}'.");
			string skillName = _fileSystem.GetDirectoryInfo(skillDirectory).Name;
			if (skills.ContainsKey(skillName)) {
				throw new InvalidOperationException(
					$"Skills repository '{repository.SourceLocator}' contains duplicate skill name '{skillName}'.");
			}

			skills[skillName] = new SkillSourceDefinition(
				skillName,
				skillDirectory,
				_fileSystem.ConvertToRelativePath(skillDirectory, repository.RepositoryRootPath));
		}

		return skills;
	}

	private List<SkillSourceDefinition> SelectSkills(
		Dictionary<string, SkillSourceDefinition> discoveredSkills,
		string skillName) {
		if (string.IsNullOrWhiteSpace(skillName)) {
			return discoveredSkills.Values.OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		if (!discoveredSkills.TryGetValue(skillName, out SkillSourceDefinition skill)) {
			throw new InvalidOperationException($"Skill '{skillName}' was not found in the source repository.");
		}

		return [skill];
	}

	private List<ManagedSkillEntry> SelectEntriesForUpdate(
		ManagedSkillsManifest manifest,
		string sourceRepo,
		string skillName,
		SkillScope scope) {
		IEnumerable<ManagedSkillEntry> entries = manifest.Entries
			.Where(entry => string.Equals(entry.SourceRepo, sourceRepo, StringComparison.OrdinalIgnoreCase));

		if (!string.IsNullOrWhiteSpace(skillName)) {
			ManagedSkillEntry entry = entries.FirstOrDefault(item =>
				string.Equals(item.SkillName, skillName, StringComparison.OrdinalIgnoreCase));
			if (entry is null) {
				throw new InvalidOperationException(
					$"Managed skill '{skillName}' was not found for repository '{sourceRepo}'.");
			}

			return [entry];
		}

		List<ManagedSkillEntry> selectedEntries = entries
			.OrderBy(entry => entry.SkillName, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (selectedEntries.Count == 0) {
			throw new InvalidOperationException(
				$"No managed skills are registered for repository '{sourceRepo}' in {GetScopeDisplayName(scope)}.");
		}

		return selectedEntries;
	}

	private string ResolveTargetRootPath(SkillScope scope, string workspacePath) {
		return scope == SkillScope.User
			? _agentHomePathProvider.GetAgentHomePath()
			: ValidateWorkspace(workspacePath);
	}

	private string ValidateWorkspace(string workspacePath) {
		if (string.IsNullOrWhiteSpace(workspacePath)) {
			throw new InvalidOperationException("Workspace path is required.");
		}

		string fullWorkspacePath = _fileSystem.GetFullPath(workspacePath);
		string workspaceMarkerPath = _fileSystem.Combine(fullWorkspacePath, ".clio", "workspaceSettings.json");
		if (!_fileSystem.ExistsFile(workspaceMarkerPath)) {
			throw new InvalidOperationException($"Workspace path is not a clio workspace: {fullWorkspacePath}");
		}

		return fullWorkspacePath;
	}

	private ManagedSkillsManifest LoadManifest(string targetRootPath, SkillScope scope) {
		string manifestPath = GetManifestPath(targetRootPath, scope);
		if (!_fileSystem.ExistsFile(manifestPath)) {
			return new ManagedSkillsManifest();
		}

		string content = _fileSystem.ReadAllText(manifestPath);
		ManagedSkillsManifest manifest = JsonSerializer.Deserialize<ManagedSkillsManifest>(content, JsonOptions);
		return manifest ?? new ManagedSkillsManifest();
	}

	private void SaveManifest(string targetRootPath, SkillScope scope, ManagedSkillsManifest manifest) {
		string skillsRootPath = GetSkillsRootPath(targetRootPath, scope);
		string manifestPath = GetManifestPath(targetRootPath, scope);
		_fileSystem.CreateDirectoryIfNotExists(skillsRootPath);

		if (manifest.Entries.Count == 0) {
			_fileSystem.DeleteFileIfExists(manifestPath);
			return;
		}

		manifest.Entries = manifest.Entries
			.OrderBy(entry => entry.SkillName, StringComparer.OrdinalIgnoreCase)
			.ToList();
		_fileSystem.WriteAllTextToFile(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
	}

	private ManagedSkillEntry FindEntry(ManagedSkillsManifest manifest, string skillName) {
		return manifest.Entries.FirstOrDefault(entry =>
			string.Equals(entry.SkillName, skillName, StringComparison.OrdinalIgnoreCase));
	}

	private string GetSkillsRootPath(string targetRootPath, SkillScope scope) {
		return scope == SkillScope.User
			? _fileSystem.Combine(targetRootPath, UserSkillsRootRelativePath)
			: _fileSystem.Combine(targetRootPath, ".agents", "skills");
	}

	private string GetManifestPath(string targetRootPath, SkillScope scope) =>
		_fileSystem.Combine(GetSkillsRootPath(targetRootPath, scope), ManifestFileName);

	private string GetSkillTargetPath(string targetRootPath, SkillScope scope, string skillName) =>
		_fileSystem.Combine(GetSkillsRootPath(targetRootPath, scope), skillName);

	private static string BuildTargetRelativeSkillPath(SkillScope scope, string skillName) {
		return scope == SkillScope.User
			? $"{UserSkillsRootRelativePath}/{skillName}"
			: $"{SkillsRootRelativePath}/{skillName}";
	}

	private static string GetScopeDisplayName(SkillScope scope) => scope == SkillScope.User ? "user scope" : "this workspace";

	private static SkillOperationResult Success(IReadOnlyList<string> infoMessages) =>
		new(0, infoMessages, Array.Empty<string>());

	private static SkillOperationResult Failure(string errorMessage) =>
		new(1, Array.Empty<string>(), new[] { errorMessage });

	private static bool AreEquivalentRelativePaths(string firstPath, string secondPath) {
		return string.Equals(
			NormalizeRelativePath(firstPath),
			NormalizeRelativePath(secondPath),
			StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeRelativePath(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return string.Empty;
		}

		string normalized = path
			.Replace('\\', '/')
			.Trim();
		while (normalized.StartsWith("./", StringComparison.Ordinal)) {
			normalized = normalized[2..];
		}

		return normalized.TrimStart('/');
	}

	private sealed record SkillSourceDefinition(string Name, string DirectoryPath, string SourceRelativePath);
}

/// <summary>
/// Manifest that tracks clio-managed skills.
/// </summary>
public sealed class ManagedSkillsManifest {
	/// <summary>
	/// Gets or sets the managed skill entries.
	/// </summary>
	public List<ManagedSkillEntry> Entries { get; set; } = [];
}

/// <summary>
/// Single managed workspace skill entry.
/// </summary>
public sealed class ManagedSkillEntry {
	/// <summary>
	/// Gets or sets the skill name.
	/// </summary>
	public string SkillName { get; set; }

	/// <summary>
	/// Gets or sets the scope-relative target path.
	/// </summary>
	public string TargetPath { get; set; }

	/// <summary>
	/// Gets or sets the normalized source repository locator.
	/// </summary>
	public string SourceRepo { get; set; }

	/// <summary>
	/// Gets or sets the skill directory path relative to the repository root.
	/// </summary>
	public string SourceRelativePath { get; set; }

	/// <summary>
	/// Gets or sets the installed repository commit hash.
	/// </summary>
	public string CommitHash { get; set; }

	/// <summary>
	/// Gets or sets the timestamp of the first install.
	/// </summary>
	public DateTimeOffset InstalledAtUtc { get; set; }

	/// <summary>
	/// Gets or sets the timestamp of the last update.
	/// </summary>
	public DateTimeOffset UpdatedAtUtc { get; set; }
}
