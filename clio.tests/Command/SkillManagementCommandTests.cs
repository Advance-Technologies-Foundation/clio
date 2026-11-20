using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Command;
using Clio.Common;
using Clio.Common.Skills;
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
public sealed class SkillInstallServiceTests {
	private const string ReleaseCommit = "6b296e3929701ac438af71302681bfa96002e78e";
	private const string OlderCommit = "1111111111111111111111111111111111111111";

	private IManagedSkillsManifestStore _manifestStore = null!;
	private ManagedSkillsManifest _manifest = null!;
	private IInstalledToolkitVersions _installedVersions = null!;
	private IToolkitReleaseProbe _releaseProbe = null!;
	private IToolkitOperationLock _operationLock = null!;

	[SetUp]
	public void SetUp() {
		_manifest = new ManagedSkillsManifest();
		_manifestStore = Substitute.For<IManagedSkillsManifestStore>();
		_manifestStore.Read().Returns(_ => _manifest);
		_installedVersions = Substitute.For<IInstalledToolkitVersions>();
		_releaseProbe = Substitute.For<IToolkitReleaseProbe>();
		_releaseProbe.ReadReleaseCommit().Returns(ReleaseCommit);
		_operationLock = Substitute.For<IToolkitOperationLock>();
		_operationLock.TryAcquire().Returns(_ => Substitute.For<IDisposable>());
	}

