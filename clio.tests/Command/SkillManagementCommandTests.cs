using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class InstallSkillsCommandDocTests : BaseCommandTests<InstallSkillsOptions> {
}

[TestFixture]
[Property("Module", "Command")]
public sealed class UpdateSkillCommandDocTests : BaseCommandTests<UpdateSkillOptions> {
}

[TestFixture]
[Property("Module", "Command")]
public sealed class DeleteSkillCommandDocTests : BaseCommandTests<DeleteSkillOptions> {
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SkillManagementServiceTests {
	private MockFileSystem _mockFileSystem = null!;
	private Clio.Common.IFileSystem _fileSystem = null!;
	private ISkillRepositoryResolver _resolver = null!;
	private IAgentHomePathProvider _agentHomePathProvider = null!;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider = null!;
	private SkillManagementService _sut = null!;

	[SetUp]
	public void SetUp() {
		_mockFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), GetCurrentDirectoryPath());
		_fileSystem = new Clio.Common.FileSystem(_mockFileSystem);
		_resolver = Substitute.For<ISkillRepositoryResolver>();
		_agentHomePathProvider = Substitute.For<IAgentHomePathProvider>();
		_agentHomePathProvider.GetAgentHomePath().Returns(GetUserScopeHomePath());
		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_sut = new SkillManagementService(_fileSystem, _resolver, _agentHomePathProvider);
	}

	[Test]
	[Description("Install should copy all discovered skills into the workspace and record them in the managed manifest.")]
	public void Install_ShouldInstallAllSkills_WhenSkillNameIsOmitted() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateSkill(repoPath, "alpha");
		CreateSkill(repoPath, "beta");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "abc123", "repo"));

		// Act
		SkillOperationResult result = _sut.Install(new InstallSkillsRequest(workspacePath, null, "repo"));

		// Assert
		result.ExitCode.Should().Be(0, because: "install should succeed when the source repository contains valid skills and the workspace is valid");
		_mockFileSystem.Directory.Exists(GetPath("workspace", ".agents", "skills", "alpha")).Should().BeTrue(
			because: "install should copy the alpha skill into the workspace-local skills folder");
		_mockFileSystem.Directory.Exists(GetPath("workspace", ".agents", "skills", "beta")).Should().BeTrue(
			because: "install should copy the beta skill into the workspace-local skills folder");
		_mockFileSystem.File.Exists(GetPath("workspace", ".agents", "skills", ".clio-managed.json")).Should().BeTrue(
			because: "install should record managed skills in the manifest");
		result.InfoMessages.Should().Contain(message => message.Contains("Installed skill 'alpha'"),
			because: "install should report each installed skill");
		result.InfoMessages.Should().Contain(message => message.Contains("Installed skill 'beta'"),
			because: "install should report each installed skill");
	}

	[Test]
	[Description("Install should copy only the requested skill when --skill is provided.")]
	public void Install_ShouldInstallSingleSkill_WhenSkillNameIsProvided() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateSkill(repoPath, "alpha");
		CreateSkill(repoPath, "beta");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "abc123", "repo"));

		// Act
		SkillOperationResult result = _sut.Install(new InstallSkillsRequest(workspacePath, "beta", "repo"));

		// Assert
		result.ExitCode.Should().Be(0, because: "install should support selecting one source skill by name");
		_mockFileSystem.Directory.Exists(GetPath("workspace", ".agents", "skills", "beta")).Should().BeTrue(
			because: "the requested skill should be copied into the workspace");
		_mockFileSystem.Directory.Exists(GetPath("workspace", ".agents", "skills", "alpha")).Should().BeFalse(
			because: "install should not copy unselected skills when --skill is used");
	}

	[Test]
	[Description("Install should fail when the workspace path is not a clio workspace.")]
	public void Install_ShouldFail_WhenWorkspaceIsMissing() {
		// Arrange
		string workspacePath = GetWorkspacePath();

		// Act
		SkillOperationResult result = _sut.Install(new InstallSkillsRequest(workspacePath, null, "repo"));

		// Assert
		result.ExitCode.Should().Be(1, because: "install should reject a path that is not a clio workspace");
		result.ErrorMessages.Should().ContainSingle(
			message => message.Contains("not a clio workspace"),
			because: "the error should explain why the workspace path was rejected");
	}

	[Test]
	[Description("Install should create the workspace-local skills folder when it does not exist yet.")]
	public void Install_ShouldCreateSkillsDirectory_WhenMissing() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateSkill(repoPath, "alpha");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "abc123", "repo"));

		// Act
		SkillOperationResult result = _sut.Install(new InstallSkillsRequest(workspacePath, null, "repo"));

		// Assert
		result.ExitCode.Should().Be(0, because: "install should create the destination skill root automatically");
		_mockFileSystem.Directory.Exists(GetPath("workspace", ".agents", "skills")).Should().BeTrue(
			because: "the workspace-local skills folder should be created when it is absent");
	}

	[Test]
	[Description("Install should create user-scope skills under the agent home when scope is user.")]
	public void Install_ShouldCreateUserScopeSkills_WhenScopeIsUser() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateSkill(repoPath, "alpha");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "abc123", "repo"));

		// Act
		SkillOperationResult result = _sut.Install(new InstallSkillsRequest(workspacePath, null, "repo", SkillScope.User));

		// Assert
		result.ExitCode.Should().Be(0, because: "user-scope install should not depend on being inside a clio workspace");
		_mockFileSystem.Directory.Exists(GetPath("codex-home", "skills", "alpha")).Should().BeTrue(
			because: "user-scope install should copy the skill into the user-level agent home");
		_mockFileSystem.File.Exists(GetPath("codex-home", "skills", ".clio-managed.json")).Should().BeTrue(
			because: "user-scope install should create a dedicated managed manifest under the agent home");
	}

	[Test]
	[Description("Install should fail when a requested skill is not present in the source repository.")]
	public void Install_ShouldFail_WhenRequestedSkillIsMissing() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateSkill(repoPath, "alpha");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "abc123", "repo"));

		// Act
		SkillOperationResult result = _sut.Install(new InstallSkillsRequest(workspacePath, "beta", "repo"));

		// Assert
		result.ExitCode.Should().Be(1, because: "install should fail when the requested skill name does not match a discovered source skill");
		result.ErrorMessages.Should().ContainSingle(
			message => message.Contains("Skill 'beta' was not found"),
			because: "the error should explain that the named skill is missing from the source repository");
	}

	[Test]
	[Description("Install should reject a managed skill that is already present in the workspace instead of treating install as update.")]
	public void Install_ShouldFail_WhenManagedSkillAlreadyExists() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateSkill(repoPath, "alpha");
		CreateManagedSkill(workspacePath, SkillScope.Workspace, "alpha", "repo", "oldhash");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "newhash", "repo"));

		// Act
		SkillOperationResult result = _sut.Install(new InstallSkillsRequest(workspacePath, "alpha", "repo"));

		// Assert
		result.ExitCode.Should().Be(1, because: "install should not replace an already managed skill");
		result.ErrorMessages.Should().ContainSingle(
			message => message.Contains("Use update-skill instead"),
			because: "the failure should direct the caller to the explicit update flow");
	}

	[Test]
	[Description("Install should fail when the destination skill folder exists but is not managed by clio.")]
	public void Install_ShouldFail_WhenUnmanagedSkillAlreadyExists() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateSkill(repoPath, "alpha");
		_mockFileSystem.AddFile(GetPath("workspace", ".agents", "skills", "alpha", "SKILL.md"), new MockFileData("manual"));
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "abc123", "repo"));

		// Act
		SkillOperationResult result = _sut.Install(new InstallSkillsRequest(workspacePath, "alpha", "repo"));

		// Assert
		result.ExitCode.Should().Be(1, because: "install should protect unmanaged skill folders from overwrite");
		result.ErrorMessages.Should().ContainSingle(
			message => message.Contains("is not managed by clio"),
			because: "the failure should explain that clio refuses to overwrite an unmanaged skill folder");
	}

	[Test]
	[Description("Update should refresh one managed skill when the source repository commit hash changes.")]
	public void Update_ShouldRefreshManagedSkill_WhenCommitHashChanges() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateManagedSkill(workspacePath, SkillScope.Workspace, "alpha", "repo", "oldhash");
		CreateSkill(repoPath, "alpha", "updated");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "newhash", "repo"));

		// Act
		SkillOperationResult result = _sut.Update(new UpdateSkillsRequest(workspacePath, "alpha", "repo"));

		// Assert
		result.ExitCode.Should().Be(0, because: "update should succeed when the source commit hash changed and the skill still exists");
		_mockFileSystem.File.ReadAllText(GetPath("workspace", ".agents", "skills", "alpha", "SKILL.md")).Should().Contain("updated",
			because: "update should replace the installed skill files with the source repository content");
		_mockFileSystem.File.ReadAllText(GetPath("workspace", ".agents", "skills", ".clio-managed.json")).Should().Contain("newhash",
			because: "update should persist the refreshed source commit hash");
	}

	[Test]
	[Description("Update should refresh all managed skills for the selected repository when --skill is omitted.")]
	public void Update_ShouldRefreshAllManagedSkills_WhenSkillNameIsOmitted() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateManagedSkill(workspacePath, SkillScope.Workspace, "alpha", "repo", "oldhash");
		CreateManagedSkill(workspacePath, SkillScope.Workspace, "beta", "repo", "oldhash");
		CreateSkill(repoPath, "alpha", "updated alpha");
		CreateSkill(repoPath, "beta", "updated beta");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "newhash", "repo"));

		// Act
		SkillOperationResult result = _sut.Update(new UpdateSkillsRequest(workspacePath, null, "repo"));

		// Assert
		result.ExitCode.Should().Be(0, because: "update should support refreshing every managed skill registered for the selected repository");
		_mockFileSystem.File.ReadAllText(GetPath("workspace", ".agents", "skills", "alpha", "SKILL.md")).Should().Contain("updated alpha",
			because: "update should refresh alpha when all managed skills are updated");
		_mockFileSystem.File.ReadAllText(GetPath("workspace", ".agents", "skills", "beta", "SKILL.md")).Should().Contain("updated beta",
			because: "update should refresh beta when all managed skills are updated");
	}

	[Test]
	[Description("Update should report that a managed skill is already up to date when the repository commit hash did not change.")]
	public void Update_ShouldReportNoOp_WhenCommitHashMatches() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateManagedSkill(workspacePath, SkillScope.Workspace, "alpha", "repo", "samehash");
		CreateSkill(repoPath, "alpha", "original");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "samehash", "repo"));

		// Act
		SkillOperationResult result = _sut.Update(new UpdateSkillsRequest(workspacePath, "alpha", "repo"));

		// Assert
		result.ExitCode.Should().Be(0, because: "update should treat a matching commit hash as an already-up-to-date no-op");
		result.InfoMessages.Should().Contain(message => message.Contains("already up to date"),
			because: "update should report the no-op state to the caller");
	}

	[Test]
	[Description("Update should refresh a managed user-scope skill when the source repository commit hash changes.")]
	public void Update_ShouldRefreshManagedUserScopeSkill_WhenCommitHashChanges() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateManagedSkill(GetUserScopeHomePath(), SkillScope.User, "alpha", "repo", "oldhash");
		CreateSkill(repoPath, "alpha", "updated");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "newhash", "repo"));

		// Act
		SkillOperationResult result = _sut.Update(new UpdateSkillsRequest(workspacePath, "alpha", "repo", SkillScope.User));

		// Assert
		result.ExitCode.Should().Be(0, because: "user-scope update should refresh managed skills under the agent home");
		_mockFileSystem.File.ReadAllText(GetPath("codex-home", "skills", "alpha", "SKILL.md")).Should().Contain("updated",
			because: "user-scope update should replace the installed skill files with the source repository content");
	}

	[Test]
	[Description("Update should fail when the requested skill is not managed by clio for the selected repository.")]
	public void Update_ShouldFail_WhenSkillIsNotManaged() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		CreateWorkspace(workspacePath);

		// Act
		SkillOperationResult result = _sut.Update(new UpdateSkillsRequest(workspacePath, "alpha", "repo"));

		// Assert
		result.ExitCode.Should().Be(1, because: "update should only operate on managed skills");
		result.ErrorMessages.Should().ContainSingle(
			message => message.Contains("There are no clio-managed skills"),
			because: "the failure should explain that the workspace has no managed skills to update");
	}

	[Test]
	[Description("Update should fail when the managed skill no longer exists in the source repository.")]
	public void Update_ShouldFail_WhenSourceSkillIsMissing() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		string repoPath = GetRepositoryPath();
		CreateWorkspace(workspacePath);
		CreateManagedSkill(workspacePath, SkillScope.Workspace, "alpha", "repo", "oldhash");
		CreateSkill(repoPath, "beta");
		_resolver.Resolve("repo").Returns(CreateResolvedRepository(repoPath, "newhash", "repo"));

		// Act
		SkillOperationResult result = _sut.Update(new UpdateSkillsRequest(workspacePath, "alpha", "repo"));

		// Assert
		result.ExitCode.Should().Be(1, because: "update should fail when the managed skill path no longer exists in the source repository");
		result.ErrorMessages.Should().ContainSingle(
			message => message.Contains("no longer available"),
			because: "the failure should explain that the source repository no longer contains the managed skill");
	}

	[Test]
	[Description("Delete should remove a managed skill directory and update the managed manifest.")]
	public void Delete_ShouldRemoveManagedSkill() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		CreateWorkspace(workspacePath);
		CreateManagedSkill(workspacePath, SkillScope.Workspace, "alpha", "repo", "hash");

		// Act
		SkillOperationResult result = _sut.Delete(new DeleteSkillRequest(workspacePath, "alpha"));

		// Assert
		result.ExitCode.Should().Be(0, because: "delete should succeed for a managed workspace-local skill");
		_mockFileSystem.Directory.Exists(GetPath("workspace", ".agents", "skills", "alpha")).Should().BeFalse(
			because: "delete should remove the managed skill folder");
		_mockFileSystem.File.Exists(GetPath("workspace", ".agents", "skills", ".clio-managed.json")).Should().BeFalse(
			because: "delete should remove the manifest file when the last managed skill entry is removed");
	}

	[Test]
	[Description("Delete should fail when the workspace skill exists but is not managed by clio.")]
	public void Delete_ShouldFail_WhenSkillIsUnmanaged() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		CreateWorkspace(workspacePath);
		_mockFileSystem.AddFile(GetPath("workspace", ".agents", "skills", "alpha", "SKILL.md"), new MockFileData("manual"));

		// Act
		SkillOperationResult result = _sut.Delete(new DeleteSkillRequest(workspacePath, "alpha"));

		// Assert
		result.ExitCode.Should().Be(1, because: "delete should protect unmanaged skills from accidental removal");
		result.ErrorMessages.Should().ContainSingle(
			message => message.Contains("is not managed by clio"),
			because: "the failure should explain that clio only deletes managed skills");
	}

	[Test]
	[Description("Delete should remove a managed user-scope skill and clean up the user manifest.")]
	public void Delete_ShouldRemoveManagedUserScopeSkill() {
		// Arrange
		string workspacePath = GetWorkspacePath();
		CreateManagedSkill(GetUserScopeHomePath(), SkillScope.User, "alpha", "repo", "hash");

		// Act
		SkillOperationResult result = _sut.Delete(new DeleteSkillRequest(workspacePath, "alpha", SkillScope.User));

		// Assert
		result.ExitCode.Should().Be(0, because: "delete should support managed skills in user scope");
		_mockFileSystem.Directory.Exists(GetPath("codex-home", "skills", "alpha")).Should().BeFalse(
			because: "delete should remove the managed user-scope skill folder");
		_mockFileSystem.File.Exists(GetPath("codex-home", "skills", ".clio-managed.json")).Should().BeFalse(
			because: "delete should remove the user-scope manifest when the last entry is removed");
	}

	[Test]
	[Description("Repository resolver should cache the default bootstrap repository under the clio app-settings folder when the caller omits --repo.")]
	public void SkillRepositoryResolver_ShouldUseDefaultRepository_WhenRepoIsOmitted() {
		// Arrange
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>(), GetCurrentDirectoryPath());
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(mockFileSystem);
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		IGitCommandRunner gitCommandRunner = Substitute.For<IGitCommandRunner>();
		string cachedRepositoryPath = System.IO.Path.Combine(
			SettingsRepository.AppSettingsFolderPath,
			"bootstrap-composable-app-starter-kit");
		gitCommandRunner.Clone(WorkspaceSkillDefaults.DefaultRepository, cachedRepositoryPath)
			.Returns(new GitCommandResult(true, string.Empty, string.Empty));
		gitCommandRunner.GetHeadCommitHash(cachedRepositoryPath)
			.Returns(new GitCommandResult(true, "abc123", string.Empty));
		SkillRepositoryResolver resolver = new(fileSystem, workingDirectoriesProvider, gitCommandRunner);

		// Act
		using ResolvedSkillRepository repository = resolver.Resolve(null);

		// Assert
		gitCommandRunner.Received(1).Clone(WorkspaceSkillDefaults.DefaultRepository, cachedRepositoryPath);
		gitCommandRunner.DidNotReceiveWithAnyArgs().Pull(default!);
		gitCommandRunner.Received(1).GetHeadCommitHash(cachedRepositoryPath);
		repository.SourceLocator.Should().Be(WorkspaceSkillDefaults.DefaultRepository,
			because: "the resolver should normalize an omitted repository to the configured default URL");
	}

	[Test]
	[Description("Repository resolver should refresh an existing cached repository with git pull instead of cloning it again.")]
	public void SkillRepositoryResolver_ShouldPullCachedRepository_WhenCacheExists() {
		// Arrange
		string cachedRepositoryPath = System.IO.Path.Combine(
			SettingsRepository.AppSettingsFolderPath,
			"bootstrap-composable-app-starter-kit");
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData> {
			[Path.Combine(cachedRepositoryPath, ".git", "HEAD")] = new("ref: refs/heads/main")
		}, GetCurrentDirectoryPath());
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(mockFileSystem);
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		IGitCommandRunner gitCommandRunner = Substitute.For<IGitCommandRunner>();
		gitCommandRunner.Pull(cachedRepositoryPath)
			.Returns(new GitCommandResult(true, string.Empty, string.Empty));
		gitCommandRunner.GetHeadCommitHash(cachedRepositoryPath)
			.Returns(new GitCommandResult(true, "abc123", string.Empty));
		SkillRepositoryResolver resolver = new(fileSystem, workingDirectoriesProvider, gitCommandRunner);

		// Act
		using ResolvedSkillRepository repository = resolver.Resolve(WorkspaceSkillDefaults.DefaultRepository);

		// Assert
		gitCommandRunner.DidNotReceiveWithAnyArgs().Clone(default!, default!);
		gitCommandRunner.Received(1).Pull(cachedRepositoryPath);
		gitCommandRunner.Received(1).GetHeadCommitHash(cachedRepositoryPath);
		repository.RepositoryRootPath.Should().Be(cachedRepositoryPath,
			because: "the resolver should reuse the persistent cached repository directory for repeated operations");
	}

	private void CreateWorkspace(string workspacePath) {
		_mockFileSystem.AddFile(Path.Combine(workspacePath, ".clio", "workspaceSettings.json"), new MockFileData("{}"));
	}

	private void CreateSkill(string repoPath, string skillName, string skillContent = "content") {
		string skillDirectory = Path.Combine(repoPath, ".agents", "skills", skillName);
		_mockFileSystem.AddFile(Path.Combine(skillDirectory, "SKILL.md"), new MockFileData(skillContent));
		_mockFileSystem.AddFile(Path.Combine(skillDirectory, "README.md"), new MockFileData("details"));
	}

	private void CreateManagedSkill(string rootPath, SkillScope scope, string skillName, string sourceRepo, string commitHash) {
		string skillRoot = scope == SkillScope.User
			? Path.Combine(rootPath, "skills")
			: Path.Combine(rootPath, ".agents", "skills");
		string relativeTargetPath = scope == SkillScope.User
			? $"skills/{skillName}"
			: $".agents/skills/{skillName}";
		_mockFileSystem.AddFile(Path.Combine(skillRoot, skillName, "SKILL.md"), new MockFileData("installed"));
		string manifestPath = Path.Combine(skillRoot, ".clio-managed.json");
		ManagedSkillsManifest existingManifest = _mockFileSystem.File.Exists(manifestPath)
			? JsonSerializer.Deserialize<ManagedSkillsManifest>(_mockFileSystem.File.ReadAllText(manifestPath)) ?? new ManagedSkillsManifest()
			: new ManagedSkillsManifest();
		existingManifest.Entries.RemoveAll(entry => entry.SkillName == skillName);
		existingManifest.Entries.Add(new ManagedSkillEntry {
			SkillName = skillName,
			TargetPath = relativeTargetPath,
			SourceRepo = sourceRepo,
			SourceRelativePath = $".agents/skills/{skillName}",
			CommitHash = commitHash,
			InstalledAtUtc = DateTimeOffset.Parse("2026-03-27T10:00:00+00:00"),
			UpdatedAtUtc = DateTimeOffset.Parse("2026-03-27T10:00:00+00:00")
		});
		string manifestJson = JsonSerializer.Serialize(existingManifest);
		_mockFileSystem.AddFile(manifestPath, new MockFileData(manifestJson));
	}

	private ResolvedSkillRepository CreateResolvedRepository(string repositoryRootPath, string commitHash, string sourceLocator) {
		return new ResolvedSkillRepository(sourceLocator, repositoryRootPath, commitHash, _workingDirectoriesProvider);
	}

	private static string GetCurrentDirectoryPath() => OperatingSystem.IsWindows() ? @"C:\workspace" : "/";

	private static string GetWorkspacePath() => GetPath("workspace");

	private static string GetRepositoryPath() => GetPath("repo");

	private static string GetUserScopeHomePath() => GetPath("codex-home");

	private static string GetPath(params string[] segments) {
		string currentPath = OperatingSystem.IsWindows() ? @"C:\" : "/";
		foreach (string segment in segments) {
			currentPath = Path.Combine(currentPath, segment);
		}

		return currentPath;
	}
}
