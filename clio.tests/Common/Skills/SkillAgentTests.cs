using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Common;
using Clio.Common.Skills;
using Clio.Common.Skills.Agents;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Skills;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class SkillAgentTests {
	private static string Root => OperatingSystem.IsWindows() ? @"C:\" : "/";

	private MockFileSystem _mockFileSystem = null!;
	private Clio.Common.IFileSystem _fileSystem = null!;
	private IUserHomeProvider _home = null!;
	private IAgentCliRunner _cli = null!;
	private IJsonConfigEditor _json = null!;
	private ICodexTomlConfigEditor _toml = null!;
	private AgentOperationContext _installContext = null!;

	[SetUp]
	public void SetUp() {
		_mockFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), Root);
		_fileSystem = new Clio.Common.FileSystem(_mockFileSystem);
		_home = Substitute.For<IUserHomeProvider>();
		_home.GetAgentHome(Arg.Any<string>()).Returns(call => Path.Combine(Root, "home", $".{call.Arg<string>()}"));
		_home.GetAgentsDir().Returns(Path.Combine(Root, "home", ".agents"));
		_home.GetClioDir().Returns(Path.Combine(Root, "home", ".clio"));
		_cli = Substitute.For<IAgentCliRunner>();
		_cli.IsOnPath(Arg.Any<string>()).Returns(true);
		_cli.Run(Arg.Any<string>(), Arg.Any<string[]>()).Returns(Ok());
		_json = Substitute.For<IJsonConfigEditor>();
		_toml = Substitute.For<ICodexTomlConfigEditor>();
		_installContext = new AgentOperationContext(SkillOperationKind.Install, null);
	}

	// ---- MarketplaceAgentBase: CLI preflight (NOQ-01) ----

	[Test]
	[Description("A marketplace agent whose CLI is not on PATH is skipped (exit-0 semantics), without invoking the CLI.")]
	public void ClaudeInstall_ShouldSkip_WhenCliNotOnPath() {
		// Arrange
		_cli.IsOnPath("claude").Returns(false);
		ClaudeAgent agent = new(_fileSystem, _home, _cli, _json);

		// Act
		AgentOutcome outcome = agent.Install(_installContext);

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Skipped, because: "a detected agent with no CLI on PATH is skipped (NOQ-01)");
		_cli.DidNotReceive().Run(Arg.Any<string>(), Arg.Any<string[]>());
	}

	// ---- MarketplaceAgentBase: pre-remove install path (Claude) ----

	[Test]
	[Description("Claude install registers the marketplace and installs the plugin, then enables marketplace auto-update.")]
	public void ClaudeInstall_ShouldRegisterMarketplaceInstallAndEnableAutoUpdate() {
		// Arrange
		ClaudeAgent agent = new(_fileSystem, _home, _cli, _json);

		// Act
		AgentOutcome outcome = agent.Install(_installContext);

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded, because: "all CLI steps succeeded");
		_cli.Received().Run("claude", Arg.Is<string[]>(a => a.SequenceEqual(new[] { "plugin", "marketplace", "add", ToolkitDistribution.MarketplaceGitUrl })));
		_cli.Received().Run("claude", Arg.Is<string[]>(a => a.SequenceEqual(new[] { "plugin", "install", ToolkitDistribution.PluginSource })));
		_json.Received(1).EnableMarketplaceAutoUpdate(Arg.Any<string>(), ToolkitDistribution.MarketplaceName);
	}

	[Test]
	[Description("Claude install fails when the marketplace add fails, and does not proceed to plugin install.")]
	public void ClaudeInstall_ShouldFail_WhenMarketplaceAddFails() {
		// Arrange
		_cli.Run("claude", Arg.Is<string[]>(a => a.Contains("add"))).Returns(Err("boom"));
		ClaudeAgent agent = new(_fileSystem, _home, _cli, _json);

		// Act
		AgentOutcome outcome = agent.Install(_installContext);

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Failed, because: "a marketplace-add failure must fail the agent");
		_cli.DidNotReceive().Run("claude", Arg.Is<string[]>(a => a.Contains("install")));
	}

	[Test]
	[Description("Claude update refreshes via `plugin update` and re-asserts marketplace auto-update.")]
	public void ClaudeUpdate_ShouldRunPluginUpdate() {
		// Arrange
		ClaudeAgent agent = new(_fileSystem, _home, _cli, _json);

		// Act
		AgentOutcome outcome = agent.Update(new AgentOperationContext(SkillOperationKind.Update, null));

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded, because: "Claude is updatable, not skipped");
		_cli.Received().Run("claude", Arg.Is<string[]>(a => a.SequenceEqual(new[] { "plugin", "update", ToolkitDistribution.PluginSource })));
	}

	// ---- MarketplaceAgentBase: conflict-driven re-add (Copilot) ----

	[Test]
	[Description("Copilot install handles an 'already registered' marketplace by removing (with --force) and re-adding, then installs.")]
	public void CopilotInstall_ShouldRemoveForceAndReadd_WhenAlreadyRegistered() {
		// Arrange: first add reports the conflict, second add succeeds.
		_cli.Run("copilot", Arg.Is<string[]>(a => a.Contains("add"))).Returns(Err("marketplace \"creatio\" already registered"), Ok());
		CopilotAgent agent = new(_fileSystem, _home, _cli);

		// Act
		AgentOutcome outcome = agent.Install(_installContext);

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded, because: "the conflict-retry path should recover and install");
		_cli.Received().Run("copilot", Arg.Is<string[]>(a => a.Contains("remove") && a.Contains("--force")));
		_cli.Received().Run("copilot", Arg.Is<string[]>(a => a.Contains("install")));
	}

	// ---- MarketplaceAgentBase: tolerant teardown (Copilot delete) ----

	[Test]
	[Description("Delete is idempotent: a 'not installed' uninstall/remove is tolerated as success.")]
	public void CopilotDelete_ShouldSucceed_WhenNotInstalled() {
		// Arrange
		_cli.Run("copilot", Arg.Any<string[]>()).Returns(Err("plugin is not installed"));
		CopilotAgent agent = new(_fileSystem, _home, _cli);

		// Act
		AgentOutcome outcome = agent.Delete(new AgentOperationContext(SkillOperationKind.Delete, null));

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded, because: "delete tolerates 'not found / not installed' (idempotent)");
	}

	[Test]
	[Description("Delete fails when uninstall fails for a non-tolerated reason.")]
	public void CopilotDelete_ShouldFail_WhenUninstallFailsHard() {
		// Arrange
		_cli.Run("copilot", Arg.Is<string[]>(a => a.Contains("uninstall"))).Returns(Err("permission denied"));
		CopilotAgent agent = new(_fileSystem, _home, _cli);

		// Act
		AgentOutcome outcome = agent.Delete(new AgentOperationContext(SkillOperationKind.Delete, null));

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Failed, because: "a hard uninstall failure must surface as Failed");
	}

	// ---- CodexAgent: legacy cleanup + MCP merge ----

	[Test]
	[Description("Codex install cleans legacy state (dirs + TOML sections + agents catalog) and merges the clio MCP server.")]
	public void CodexInstall_ShouldCleanLegacyState_AndMergeMcpServer() {
		// Arrange
		CodexAgent agent = new(_fileSystem, _home, _cli, _toml, _json);

		// Act
		AgentOutcome outcome = agent.Install(_installContext);

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded, because: "the install steps succeeded");
		_toml.Received().RemoveMarketplaceSection(Arg.Any<string>(), ToolkitDistribution.MarketplaceName);
		_toml.Received().RemovePluginSection(Arg.Any<string>(), ToolkitDistribution.PluginName, ToolkitDistribution.MarketplaceName);
		_toml.Received().RemoveSkillConfigOverride(Arg.Any<string>(), $"{ToolkitDistribution.PluginName}:{ToolkitDistribution.SkillName}");
		_json.Received().RemovePersonalMarketplacePluginEntry(Arg.Any<string>(), ToolkitDistribution.MarketplaceName, ToolkitDistribution.PluginName);
		_toml.Received(1).MergeClioMcpServer(Arg.Any<string>());
	}

	[Test]
	[Description("Codex update removes installer-owned directories containing ordinary and nested read-only files.")]
	public void CodexUpdate_ShouldRemoveLegacyDirectories_WhenFilesAreOrdinaryOrReadOnly() {
		// Arrange
		string codexHome = _home.GetAgentHome("codex");
		string legacyMarketplace = Path.Combine(codexHome, "plugins", "marketplaces",
			ToolkitDistribution.MarketplaceName);
		string legacyCache = Path.Combine(codexHome, "plugins", "cache", ToolkitDistribution.MarketplaceName);
		string readOnlyPack = Path.Combine(legacyCache, ToolkitDistribution.PluginName, "1.4.0", ".git",
			"objects", "pack", "pack-test.idx");
		_mockFileSystem.AddFile(Path.Combine(legacyMarketplace, "marketplace.json"), new MockFileData("{}"));
		_mockFileSystem.AddFile(readOnlyPack, new MockFileData("git index") {
			Attributes = FileAttributes.ReadOnly
		});
		CodexAgent agent = new(_fileSystem, _home, _cli, _toml, _json);

		// Act
		AgentOutcome outcome = agent.Update(new AgentOperationContext(SkillOperationKind.Update, null));

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded,
			because: "read-only files in a legacy Codex cache should not prevent the toolkit update");
		_mockFileSystem.Directory.Exists(legacyMarketplace).Should().BeFalse(
			because: "an ordinary installer-owned marketplace directory should still be removed");
		_mockFileSystem.Directory.Exists(legacyCache).Should().BeFalse(
			because: "the installer-owned legacy cache should be removed before reinstalling the plugin");
	}

	[Test]
	[Description("Codex update treats a directory reparse point as a leaf while preparing a legacy cache for deletion.")]
	public void CodexUpdate_ShouldNotTraverseDirectory_WhenDirectoryIsReparsePoint() {
		// Arrange
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		fileSystem.Combine(Arg.Any<string[]>())
			.Returns(call => Path.Combine(call.Arg<string[]>()));
		string codexHome = _home.GetAgentHome("codex");
		string legacyCache = Path.Combine(codexHome, "plugins", "cache", ToolkitDistribution.MarketplaceName);
		string linkedDirectory = Path.Combine(legacyCache, "linked-directory");
		fileSystem.ExistsDirectory(Arg.Any<string>())
			.Returns(call => string.Equals(call.Arg<string>(), legacyCache, StringComparison.Ordinal));
		System.IO.Abstractions.IDirectoryInfo cacheInfo =
			Substitute.For<System.IO.Abstractions.IDirectoryInfo>();
		cacheInfo.Attributes.Returns(FileAttributes.Directory);
		System.IO.Abstractions.IDirectoryInfo linkInfo =
			Substitute.For<System.IO.Abstractions.IDirectoryInfo>();
		linkInfo.Attributes.Returns(FileAttributes.Directory | FileAttributes.ReparsePoint);
		fileSystem.GetDirectoryInfo(legacyCache).Returns(cacheInfo);
		fileSystem.GetDirectoryInfo(linkedDirectory).Returns(linkInfo);
		fileSystem.GetFilesInfos(legacyCache, "*", SearchOption.TopDirectoryOnly)
			.Returns([]);
		fileSystem.GetDirectories(legacyCache).Returns([linkedDirectory]);
		CodexAgent agent = new(fileSystem, _home, _cli, _toml, _json);

		// Act
		AgentOutcome outcome = agent.Update(new AgentOperationContext(SkillOperationKind.Update, null));

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded,
			because: "a linked directory should be unlinked by recursive deletion without traversing its target");
		fileSystem.DidNotReceive().GetFilesInfos(linkedDirectory, "*", SearchOption.TopDirectoryOnly);
		fileSystem.DidNotReceive().GetDirectories(linkedDirectory);
		fileSystem.Received(1).DeleteDirectoryIfExists(legacyCache);
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Codex update tolerates a root or child legacy directory disappearing during read-only preparation.")]
	public void CodexUpdate_ShouldContinueCleanup_WhenDirectoryDisappearsDuringReadOnlyPreparation(
		bool rootDisappears) {
		// Arrange
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		fileSystem.Combine(Arg.Any<string[]>())
			.Returns(call => Path.Combine(call.Arg<string[]>()));
		string codexHome = _home.GetAgentHome("codex");
		string legacyCache = Path.Combine(codexHome, "plugins", "cache", ToolkitDistribution.MarketplaceName);
		string childDirectory = Path.Combine(legacyCache, "concurrently-removed");
		fileSystem.ExistsDirectory(Arg.Any<string>())
			.Returns(call => string.Equals(call.Arg<string>(), legacyCache, StringComparison.Ordinal));
		System.IO.Abstractions.IDirectoryInfo cacheInfo =
			Substitute.For<System.IO.Abstractions.IDirectoryInfo>();
		cacheInfo.Attributes.Returns(FileAttributes.Directory);
		fileSystem.GetFilesInfos(legacyCache, "*", SearchOption.TopDirectoryOnly)
			.Returns([]);
		if (rootDisappears) {
			fileSystem.GetDirectoryInfo(legacyCache)
				.Returns(_ => throw new DirectoryNotFoundException(legacyCache));
		}
		else {
			fileSystem.GetDirectoryInfo(legacyCache).Returns(cacheInfo);
			fileSystem.GetDirectories(legacyCache).Returns([childDirectory]);
			fileSystem.GetDirectoryInfo(childDirectory)
				.Returns(_ => throw new DirectoryNotFoundException(childDirectory));
		}
		CodexAgent agent = new(fileSystem, _home, _cli, _toml, _json);

		// Act
		AgentOutcome outcome = agent.Update(new AgentOperationContext(SkillOperationKind.Update, null));

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded,
			because: "a legacy directory already removed by another installer process is a successful cleanup");
		fileSystem.Received(1).DeleteDirectoryIfExists(legacyCache);
	}

	[Test]
	[Description("Codex update continues preparing sibling files when one legacy cache file disappears.")]
	public void CodexUpdate_ShouldContinuePreparingSiblings_WhenFileDisappears() {
		// Arrange
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		fileSystem.Combine(Arg.Any<string[]>())
			.Returns(call => Path.Combine(call.Arg<string[]>()));
		string codexHome = _home.GetAgentHome("codex");
		string legacyCache = Path.Combine(codexHome, "plugins", "cache", ToolkitDistribution.MarketplaceName);
		string readOnlyFilePath = Path.Combine(legacyCache, "remaining-read-only.idx");
		fileSystem.ExistsDirectory(Arg.Any<string>())
			.Returns(call => string.Equals(call.Arg<string>(), legacyCache, StringComparison.Ordinal));
		System.IO.Abstractions.IDirectoryInfo cacheInfo =
			Substitute.For<System.IO.Abstractions.IDirectoryInfo>();
		cacheInfo.Attributes.Returns(FileAttributes.Directory);
		System.IO.Abstractions.IFileInfo disappearedFile =
			Substitute.For<System.IO.Abstractions.IFileInfo>();
		disappearedFile.Attributes.Returns(_ => throw new FileNotFoundException());
		System.IO.Abstractions.IFileInfo readOnlyFile =
			Substitute.For<System.IO.Abstractions.IFileInfo>();
		readOnlyFile.Attributes.Returns(FileAttributes.ReadOnly);
		readOnlyFile.FullName.Returns(readOnlyFilePath);
		fileSystem.GetDirectoryInfo(legacyCache).Returns(cacheInfo);
		fileSystem.GetFilesInfos(legacyCache, "*", SearchOption.TopDirectoryOnly)
			.Returns([disappearedFile, readOnlyFile]);
		fileSystem.GetDirectories(legacyCache).Returns([]);
		CodexAgent agent = new(fileSystem, _home, _cli, _toml, _json);

		// Act
		AgentOutcome outcome = agent.Update(new AgentOperationContext(SkillOperationKind.Update, null));

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded,
			because: "one concurrently removed file should not prevent cleanup of its remaining siblings");
		fileSystem.Received(1).ResetFileReadOnlyAttribute(readOnlyFilePath);
		fileSystem.Received(1).DeleteDirectoryIfExists(legacyCache);
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Codex update tolerates its legacy cache disappearing immediately before recursive deletion.")]
	public void CodexUpdate_ShouldSucceed_WhenLegacyCacheDisappearsBeforeDelete(bool directoryDisappears) {
		// Arrange
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		fileSystem.Combine(Arg.Any<string[]>())
			.Returns(call => Path.Combine(call.Arg<string[]>()));
		string codexHome = _home.GetAgentHome("codex");
		string legacyCache = Path.Combine(codexHome, "plugins", "cache", ToolkitDistribution.MarketplaceName);
		fileSystem.ExistsDirectory(Arg.Any<string>())
			.Returns(call => string.Equals(call.Arg<string>(), legacyCache, StringComparison.Ordinal));
		System.IO.Abstractions.IDirectoryInfo cacheInfo =
			Substitute.For<System.IO.Abstractions.IDirectoryInfo>();
		cacheInfo.Attributes.Returns(FileAttributes.Directory);
		fileSystem.GetDirectoryInfo(legacyCache).Returns(cacheInfo);
		fileSystem.GetFilesInfos(legacyCache, "*", SearchOption.TopDirectoryOnly)
			.Returns([]);
		fileSystem.GetDirectories(legacyCache).Returns([]);
		Exception disappearance = directoryDisappears
			? new DirectoryNotFoundException(legacyCache)
			: new FileNotFoundException(null, legacyCache);
		fileSystem.When(fs => fs.DeleteDirectoryIfExists(legacyCache))
			.Do(_ => throw disappearance);
		CodexAgent agent = new(fileSystem, _home, _cli, _toml, _json);

		// Act
		AgentOutcome outcome = agent.Update(new AgentOperationContext(SkillOperationKind.Update, null));

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded,
			because: "a legacy cache already removed before deletion is a successful cleanup");
		_cli.Received().Run("codex", Arg.Is<string[]>(args => args.Contains("add")));
	}

	// ---- CursorAgent ----

	[Test]
	[Description("Cursor install of the default source resolves the release ref, copies the runtime, merges mcp.json, and writes the rule.")]
	public void CursorInstall_ShouldResolveReleaseRef_CopyMergeAndWriteRule_ForDefaultSource() {
		// Arrange
		IWorkingDirectoriesProvider wdp = Substitute.For<IWorkingDirectoriesProvider>();
		ISkillRepositoryResolver resolver = Substitute.For<ISkillRepositoryResolver>();
		string sourceRoot = Path.Combine(Root, "cache", "toolkit");
		resolver.Resolve(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new ResolvedSkillRepository(ToolkitDistribution.MarketplaceGitUrl, sourceRoot, "abc", wdp));
		ICursorPluginRuntimeInstaller runtime = Substitute.For<ICursorPluginRuntimeInstaller>();
		ICursorRuleWriter ruleWriter = Substitute.For<ICursorRuleWriter>();
		CursorAgent agent = new(_fileSystem, _home, resolver, runtime, _json, ruleWriter);

		// Act
		AgentOutcome outcome = agent.Install(_installContext);

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded, because: "the Cursor file-copy install succeeded");
		resolver.Received(1).Resolve(ToolkitDistribution.MarketplaceGitUrl, ToolkitDistribution.ReleaseRef);
		runtime.Received(1).Install(sourceRoot, Arg.Is<string>(p => p.Contains(ToolkitDistribution.PluginName)));
		_json.Received(1).MergeClioMcpServer(Arg.Is<string>(p => p.EndsWith("mcp.json")));
		ruleWriter.Received(1).WriteOrchestratorRule(Arg.Is<string>(p => p.EndsWith($"{ToolkitDistribution.SkillName}.mdc")), Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Cursor install with an explicit --repo uses that source and does not force the release ref.")]
	public void CursorInstall_ShouldUseOverride_WithoutReleaseRef() {
		// Arrange
		IWorkingDirectoriesProvider wdp = Substitute.For<IWorkingDirectoriesProvider>();
		ISkillRepositoryResolver resolver = Substitute.For<ISkillRepositoryResolver>();
		resolver.Resolve(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new ResolvedSkillRepository("o", Path.Combine(Root, "src"), "h", wdp));
		CursorAgent agent = new(_fileSystem, _home, resolver, Substitute.For<ICursorPluginRuntimeInstaller>(), _json, Substitute.For<ICursorRuleWriter>());
		string overrideRepo = Path.Combine(Root, "local", "toolkit");

		// Act
		agent.Install(new AgentOperationContext(SkillOperationKind.Install, overrideRepo));

		// Assert
		resolver.Received(1).Resolve(overrideRepo, null);
	}

	[Test]
	[Description("Cursor delete removes the local plugin dir and the rule file, and does not touch mcp.json (decision O1).")]
	public void CursorDelete_ShouldRemovePluginDirAndRule_LeavingMcpJson() {
		// Arrange
		string cursorHome = Path.Combine(Root, "home", ".cursor");
		string localPluginDir = Path.Combine(cursorHome, "plugins", "local", ToolkitDistribution.PluginName);
		string rulePath = Path.Combine(cursorHome, "rules", $"{ToolkitDistribution.SkillName}.mdc");
		_mockFileSystem.AddFile(Path.Combine(localPluginDir, "AGENTS.md"), new MockFileData("x"));
		_mockFileSystem.AddFile(rulePath, new MockFileData("rule"));
		CursorAgent agent = new(_fileSystem, _home, Substitute.For<ISkillRepositoryResolver>(),
			Substitute.For<ICursorPluginRuntimeInstaller>(), _json, Substitute.For<ICursorRuleWriter>());

		// Act
		AgentOutcome outcome = agent.Delete(new AgentOperationContext(SkillOperationKind.Delete, null));

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Succeeded, because: "delete should succeed");
		_mockFileSystem.Directory.Exists(localPluginDir).Should().BeFalse(because: "the local plugin dir is removed");
		_mockFileSystem.File.Exists(rulePath).Should().BeFalse(because: "the orchestrator rule is removed");
	}

	[Test]
	[Description("Cursor install surfaces a missing release manifest as a curated Failed outcome rather than a raw exception.")]
	public void CursorInstall_ShouldReturnFailed_WhenRuntimeInstallerThrows() {
		// Arrange
		IWorkingDirectoriesProvider wdp = Substitute.For<IWorkingDirectoriesProvider>();
		ISkillRepositoryResolver resolver = Substitute.For<ISkillRepositoryResolver>();
		resolver.Resolve(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new ResolvedSkillRepository("s", Path.Combine(Root, "src"), "h", wdp));
		ICursorPluginRuntimeInstaller runtime = Substitute.For<ICursorPluginRuntimeInstaller>();
		runtime.When(r => r.Install(Arg.Any<string>(), Arg.Any<string>()))
			.Do(_ => throw new InvalidOperationException("missing .release-manifest.json"));
		CursorAgent agent = new(_fileSystem, _home, resolver, runtime, _json, Substitute.For<ICursorRuleWriter>());

		// Act
		AgentOutcome outcome = agent.Install(_installContext);

		// Assert
		outcome.Status.Should().Be(AgentOutcomeStatus.Failed, because: "a known installer failure is curated into Failed");
		outcome.Message.Should().Contain("release-manifest", because: "the curated message preserves the actionable detail");
	}

	private static AgentCliResult Ok() => new(true, 0, string.Empty, string.Empty);

	private static AgentCliResult Err(string errorText) => new(false, 1, string.Empty, errorText);
}