	[Test]
	[Description("Update skips an agent whose own recorded commit equals the release commit, without dispatching or touching the manifest.")]
	public void Update_ShouldReportUpToDate_WhenInstalledCommitEqualsRelease() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: true);
		_installedVersions.ReadInstalledCommit("codex").Returns(ReleaseCommit);
		SkillInstallService sut = CreateService(codex);

		// Act
		SkillCommandResult result = sut.Update(target: null, repo: null);

		// Assert
		result.ExitCode.Should().Be(0, because: "an agent that is already current is not a failure");
		result.Outcomes.Should().ContainSingle(outcome => outcome.Status == AgentOutcomeStatus.UpToDate,
			because: "the agent should be reported as up to date");
		result.Summary.Should().Contain("already up to date for: codex",
			because: "the summary must not claim an update that did not happen");
		codex.DidNotReceive().Update(Arg.Any<AgentOperationContext>());
		_manifestStore.DidNotReceive().Save(Arg.Any<ManagedSkillsManifest>());
	}

	[Test]
	[Description("Update reinstalls an agent whose recorded commit differs from the release commit.")]
	public void Update_ShouldDispatch_WhenInstalledCommitDiffersFromRelease() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: true);
		_installedVersions.ReadInstalledCommit("codex").Returns(OlderCommit);
		SkillInstallService sut = CreateService(codex);

		// Act
		SkillCommandResult result = sut.Update(target: null, repo: null);

		// Assert
		result.ExitCode.Should().Be(0, because: "the update succeeded");
		codex.Received(1).Update(Arg.Any<AgentOperationContext>());
	}

	[Test]
	[Description("Update falls back to a full update when the release commit cannot be determined.")]
	public void Update_ShouldDispatch_WhenReleaseCommitIsUnknown() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: true);
		_installedVersions.ReadInstalledCommit("codex").Returns(ReleaseCommit);
		_releaseProbe.ReadReleaseCommit().Returns((string)null);
		SkillInstallService sut = CreateService(codex);

		// Act
		sut.Update(target: null, repo: null);

		// Assert
		codex.Received(1).Update(Arg.Any<AgentOperationContext>());
	}

	[Test]
	[Description("An agent that records no installed commit is updated without any remote query.")]
	public void Update_ShouldDispatchWithoutQueryingRemote_WhenInstalledCommitIsUnknown() {
		// Arrange
		ICodingAgent cursor = StubAgent("cursor", detected: true);
		_installedVersions.ReadInstalledCommit("cursor").Returns((string)null);
		SkillInstallService sut = CreateService(cursor);

		// Act
		sut.Update(target: null, repo: null);

		// Assert
		cursor.Received(1).Update(Arg.Any<AgentOperationContext>());
		_releaseProbe.DidNotReceive().ReadReleaseCommit();
	}

	[Test]
	[Description("The release commit is queried once per update, however many agents are compared with it.")]
	public void Update_ShouldQueryReleaseCommitOnce_ForSeveralAgents() {
		// Arrange
		ICodingAgent claude = StubAgent("claude", detected: true);
		ICodingAgent codex = StubAgent("codex", detected: true);
		_installedVersions.ReadInstalledCommit(Arg.Any<string>()).Returns(ReleaseCommit);
		SkillInstallService sut = CreateService(claude, codex);

		// Act
		SkillCommandResult result = sut.Update(target: null, repo: null);

		// Assert
		_releaseProbe.Received(1).ReadReleaseCommit();
		result.Summary.Should().Contain("claude, codex", because: "both agents are reported up to date");
	}

	[Test]
	[Description("force reinstalls an agent that is already on the release commit, without any comparison.")]
	public void Update_ShouldDispatch_WhenForced() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: true);
		_installedVersions.ReadInstalledCommit("codex").Returns(ReleaseCommit);
		SkillInstallService sut = CreateService(codex);

		// Act
		sut.Update(target: null, repo: null, force: true);

		// Assert
		codex.Received(1).Update(Arg.Any<AgentOperationContext>());
		_releaseProbe.DidNotReceive().ReadReleaseCommit();
	}

	[Test]
	[Description("A --repo override always updates, because only the default source has a release commit to compare with.")]
	public void Update_ShouldDispatch_WhenRepositoryIsOverridden() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: true);
		_installedVersions.ReadInstalledCommit("codex").Returns(ReleaseCommit);
		SkillInstallService sut = CreateService(codex);

		// Act
		sut.Update(target: null, repo: "https://example.com/fork.git");

		// Assert
		codex.Received(1).Update(Arg.Any<AgentOperationContext>());
		_releaseProbe.DidNotReceive().ReadReleaseCommit();
	}

	[Test]
	[Description("Install never takes the up-to-date shortcut.")]
	public void Install_ShouldNotCompareCommits() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: true);
		_installedVersions.ReadInstalledCommit("codex").Returns(ReleaseCommit);
		SkillInstallService sut = CreateService(codex);

		// Act
		sut.Install(target: null, repo: null);

		// Assert
		codex.Received(1).Install(Arg.Any<AgentOperationContext>());
		_installedVersions.DidNotReceive().ReadInstalledCommit(Arg.Any<string>());
	}

	[Test]
	[Description("A toolkit operation running in another clio process makes this one fail fast without touching any agent.")]
	public void Update_ShouldFailWithoutDispatch_WhenAnotherOperationHoldsTheLock() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: true);
		_operationLock.TryAcquire().Returns((IDisposable)null);
		SkillInstallService sut = CreateService(codex);

		// Act
		SkillCommandResult result = sut.Update(target: null, repo: null);

		// Assert
		result.ExitCode.Should().Be(1, because: "the operation did not run");
		result.Summary.Should().Be(SkillInstallService.OperationInProgressMessage,
			because: "the caller must learn why nothing happened");
		codex.DidNotReceive().Update(Arg.Any<AgentOperationContext>());
		codex.DidNotReceive().Detect();
	}

	[Test]
	[Description("The lock is released when the operation ends.")]
	public void Update_ShouldReleaseTheLock_WhenOperationEnds() {
		// Arrange
		IDisposable handle = Substitute.For<IDisposable>();
		_operationLock.TryAcquire().Returns(handle);
		SkillInstallService sut = CreateService(StubAgent("codex", detected: true));

		// Act
		sut.Update(target: null, repo: null);

		// Assert
		handle.Received(1).Dispose();
	}

	private SkillInstallService CreateService(params ICodingAgent[] agents) =>
		new(agents, _manifestStore, _installedVersions, _releaseProbe, _operationLock);

	[Test]
	[Description("Install dispatches to every detected agent and records succeeded agents in the manifest.")]
	public void Install_ShouldDispatchToAllDetectedAgents_WhenNoTarget() {
		// Arrange
		ICodingAgent claude = StubAgent("claude", detected: true, AgentOutcome.Succeeded("claude", "ok"));
		ICodingAgent codex = StubAgent("codex", detected: true, AgentOutcome.Succeeded("codex", "ok"));
		SkillInstallService sut = CreateService(claude, codex);

		// Act
		SkillCommandResult result = sut.Install(target: null, repo: null);

		// Assert
		result.ExitCode.Should().Be(0, because: "all detected agents installed successfully");
		claude.Received(1).Install(Arg.Any<AgentOperationContext>());
		codex.Received(1).Install(Arg.Any<AgentOperationContext>());
		_manifestStore.Received(1).Save(Arg.Is<ManagedSkillsManifest>(m =>
			m.Agents.ContainsKey("claude") && m.Agents.ContainsKey("codex")));
	}

	[Test]
	[Description("Install skips an undetected agent with exit 0 and does not dispatch to it.")]
	public void Install_ShouldSkipUndetectedAgent_WhenHomeAbsent() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: false, AgentOutcome.Succeeded("codex", "ok"));
		SkillInstallService sut = CreateService(codex);

		// Act
		SkillCommandResult result = sut.Install(target: null, repo: null);

		// Assert
		result.ExitCode.Should().Be(0, because: "an undetected agent is a clean no-op, not a failure");
		result.Outcomes.Should().ContainSingle(outcome => outcome.Status == AgentOutcomeStatus.Skipped,
			because: "the undetected agent should be reported as skipped");
		codex.DidNotReceive().Install(Arg.Any<AgentOperationContext>());
	}

	[Test]
	[Description("Install returns a non-zero exit code when any selected agent fails.")]
	public void Install_ShouldReturnNonZero_WhenAnyAgentFails() {
		// Arrange
		ICodingAgent claude = StubAgent("claude", detected: true, AgentOutcome.Succeeded("claude", "ok"));
		ICodingAgent codex = StubAgent("codex", detected: true, AgentOutcome.Failed("codex", "boom"));
		SkillInstallService sut = CreateService(claude, codex);

		// Act
		SkillCommandResult result = sut.Install(target: null, repo: null);

		// Assert
		result.ExitCode.Should().Be(1, because: "a failed agent must fail the command");
	}

	[Test]
	[Description("An invalid --target value is rejected before dispatch with a non-zero exit code.")]
	public void Install_ShouldRejectUnknownTarget_BeforeDispatch() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: true, AgentOutcome.Succeeded("codex", "ok"));
		SkillInstallService sut = CreateService(codex);

		// Act
		SkillCommandResult result = sut.Install(target: "foobar", repo: null);

		// Assert
		result.ExitCode.Should().Be(1, because: "an out-of-set target name is a validation error");
		result.Summary.Should().Contain("Unknown target", because: "the error should name the invalid target");
		codex.DidNotReceive().Install(Arg.Any<AgentOperationContext>());
	}

	[Test]
	[Description("A --target value limits the operation to a single agent and leaves others untouched.")]
	public void Install_ShouldTargetSingleAgent_WhenTargetProvided() {
		// Arrange
		ICodingAgent claude = StubAgent("claude", detected: true, AgentOutcome.Succeeded("claude", "ok"));
		ICodingAgent codex = StubAgent("codex", detected: true, AgentOutcome.Succeeded("codex", "ok"));
		SkillInstallService sut = CreateService(claude, codex);

		// Act
		SkillCommandResult result = sut.Install(target: "codex", repo: null);

		// Assert
		result.ExitCode.Should().Be(0, because: "the single targeted agent succeeded");
		codex.Received(1).Install(Arg.Any<AgentOperationContext>());
		claude.DidNotReceive().Install(Arg.Any<AgentOperationContext>());
	}

	[Test]
	[Description("Update dispatches the update operation to detected agents (Claude is not skipped).")]
	public void Update_ShouldDispatchUpdate_ToDetectedAgents() {
		// Arrange
		ICodingAgent claude = StubAgent("claude", detected: true, updateOutcome: AgentOutcome.Succeeded("claude", "ok"));
		SkillInstallService sut = CreateService(claude);

		// Act
		SkillCommandResult result = sut.Update(target: null, repo: null);

		// Assert
		result.ExitCode.Should().Be(0, because: "the detected agent updated successfully");
		claude.Received(1).Update(Arg.Any<AgentOperationContext>());
	}

	[Test]
	[Description("Delete removes the agent's manifest entry on success.")]
	public void Delete_ShouldRemoveManifestEntry_OnSuccess() {
		// Arrange
		_manifest.Upsert("codex", "src", DateTimeOffset.UnixEpoch);
		ICodingAgent codex = StubAgent("codex", detected: true, deleteOutcome: AgentOutcome.Succeeded("codex", "removed"));
		SkillInstallService sut = CreateService(codex);

		// Act
		SkillCommandResult result = sut.Delete(target: null);

		// Assert
		result.ExitCode.Should().Be(0, because: "delete succeeded");
		// A successful delete should drop the agent's manifest entry.
		_manifestStore.Received(1).Save(Arg.Is<ManagedSkillsManifest>(m => !m.Agents.ContainsKey("codex")));
	}

	[Test]
	[Description("On a mixed run only succeeded agents are written to the manifest; failed agents are not recorded.")]
	public void Install_ShouldWriteOnlySucceededAgents_WhenMixedOutcomes() {
		// Arrange
		ICodingAgent claude = StubAgent("claude", detected: true, AgentOutcome.Succeeded("claude", "ok"));
		ICodingAgent codex = StubAgent("codex", detected: true, AgentOutcome.Failed("codex", "boom"));
		SkillInstallService sut = CreateService(claude, codex);

		// Act
		sut.Install(target: null, repo: null);

		// Assert
		_manifestStore.Received(1).Save(Arg.Is<ManagedSkillsManifest>(m =>
			m.Agents.ContainsKey("claude") && !m.Agents.ContainsKey("codex")));
	}

	[Test]
	[Description("The manifest is not saved when no agent changed state (all skipped).")]
	public void Install_ShouldNotSaveManifest_WhenAllAgentsSkipped() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: false, AgentOutcome.Succeeded("codex", "ok"));
		SkillInstallService sut = CreateService(codex);

		// Act
		sut.Install(target: null, repo: null);

		// Assert
		_manifestStore.DidNotReceive().Save(Arg.Any<ManagedSkillsManifest>());
	}

	[Test]
	[Description("An invalid --repo value is rejected before dispatch with a non-zero exit code.")]
	public void Install_ShouldRejectUnsafeRepo_BeforeDispatch() {
		// Arrange
		ICodingAgent codex = StubAgent("codex", detected: true, AgentOutcome.Succeeded("codex", "ok"));
		SkillInstallService sut = CreateService(codex);

		// Act
		SkillCommandResult result = sut.Install(target: null, repo: "ext::sh -c calc");

		// Assert
		result.ExitCode.Should().Be(1, because: "an ext:: transport in --repo must be rejected");
		codex.DidNotReceive().Install(Arg.Any<AgentOperationContext>());
	}

	private static ICodingAgent StubAgent(
		string id,
		bool detected,
		AgentOutcome installOutcome = null,
		AgentOutcome updateOutcome = null,
		AgentOutcome deleteOutcome = null) {
		ICodingAgent agent = Substitute.For<ICodingAgent>();
		agent.AgentId.Returns(id);
		agent.DisplayName.Returns(id);
		agent.Detect().Returns(detected);
		agent.Install(Arg.Any<AgentOperationContext>()).Returns(installOutcome ?? AgentOutcome.Succeeded(id, "ok"));
		agent.Update(Arg.Any<AgentOperationContext>()).Returns(updateOutcome ?? AgentOutcome.Succeeded(id, "ok"));
		agent.Delete(Arg.Any<AgentOperationContext>()).Returns(deleteOutcome ?? AgentOutcome.Succeeded(id, "ok"));
		return agent;
	}
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SkillCommandDeprecationTests {
	private ISkillInstallService _service = null!;
	private ILogger _logger = null!;

	[SetUp]
	public void SetUp() {
		_service = Substitute.For<ISkillInstallService>();
		_logger = Substitute.For<ILogger>();
	}

	[Test]
	[Description("install-skills rejects the removed --scope option with a non-zero exit and does not call the service.")]
	public void Install_ShouldRejectScopeOption_WithError() {
		// Arrange
		InstallSkillsCommand command = new(_service, _logger);

		// Act
		int exitCode = command.Execute(new InstallSkillsOptions { Scope = "user" });

		// Assert
		exitCode.Should().Be(1, because: "--scope has been removed and must hard-error");
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("--scope")));
		_service.DidNotReceiveWithAnyArgs().Install(default, default);
	}

	[Test]
	[Description("install-skills rejects the removed --skill option with a non-zero exit and does not call the service.")]
	public void Install_ShouldRejectSkillOption_WithError() {
		// Arrange
		InstallSkillsCommand command = new(_service, _logger);

		// Act
		int exitCode = command.Execute(new InstallSkillsOptions { Skill = "alpha" });

		// Assert
		exitCode.Should().Be(1, because: "--skill has been removed and must hard-error");
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("--skill")));
		_service.DidNotReceiveWithAnyArgs().Install(default, default);
	}

	[Test]
	[Description("install-skills forwards target and repo to the service and returns its exit code.")]
	public void Install_ShouldCallService_AndReturnExitCode() {
		// Arrange
		_service.Install("codex", "url").Returns(new SkillCommandResult(0, Array.Empty<AgentOutcome>(), "done"));
		InstallSkillsCommand command = new(_service, _logger);

		// Act
		int exitCode = command.Execute(new InstallSkillsOptions { Target = "codex", Repo = "url" });

		// Assert
		exitCode.Should().Be(0, because: "the command should return the service exit code");
		_service.Received(1).Install("codex", "url");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("update-toolkit forwards target, repo, and --force to the service and returns its exit code.")]
	public void Update_ShouldForwardForce_AndReturnExitCode(bool force) {
		// Arrange
		_service.Update("codex", "url", force).Returns(new SkillCommandResult(0, Array.Empty<AgentOutcome>(), "done"));
		UpdateSkillCommand command = new(_service, _logger);

		// Act
		int exitCode = command.Execute(new UpdateSkillOptions { Target = "codex", Repo = "url", Force = force });

		// Assert
		exitCode.Should().Be(0, because: "the command should return the service exit code");
		_service.Received(1).Update("codex", "url", force);
	}

	[Test]
	[Description("update-toolkit reports up-to-date agents as information, not as errors.")]
	public void Update_ShouldReportUpToDateAgentsAsInfo() {
		// Arrange
		_service.Update(null, null, false).Returns(new SkillCommandResult(0,
			[AgentOutcome.UpToDate("codex", "Codex already has the latest toolkit")], "already up to date for: codex."));
		UpdateSkillCommand command = new(_service, _logger);

		// Act
		int exitCode = command.Execute(new UpdateSkillOptions());

		// Assert
		exitCode.Should().Be(0, because: "being up to date is a successful outcome");
		_logger.Received(1).WriteInfo("Codex already has the latest toolkit");
		_logger.DidNotReceiveWithAnyArgs().WriteError(default);
	}

	[Test]
	[Description("delete-skill rejects the removed --scope option with a non-zero exit.")]
	public void Delete_ShouldRejectScopeOption_WithError() {
		// Arrange
		DeleteSkillCommand command = new(_service, _logger);

		// Act
		int exitCode = command.Execute(new DeleteSkillOptions { Scope = "workspace" });

		// Assert
		exitCode.Should().Be(1, because: "delete-skill must also reject the removed --scope option");
		_service.DidNotReceiveWithAnyArgs().Delete(default);
	}
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SkillRepositoryResolverTests {
	private const string CachedRepoName = "creatio-ai-app-development-toolkit";

	[Test]
	[Description("Repository resolver should clone the default toolkit marketplace under the clio app-settings folder when --repo is omitted.")]
	public void SkillRepositoryResolver_ShouldUseDefaultRepository_WhenRepoIsOmitted() {
		// Arrange
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>(), GetCurrentDirectoryPath());
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(mockFileSystem);
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		IGitCommandRunner gitCommandRunner = Substitute.For<IGitCommandRunner>();
		string cachedRepositoryPath = Path.Combine(SettingsRepository.AppSettingsFolderPath, CachedRepoName);
		gitCommandRunner.Clone(ToolkitDistribution.MarketplaceGitUrl, cachedRepositoryPath)
			.Returns(new GitCommandResult(true, string.Empty, string.Empty));
		gitCommandRunner.GetHeadCommitHash(cachedRepositoryPath)
			.Returns(new GitCommandResult(true, "abc123", string.Empty));
		SkillRepositoryResolver resolver = new(fileSystem, workingDirectoriesProvider, gitCommandRunner, mockFileSystem);

		// Act
		using ResolvedSkillRepository repository = resolver.Resolve(null);

		// Assert
		gitCommandRunner.Received(1).Clone(ToolkitDistribution.MarketplaceGitUrl, cachedRepositoryPath);
		gitCommandRunner.DidNotReceiveWithAnyArgs().Pull(default!);
		repository.SourceLocator.Should().Be(ToolkitDistribution.MarketplaceGitUrl,
			because: "the resolver should normalize an omitted repository to the default toolkit marketplace URL");
	}

	[Test]
	[Description("Repository resolver should refresh an existing cached repository with git pull instead of cloning again.")]
	public void SkillRepositoryResolver_ShouldPullCachedRepository_WhenCacheExists() {
		// Arrange
		string cachedRepositoryPath = Path.Combine(SettingsRepository.AppSettingsFolderPath, CachedRepoName);
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
		SkillRepositoryResolver resolver = new(fileSystem, workingDirectoriesProvider, gitCommandRunner, mockFileSystem);

		// Act
		using ResolvedSkillRepository repository = resolver.Resolve(ToolkitDistribution.MarketplaceGitUrl);

		// Assert
		gitCommandRunner.DidNotReceiveWithAnyArgs().Clone(default!, default!);
		gitCommandRunner.Received(1).Pull(cachedRepositoryPath);
		repository.RepositoryRootPath.Should().Be(cachedRepositoryPath,
			because: "the resolver should reuse the persistent cached repository directory for repeated operations");
	}

	[TestCase("https://host/x/..")]
	[TestCase("https://host/x/../")]
	[TestCase("https://host/x/.")]
	[TestCase("https://host/x/.git")]
	[TestCase("https://host/x/...git")]
	[TestCase("https://host/x/.. .git")]
	[TestCase("https://host\\x\\..")]
	[TestCase("https://host/x/. .")]
	[TestCase("https://host/x/.. .")]
	[TestCase("https://host/x/. . .git")]
	[TestCase("https://host/x/repo.")]
	[TestCase("https://host/x/repo..git")]
	[Description("Repository resolver should refuse a git URL whose last segment is not a plain folder name before it creates a directory or starts git, because the cache folder would be the clio settings folder itself or a folder outside it.")]
	public void SkillRepositoryResolver_ShouldRefuseRepository_WhenDerivedCacheNameIsNotPlainName(string repositoryLocator) {
		// Arrange
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>(), GetCurrentDirectoryPath());
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(mockFileSystem);
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		IGitCommandRunner gitCommandRunner = Substitute.For<IGitCommandRunner>();
		gitCommandRunner.Pull(Arg.Any<string>()).Returns(new GitCommandResult(true, string.Empty, string.Empty));
		gitCommandRunner.Clone(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(new GitCommandResult(true, string.Empty, string.Empty));
		gitCommandRunner.GetHeadCommitHash(Arg.Any<string>())
			.Returns(new GitCommandResult(true, "abc123", string.Empty));
		SkillRepositoryResolver resolver = new(fileSystem, workingDirectoriesProvider, gitCommandRunner, mockFileSystem);

		// Act
		Action act = () => resolver.Resolve(repositoryLocator);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a cache folder name that is not a plain name would place the git working folder outside the cache root")
			.WithMessage("*is not a plain folder name*",
				because: "the caller should learn what is wrong with the repository locator");
		gitCommandRunner.ReceivedCalls().Should().BeEmpty(
			because: "no git process may start for a repository whose cache folder is refused");
		mockFileSystem.Directory.Exists(SettingsRepository.AppSettingsFolderPath).Should().BeFalse(
			because: "the refusal must come before any directory is created");
	}

	[TestCase("sessions", true)]
	[TestCase("tokens", true)]
	[TestCase("sessions", false)]
	[Description("Repository resolver should refuse a git URL whose cache folder already exists and is not the root of a git working tree, with or without content, before any git process starts and without deleting anything: git pull there would search the parent folders for a repository.")]
	public void SkillRepositoryResolver_ShouldRefuseRepository_WhenCacheFolderExistsAndIsNotAClone(
		string folderName, bool hasContent) {
		// Arrange
		string existingFolderPath = Path.Combine(SettingsRepository.AppSettingsFolderPath, folderName);
		string existingFilePath = Path.Combine(existingFolderPath, "state.json");
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>(), GetCurrentDirectoryPath());
		mockFileSystem.AddDirectory(existingFolderPath);
		if (hasContent) {
			mockFileSystem.AddFile(existingFilePath, new MockFileData("{}"));
		}
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(mockFileSystem);
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		IGitCommandRunner gitCommandRunner = Substitute.For<IGitCommandRunner>();
		gitCommandRunner.Pull(Arg.Any<string>()).Returns(new GitCommandResult(true, string.Empty, string.Empty));
		gitCommandRunner.Clone(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(new GitCommandResult(true, string.Empty, string.Empty));
		gitCommandRunner.GetHeadCommitHash(Arg.Any<string>())
			.Returns(new GitCommandResult(true, "abc123", string.Empty));
		SkillRepositoryResolver resolver = new(fileSystem, workingDirectoriesProvider, gitCommandRunner, mockFileSystem);

		// Act
		Action act = () => resolver.Resolve($"https://host/x/{folderName}");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a folder that is not a clone must never become the working folder of a git command")
			.WithMessage("*exists and is not a clone clio made*",
				because: "the caller should learn that the folder is in the way and what to do about it");
		gitCommandRunner.ReceivedCalls().Should().BeEmpty(
			because: "git pull in a folder that is not a repository root walks up to any enclosing repository");
		workingDirectoriesProvider.ReceivedCalls().Should().BeEmpty(
			because: "the refusal must not clean anything up");
		mockFileSystem.Directory.Exists(existingFolderPath).Should().BeTrue(
			because: "the folder is not clio's to delete");
		if (hasContent) {
			mockFileSystem.File.Exists(existingFilePath).Should().BeTrue(
				because: "the content of the folder must be left untouched");
		}
	}

	[Test]
	[Description("Repository resolver should pull an existing cache folder whose .git is a file, the form of a linked working tree, because that folder is still the root of a git working tree.")]
	public void SkillRepositoryResolver_ShouldPullCachedRepository_WhenGitEntryIsAFile() {
		// Arrange
		string cachedRepositoryPath = Path.Combine(SettingsRepository.AppSettingsFolderPath, "repo");
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData> {
			[Path.Combine(cachedRepositoryPath, ".git")] = new("gitdir: /elsewhere/.git/worktrees/repo")
		}, GetCurrentDirectoryPath());
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(mockFileSystem);
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		IGitCommandRunner gitCommandRunner = Substitute.For<IGitCommandRunner>();
		gitCommandRunner.Pull(cachedRepositoryPath)
			.Returns(new GitCommandResult(true, string.Empty, string.Empty));
		gitCommandRunner.GetHeadCommitHash(cachedRepositoryPath)
			.Returns(new GitCommandResult(true, "abc123", string.Empty));
		SkillRepositoryResolver resolver = new(fileSystem, workingDirectoriesProvider, gitCommandRunner, mockFileSystem);

		// Act
		using ResolvedSkillRepository repository = resolver.Resolve("https://host/x/repo.git");

		// Assert
		gitCommandRunner.Received(1).Pull(cachedRepositoryPath);
		repository.RepositoryRootPath.Should().Be(cachedRepositoryPath,
			because: "a folder with a .git file is a git working tree root and stays usable as the cache");
	}

	[TestCase("https://github.com/org/repo")]
	[TestCase("https://github.com/org/repo.git")]
	[TestCase("https://github.com/org/repo/")]
	[TestCase("https://github.com/org/repo.git/")]
	[TestCase("git@github.com:org/repo.git")]
	[Description("Repository resolver should keep deriving the cache folder name from the last segment of an ordinary git URL, with or without .git and a trailing slash.")]
	public void SkillRepositoryResolver_ShouldCloneIntoFolderNamedAfterLastSegment_WhenRepoIsOrdinaryGitUrl(
		string repositoryLocator) {
		// Arrange
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData>(), GetCurrentDirectoryPath());
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(mockFileSystem);
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		IGitCommandRunner gitCommandRunner = Substitute.For<IGitCommandRunner>();
		string cachedRepositoryPath = Path.Combine(SettingsRepository.AppSettingsFolderPath, "repo");
		gitCommandRunner.Clone(repositoryLocator, cachedRepositoryPath)
			.Returns(new GitCommandResult(true, string.Empty, string.Empty));
		gitCommandRunner.GetHeadCommitHash(cachedRepositoryPath)
			.Returns(new GitCommandResult(true, "abc123", string.Empty));
		SkillRepositoryResolver resolver = new(fileSystem, workingDirectoriesProvider, gitCommandRunner, mockFileSystem);

		// Act
		using ResolvedSkillRepository repository = resolver.Resolve(repositoryLocator);

		// Assert
		gitCommandRunner.Received(1).Clone(repositoryLocator, cachedRepositoryPath);
		repository.RepositoryRootPath.Should().Be(cachedRepositoryPath,
			because: "an ordinary git URL should still be cached in a folder named after its last segment");
	}

	[Test]
	[Description("Repository resolver should use an existing local folder in place, without deriving a cache folder, cloning or pulling.")]
	public void SkillRepositoryResolver_ShouldUseLocalFolderInPlace_WhenRepoIsExistingDirectory() {
		// Arrange
		string localRepositoryPath = OperatingSystem.IsWindows() ? @"C:\repos\toolkit" : "/repos/toolkit";
		MockFileSystem mockFileSystem = new(new Dictionary<string, MockFileData> {
			[Path.Combine(localRepositoryPath, ".git", "HEAD")] = new("ref: refs/heads/main")
		}, GetCurrentDirectoryPath());
		Clio.Common.IFileSystem fileSystem = new Clio.Common.FileSystem(mockFileSystem);
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		IGitCommandRunner gitCommandRunner = Substitute.For<IGitCommandRunner>();
		string resolvedPath = mockFileSystem.Path.GetFullPath(localRepositoryPath);
		gitCommandRunner.GetHeadCommitHash(resolvedPath)
			.Returns(new GitCommandResult(true, "abc123", string.Empty));
		SkillRepositoryResolver resolver = new(fileSystem, workingDirectoriesProvider, gitCommandRunner, mockFileSystem);

		// Act
		using ResolvedSkillRepository repository = resolver.Resolve(localRepositoryPath);

		// Assert
		repository.RepositoryRootPath.Should().Be(resolvedPath,
			because: "a local folder is the source itself and is not copied into the cache");
		gitCommandRunner.DidNotReceiveWithAnyArgs().Clone(default!, default!);
		gitCommandRunner.DidNotReceiveWithAnyArgs().Pull(default!);
		mockFileSystem.Directory.Exists(SettingsRepository.AppSettingsFolderPath).Should().BeFalse(
			because: "a local folder needs no cache folder under the clio settings folder");
	}

	private static string GetCurrentDirectoryPath() => OperatingSystem.IsWindows() ? @"C:\workspace" : "/";
}
