using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Common.Skills;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Skills;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
[NonParallelizable]
public sealed class InstalledToolkitVersionsTests {
	private MockFileSystem _files;
	private ServiceProvider _provider;
	private IInstalledToolkitVersions _sut;
	private string _originalCodexHome;
	private string _originalClaudeHome;
	private static string Root => OperatingSystem.IsWindows() ? @"C:\home" : "/home";

	[SetUp]
	public void SetUp() {
		_originalCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
		_originalClaudeHome = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
		Environment.SetEnvironmentVariable("CODEX_HOME", null);
		Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
		ServiceCollection services = new();
		services.AddSingleton<MockFileSystem>();
		services.AddSingleton<System.IO.Abstractions.IFileSystem>(provider => provider.GetRequiredService<MockFileSystem>());
		services.AddSingleton<Clio.Common.IFileSystem, Clio.Common.FileSystem>();
		IUserHomeProvider home = Substitute.For<IUserHomeProvider>();
		home.GetAgentHome(Arg.Any<string>()).Returns(call => Path.Combine(Root, "." + call.Arg<string>()));
		services.AddSingleton(home);
		services.AddSingleton<IInstalledToolkitVersions, InstalledToolkitVersions>();
		_provider = services.BuildServiceProvider();
		_files = _provider.GetRequiredService<MockFileSystem>();
		_sut = _provider.GetRequiredService<IInstalledToolkitVersions>();
	}

