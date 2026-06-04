using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class CreatioThemeGuidanceResourceTests {

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical Creatio theme guidance article as a plain-text MCP resource with the stable URI and the key contract phrases.")]
	public void CreatioThemeGuidanceResource_Should_Return_Canonical_Theme_Guide() {
		// Arrange
		CreatioThemeGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the theme guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/creatio-theme",
			because: "the resource should expose a stable MCP URI for theme guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the theme guide should be discoverable as plain text");
		article.Text.Should().Contain("Files/themes/<cssClassName>/",
			because: "the guide should state the theme artifact location");
		article.Text.Should().Contain("\"id\", \"caption\", \"cssClassName\"",
			because: "the guide should state the exact theme.json contract");
		article.Text.Should().Contain(".<cssClassName> {",
			because: "the guide should explain that theme variables are scoped under the theme class");
		article.Text.Should().Contain(":root primitives",
			because: "the guide should warn which platform variables must not be redefined in a theme");
		article.Text.Should().Contain("new-theme",
			because: "the guide should reference the workspace scaffold tool");
		article.Text.Should().Contain("clear-redis-db",
			because: "the guide should describe theme-cache activation for the workspace flow");
		article.Text.Should().Contain("Google Fonts",
			because: "the guide should cover both local and external font integration");
	}

	[Test]
	[Category("Unit")]
	[Description("Registers the creatio-theme guidance in the catalog so the get-guidance tool can resolve it by name.")]
	public void GuidanceCatalog_Should_Resolve_CreatioTheme_Guide() {
		// Act
		bool found = GuidanceCatalog.TryGet("creatio-theme", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(because: "creatio-theme should be a registered guidance name");
		entry.Article.Uri.Should().Be("docs://mcp/guides/creatio-theme",
			because: "the catalog entry should point at the canonical theme guidance article");
		GuidanceCatalog.GetNames().Should().Contain("creatio-theme",
			because: "creatio-theme should be advertised among the available guides");
	}
}
