using Clio.Command.McpServer.Resources;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture, Category("Unit"), Property("Module", "McpServer")]
public class IntegrationTestingGuidanceResourceTests {
	[Test]
	[Description("Registers guidance that keeps the generated project portable across local and CI execution.")]
	public void Guidance_Should_Describe_Ci_Authentication_And_Scenario_Boundary() {
		// Arrange
		string text = IntegrationTestingGuidanceResource.Guide.Text;

		// Act
		bool found = GuidanceCatalog.TryGet("integration-testing", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue("because integration-testing must be retrievable through get-guidance");
		entry.Article.Text.Should().Be(text, "because the catalog must expose the canonical resource article");
		text.Should().Contain("CREATIO_ACCESS_TOKEN", "because CI can authenticate without a local clio environment");
		text.Should().Contain("does not force a browser dependency", "because Playwright is scenario-specific");
	}
}