	[TearDown]
	public void TearDown() {
		Environment.SetEnvironmentVariable("CODEX_HOME", _originalCodexHome);
		Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _originalClaudeHome);
		_provider.Dispose();
	}

	[Test]
	[Description("CODEX_HOME selects the same installation that the Codex CLI updates, even when the default home is stale.")]
	public void Read_ShouldHonorCodexHome_WhenOverrideIsSet() {
		// Arrange
		const string relative = "plugins/cache/creatio/creatio-ai-app-development-toolkit/1.10.0/.codex-plugin/plugin.json";
		AddManifest("codex", relative, "1.0.0");
		string customHome = Path.Combine(Root, "custom-codex");
		Environment.SetEnvironmentVariable("CODEX_HOME", customHome);
		_files.AddFile(Path.Combine(customHome, relative), new MockFileData(JsonSerializer.Serialize(new {
			name = ToolkitDistribution.PluginName, version = "2.0.0"
		})));
		// Act
		var versions = _sut.Read();
		// Assert
		versions["codex"].Should().Be("2.0.0", because: "the custom installation is the one updated by the CLI");
	}

	[Test]
	[Description("All four agents report absence on a clean home without invoking any external program.")]
	public void Read_ShouldReportMissing_WhenNoToolkitIsInstalled() {
		// Arrange / Act
		IReadOnlyDictionary<string, string> versions = _sut.Read();
		// Assert
		versions.Should().HaveCount(4, because: "each supported agent has a visible status");
		versions.Values.Should().OnlyContain(value => value == "not installed", because: "a clean home has no installed toolkit");
	}

	[TestCase("cursor", "plugins/local/creatio-ai-app-development-toolkit/.cursor-plugin/plugin.json")]
	[TestCase("copilot", "installed-plugins/creatio/creatio-ai-app-development-toolkit/plugin.json")]
	[TestCase("copilot", "installed-plugins/creatio/creatio-ai-app-development-toolkit/.github/plugin/plugin.json")]
	[TestCase("codex", "plugins/cache/creatio/creatio-ai-app-development-toolkit/1.10.0/.codex-plugin/plugin.json")]
	[Description("Installed manifests supply the toolkit version independently of clio or cache directory names.")]
	public void Read_ShouldReadManifestVersion_WhenAgentIsInstalled(string agent, string relative) {
		// Arrange
		AddManifest(agent, relative, "2.3.4-beta.1");
		// Act
		var versions = _sut.Read();
		// Assert
		versions[agent].Should().Be("2.3.4-beta.1", because: "only the installed manifest supplies the product version");
	}

	[TestCase("1.9.0", "1.10.0")]
	[TestCase("1.10.0-beta.1", "1.10.0")]
	[TestCase("1.10.0", "local")]
	[Description("Codex selection follows semver ordering and gives local payloads priority over older caches.")]
	public void Read_ShouldSelectActiveCodexPayload_WhenMultipleCachesExist(string older, string active) {
		// Arrange
		AddManifest("codex", $"plugins/cache/creatio/creatio-ai-app-development-toolkit/{older}/.codex-plugin/plugin.json", "1.0.0");
		AddManifest("codex", $"plugins/cache/creatio/creatio-ai-app-development-toolkit/{active}/.codex-plugin/plugin.json", "2.0.0");
		// Act
		var versions = _sut.Read();
		// Assert
		versions["codex"].Should().Be("2.0.0", because: "stale cache entries must not become the reported active version");
	}

	[Test]
	[Description("Claude user-scope registry data is used while project entries are excluded from global reporting.")]
	public void Read_ShouldReportClaudeUserVersion_WhenRegistryContainsMultipleScopes() {
		// Arrange
		string installPath = Path.Combine(Root, ".claude", "installed");
		_files.AddDirectory(installPath);
		string registry = JsonSerializer.Serialize(new { plugins = new Dictionary<string, object> {
			[ToolkitDistribution.PluginSource] = new[] {
				new { scope = "project", version = "9.0.0", installPath },
				new { scope = "user", version = "1.10.0", installPath }
			}
		} });
		_files.AddFile(Path.Combine(Root, ".claude", "plugins", "installed_plugins.json"), new MockFileData(registry));
		// Act
		var versions = _sut.Read();
		// Assert
		versions["claude"].Should().Be("1.10.0", because: "update-toolkit manages global user installations");
	}

	[TestCase("{")]
	[TestCase("[]")]
	[TestCase("{\"name\":\"creatio-ai-app-development-toolkit\",\"version\":42}")]
	[TestCase("{\"name\":\"other\",\"version\":\"1.0.0\"}")]
	[Description("Malformed metadata is isolated to its agent and does not suppress valid sibling versions.")]
	public void Read_ShouldIsolateInvalidMetadata_WhenManifestIsUnreadable(string json) {
		// Arrange
		_files.AddFile(Path.Combine(Root, ".cursor", "plugins/local/creatio-ai-app-development-toolkit/.cursor-plugin/plugin.json"), new MockFileData(json));
		AddManifest("copilot", "installed-plugins/creatio/creatio-ai-app-development-toolkit/plugin.json", "3.0.0");
		// Act
		var versions = _sut.Read();
		// Assert
		versions["cursor"].Should().Be("unknown (metadata unavailable)", because: "unreadable metadata cannot establish a version");
		versions["copilot"].Should().Be("3.0.0", because: "one agent's failure must not hide another agent");
	}

	private const string Commit = "6b296e3929701ac438af71302681bfa96002e78e";
	private const string CodexPayload = "plugins/cache/creatio/creatio-ai-app-development-toolkit/1.16.0";

	[Test]
	[Description("Claude's installed commit is the user-scope registry gitCommitSha, normalized to lower case.")]
	public void ReadInstalledCommit_ShouldReturnClaudeRegistryCommit_WhenUserEntryRecordsOne() {
		// Arrange
		AddClaudeRegistry(new { scope = "user", installPath = AddClaudePayload(), gitCommitSha = Commit.ToUpperInvariant() });

		// Act
		string commit = _sut.ReadInstalledCommit("claude");

		// Assert
		commit.Should().Be(Commit, because: "the commit Claude recorded is what clio compares with the release commit");
	}

	[TestCase(null)]
	[TestCase("6b296e3")]
	[TestCase("release")]
	[Description("A Claude entry that records no commit, or not a full commit, is unknown, never up to date.")]
	public void ReadInstalledCommit_ShouldReturnNull_WhenClaudeCommitIsMissingOrMalformed(string recorded) {
		// Arrange
		AddClaudeRegistry(new { scope = "user", installPath = AddClaudePayload(), gitCommitSha = recorded });

		// Act
		string commit = _sut.ReadInstalledCommit("claude");

		// Assert
		commit.Should().BeNull(because: "only a full commit may be compared with the release commit");
	}

	[Test]
	[Description("Claude user entries that disagree about the installed commit are unknown, never up to date.")]
	public void ReadInstalledCommit_ShouldReturnNull_WhenClaudeRegistryIsAmbiguousOrStale() {
		// Arrange
		string installPath = AddClaudePayload();
		AddClaudeRegistry(
			new { scope = "user", installPath, gitCommitSha = Commit },
			new { scope = "user", installPath, gitCommitSha = "1111111111111111111111111111111111111111" });

		// Act
		string commit = _sut.ReadInstalledCommit("claude");

		// Assert
		commit.Should().BeNull(because: "two different installed commits cannot both equal the release");
	}

	[Test]
	[Description("A Claude registry entry whose payload directory is gone is not reported as an installed commit.")]
	public void ReadInstalledCommit_ShouldReturnNull_WhenClaudePayloadIsMissing() {
		// Arrange
		AddClaudeRegistry(new { scope = "user", installPath = Path.Combine(Root, "gone"), gitCommitSha = Commit });

		// Act
		string commit = _sut.ReadInstalledCommit("claude");

		// Assert
		commit.Should().BeNull(because: "a removed payload must be reinstalled, not reported as up to date");
	}

	[Test]
	[Description("Codex's installed commit follows HEAD to the loose ref file of its active cache checkout.")]
	public void ReadInstalledCommit_ShouldResolveCodexLooseRef_WhenHeadIsSymbolic() {
		// Arrange
		AddCodexGitFile("HEAD", "ref: refs/heads/release\n");
		AddCodexGitFile("refs/heads/release", Commit + "\n");

		// Act
		string commit = _sut.ReadInstalledCommit("codex");

		// Assert
		commit.Should().Be(Commit, because: "this is the layout the Codex CLI leaves in its cache");
	}

	[Test]
	[Description("Codex's installed commit falls back to packed-refs when the ref has no loose file.")]
	public void ReadInstalledCommit_ShouldResolveCodexPackedRef_WhenNoLooseRefExists() {
		// Arrange
		AddCodexGitFile("HEAD", "ref: refs/heads/release\n");
		AddCodexGitFile("packed-refs",
			"# pack-refs with: peeled fully-peeled sorted\n"
			+ "1111111111111111111111111111111111111111 refs/heads/main\n"
			+ Commit + " refs/heads/release\n");

		// Act
		string commit = _sut.ReadInstalledCommit("codex");

		// Assert
		commit.Should().Be(Commit, because: "git moves refs into packed-refs after gc");
	}

	[Test]
	[Description("A detached Codex HEAD that holds the commit itself is read directly.")]
	public void ReadInstalledCommit_ShouldReadDetachedCodexHead_WhenHeadIsACommit() {
		// Arrange
		AddCodexGitFile("HEAD", Commit + "\n");

		// Act
		string commit = _sut.ReadInstalledCommit("codex");

		// Assert
		commit.Should().Be(Commit, because: "a detached checkout records the commit in HEAD");
	}

	[TestCase("ref: refs/heads/release\n")]
	[TestCase("6b296e3\n")]
	[TestCase("ref: refs/../../escape\n")]
	[TestCase("not a commit\n")]
	[Description("Unresolvable or malformed Codex git metadata is unknown, so the agent is updated as before.")]
	public void ReadInstalledCommit_ShouldReturnNull_WhenCodexHeadCannotBeResolved(string head) {
		// Arrange
		AddCodexGitFile("HEAD", head);

		// Act
		string commit = _sut.ReadInstalledCommit("codex");

		// Assert
		commit.Should().BeNull(because: "only a full, resolvable commit may stand in for 'up to date'");
	}

	[Test]
	[Description("A Codex payload that is not a git checkout reports no installed commit.")]
	public void ReadInstalledCommit_ShouldReturnNull_WhenCodexPayloadHasNoGitDirectory() {
		// Arrange
		AddManifest("codex", CodexPayload + "/.codex-plugin/plugin.json", "1.16.0");

		// Act
		string commit = _sut.ReadInstalledCommit("codex");

		// Assert
		commit.Should().BeNull(because: "a payload without git metadata cannot be compared with the release commit");
	}

	[TestCase("cursor")]
	[TestCase("copilot")]
	[TestCase("unknown-agent")]
	[Description("Agents that record no installed commit always report none, so they keep updating.")]
	public void ReadInstalledCommit_ShouldReturnNull_WhenAgentRecordsNoCommit(string agent) {
		// Arrange / Act
		string commit = _sut.ReadInstalledCommit(agent);

		// Assert
		commit.Should().BeNull(because: $"{agent} keeps no git metadata for its toolkit payload");
	}

	private string AddClaudePayload() {
		string installPath = Path.Combine(Root, ".claude", "plugins", "cache", "creatio", "toolkit", "1.16.0");
		_files.AddDirectory(installPath);
		return installPath;
	}

	private void AddClaudeRegistry(params object[] entries) {
		string registry = JsonSerializer.Serialize(new { plugins = new Dictionary<string, object> {
			[ToolkitDistribution.PluginSource] = entries
		} });
		_files.AddFile(Path.Combine(Root, ".claude", "plugins", "installed_plugins.json"), new MockFileData(registry));
	}

	private void AddCodexGitFile(string relative, string content) =>
		_files.AddFile(Path.Combine(Root, ".codex", CodexPayload, ".git", relative), new MockFileData(content));

	private void AddManifest(string agent, string relative, string version) =>
		_files.AddFile(Path.Combine(Root, "." + agent, relative), new MockFileData(JsonSerializer.Serialize(new {
			name = ToolkitDistribution.PluginName, version
		})));
}
