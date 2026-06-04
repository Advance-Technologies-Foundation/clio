using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class DesignTokensGuidanceResourceTests {

	[Test]
	[Category("Unit")]
	[Description("Returns the design-token catalog from the single embedded source as a plain-text MCP resource with the stable URI, the catalog title, and representative token names and default values.")]
	public void DesignTokensGuidanceResource_Should_Return_Embedded_Catalog() {
		// Arrange
		DesignTokensGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the design-tokens catalog should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/design-tokens",
			because: "the resource should expose a stable MCP URI for the design-token catalog");
		article.MimeType.Should().Be("text/plain",
			because: "the catalog should be discoverable as plain text, like the sibling guidance resources");
		article.Text.Should().Contain("Design Tokens AI Guide",
			because: "the text must be loaded from the embedded DESIGN_TOKENS_AI_GUIDE.md single source");
		article.Text.Should().Contain("--crt-color-text-body",
			because: "the catalog should list the semantic color tokens by name");
		article.Text.Should().Contain("#004fd6",
			because: "the catalog should carry concrete default-theme values an agent can use as var() fallbacks");
	}

	[Test]
	[Category("Unit")]
	[Description("Registers the design-tokens catalog in the guidance catalog so the get-guidance tool can resolve it by name.")]
	public void GuidanceCatalog_Should_Resolve_DesignTokens_Guide() {
		// Act
		bool found = GuidanceCatalog.TryGet("design-tokens", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(because: "design-tokens should be a registered guidance name");
		entry.Article.Uri.Should().Be("docs://mcp/guides/design-tokens",
			because: "the catalog entry should point at the canonical design-tokens article");
		GuidanceCatalog.GetNames().Should().Contain("design-tokens",
			because: "design-tokens should be advertised among the available guides");
	}
}
