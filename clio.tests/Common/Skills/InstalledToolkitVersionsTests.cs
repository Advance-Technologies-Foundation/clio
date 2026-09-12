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

	private void AddManifest(string agent, string relative, string version) =>
		_files.AddFile(Path.Combine(Root, "." + agent, relative), new MockFileData(JsonSerializer.Serialize(new {
			name = ToolkitDistribution.PluginName, version
		})));
}
