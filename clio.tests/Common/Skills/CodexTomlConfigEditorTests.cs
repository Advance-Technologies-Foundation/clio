using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common.Skills;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.Skills;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class CodexTomlConfigEditorTests {
	private static string Root => OperatingSystem.IsWindows() ? @"C:\" : "/";
	private static string ConfigPath => Path.Combine(Root, "home", ".codex", "config.toml");

	private MockFileSystem _mockFileSystem = null!;
	private CodexTomlConfigEditor _sut = null!;

	[SetUp]
	public void SetUp() {
		_mockFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), Root);
		_sut = new CodexTomlConfigEditor(new Clio.Common.FileSystem(_mockFileSystem));
	}

	[Test]
	[Description("Merge appends the clio MCP block while preserving comments and unrelated server tables.")]
	public void MergeClioMcpServer_ShouldAppendBlock_AndPreserveExistingContent() {
		// Arrange
		const string original = "# user comment\n[mcp_servers.other]\ncommand = \"other\"\n";
		_mockFileSystem.AddFile(ConfigPath, new MockFileData(original));

		// Act
		_sut.MergeClioMcpServer(ConfigPath);

		// Assert
		string updated = _mockFileSystem.File.ReadAllText(ConfigPath);
		updated.Should().Contain("[mcp_servers.clio]", because: "merge should add the clio MCP server table");
		updated.Should().Contain("# user comment", because: "merge must preserve unrelated comments");
		updated.Should().Contain("[mcp_servers.other]", because: "merge must preserve the user's other MCP servers");
		updated.Should().Contain("command = \"clio\"", because: "the clio block carries the clio command");
		updated.Should().Contain("args = [\"mcp-server\"]", because: "the clio block carries the mcp-server arg");
	}

	[Test]
	[Description("Merge is idempotent: re-running it does not add a second clio block.")]
	public void MergeClioMcpServer_ShouldBeIdempotent_WhenClioBlockPresent() {
		// Arrange
		_mockFileSystem.AddFile(ConfigPath, new MockFileData(string.Empty));
		_sut.MergeClioMcpServer(ConfigPath);
		string afterFirst = _mockFileSystem.File.ReadAllText(ConfigPath);

		// Act
		_sut.MergeClioMcpServer(ConfigPath);

		// Assert
		string afterSecond = _mockFileSystem.File.ReadAllText(ConfigPath);
		afterSecond.Should().Be(afterFirst, because: "a second merge must be a no-op when the clio block already exists");
		CountOccurrences(afterSecond, "[mcp_servers.clio]").Should().Be(1,
			because: "there must be exactly one clio MCP server table");
	}

	[Test]
	[Description("Removing the marketplace block deletes only that table and preserves surrounding content.")]
	public void RemoveMarketplaceSection_ShouldRemoveOnlyTargetBlock() {
		// Arrange
		const string original =
			"[marketplaces.creatio]\nsource = \"x\"\n\n[mcp_servers.clio]\ncommand = \"clio\"\n";
		_mockFileSystem.AddFile(ConfigPath, new MockFileData(original));

		// Act
		_sut.RemoveMarketplaceSection(ConfigPath, "creatio");

		// Assert
		string updated = _mockFileSystem.File.ReadAllText(ConfigPath);
		updated.Should().NotContain("[marketplaces.creatio]", because: "the targeted marketplace block must be removed");
		updated.Should().Contain("[mcp_servers.clio]", because: "unrelated tables must remain intact");
	}

	[Test]
	[Description("Removing the skill config override drops only the [[skills.config]] block matching the skill name.")]
	public void RemoveSkillConfigOverride_ShouldRemoveOnlyMatchingBlock() {
		// Arrange
		const string skillName = "creatio-ai-app-development-toolkit:creatio-app-orchestrator";
		const string original =
			"[[skills.config]]\nname = \"keep-me\"\n\n" +
			"[[skills.config]]\nname = \"creatio-ai-app-development-toolkit:creatio-app-orchestrator\"\nenabled = false\n";
		_mockFileSystem.AddFile(ConfigPath, new MockFileData(original));

		// Act
		_sut.RemoveSkillConfigOverride(ConfigPath, skillName);

		// Assert
		string updated = _mockFileSystem.File.ReadAllText(ConfigPath);
		updated.Should().Contain("name = \"keep-me\"", because: "unrelated skill config blocks must be preserved");
		updated.Should().NotContain(skillName, because: "only the matching skill override should be removed");
	}

	[Test]
	[Description("Removing a section is a no-op when the config file does not exist.")]
	public void RemoveMarketplaceSection_ShouldNotThrow_WhenFileMissing() {
		// Act
		Action act = () => _sut.RemoveMarketplaceSection(ConfigPath, "creatio");

		// Assert
		act.Should().NotThrow(because: "editing an absent config file must be a safe no-op");
	}

	private static int CountOccurrences(string text, string token) {
		int count = 0;
		int index = 0;
		while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0) {
			count++;
			index += token.Length;
		}

		return count;
	}
}
