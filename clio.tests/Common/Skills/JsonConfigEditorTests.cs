using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using Clio.Common.Skills;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.Skills;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class JsonConfigEditorTests {
	private static string Root => OperatingSystem.IsWindows() ? @"C:\" : "/";

	private MockFileSystem _mockFileSystem = null!;
	private JsonConfigEditor _sut = null!;

	[SetUp]
	public void SetUp() {
		_mockFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), Root);
		_sut = new JsonConfigEditor(new Clio.Common.FileSystem(_mockFileSystem));
	}

	private static string P(params string[] parts) {
		string path = Root;
		foreach (string part in parts) {
			path = Path.Combine(path, part);
		}

		return path;
	}

	[Test]
	[Description("Enabling marketplace auto-update sets autoUpdate=true and preserves unrelated settings keys.")]
	public void EnableMarketplaceAutoUpdate_ShouldSetAutoUpdate_AndPreserveOtherKeys() {
		// Arrange
		string path = P("home", ".claude", "settings.json");
		_mockFileSystem.AddFile(path, new MockFileData("{ \"theme\": \"dark\" }"));

		// Act
		_sut.EnableMarketplaceAutoUpdate(path, "creatio");

		// Assert
		JsonObject settings = ReadObject(path);
		settings["theme"]!.GetValue<string>().Should().Be("dark", because: "unrelated keys must be preserved");
		settings["extraKnownMarketplaces"]!["creatio"]!["autoUpdate"]!.GetValue<bool>().Should().BeTrue(
			because: "auto-update should be enabled for the marketplace");
	}

	[Test]
	[Description("Enabling marketplace auto-update drops a stale directory-source entry.")]
	public void EnableMarketplaceAutoUpdate_ShouldDropStaleDirectorySource() {
		// Arrange
		string path = P("home", ".claude", "settings.json");
		_mockFileSystem.AddFile(path, new MockFileData(
			"{ \"extraKnownMarketplaces\": { \"creatio\": { \"source\": { \"source\": \"directory\" } } } }"));

		// Act
		_sut.EnableMarketplaceAutoUpdate(path, "creatio");

		// Assert
		JsonObject creatio = ReadObject(path)["extraKnownMarketplaces"]!["creatio"]!.AsObject();
		creatio.ContainsKey("source").Should().BeFalse(because: "a stale directory source must be dropped");
		creatio["autoUpdate"]!.GetValue<bool>().Should().BeTrue(because: "auto-update should still be enabled");
	}

	[Test]
	[Description("Merging the clio MCP server adds it when absent and skips when already present.")]
	public void MergeClioMcpServer_ShouldAddWhenAbsent_AndSkipWhenPresent() {
		// Arrange
		string path = P("home", ".cursor", "mcp.json");
		_mockFileSystem.AddFile(path, new MockFileData("{ \"mcpServers\": { \"other\": { \"command\": \"x\" } } }"));

		// Act
		_sut.MergeClioMcpServer(path);
		_sut.MergeClioMcpServer(path); // second call must be a no-op

		// Assert
		JsonObject servers = ReadObject(path)["mcpServers"]!.AsObject();
		servers.ContainsKey("other").Should().BeTrue(because: "existing servers must be preserved");
		servers["clio"]!["command"]!.GetValue<string>().Should().Be("clio", because: "the clio server should be added");
		servers["clio"]!["args"]!.AsArray()[0]!.GetValue<string>().Should().Be("mcp-server",
			because: "the clio server should carry the mcp-server arg");
	}

	[Test]
	[Description("Removing the marketplace from settings drops the entry under extraKnownMarketplaces.")]
	public void RemoveMarketplaceFromSettings_ShouldRemoveEntry() {
		// Arrange
		string path = P("home", ".claude", "settings.json");
		_mockFileSystem.AddFile(path, new MockFileData(
			"{ \"extraKnownMarketplaces\": { \"creatio\": { \"autoUpdate\": true } } }"));

		// Act
		_sut.RemoveMarketplaceFromSettings(path, "creatio");

		// Assert
		ReadObject(path)["extraKnownMarketplaces"]!.AsObject().ContainsKey("creatio").Should().BeFalse(
			because: "the marketplace entry should be removed on delete");
	}

	[Test]
	[Description("Removing the toolkit plugin entry deletes an installer-owned catalog that becomes empty.")]
	public void RemovePersonalMarketplacePluginEntry_ShouldDeleteEmptyOwnedCatalog() {
		// Arrange
		string path = P("home", ".agents", "plugins", "marketplace.json");
		_mockFileSystem.AddFile(path, new MockFileData(
			"{ \"name\": \"creatio\", \"plugins\": [ { \"name\": \"creatio-ai-app-development-toolkit\" } ] }"));

		// Act
		_sut.RemovePersonalMarketplacePluginEntry(path, "creatio", "creatio-ai-app-development-toolkit");

		// Assert
		_mockFileSystem.File.Exists(path).Should().BeFalse(
			because: "an installer-owned catalog with no remaining plugins should be deleted");
	}

	private JsonObject ReadObject(string path) =>
		JsonNode.Parse(_mockFileSystem.File.ReadAllText(path))!.AsObject();
}
