using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class IdentityAssertionGuidanceTests {

	[Test]
	[Category("Unit")]
	[Description("GuidanceGet returns the identity-assertion article with the onboarding sequence and prerequisites so AI callers can drive the token-exchange flow.")]
	public async Task GuidanceGet_ShouldReturnIdentityAssertionArticle_WhenRequestedByName() {
		// Arrange
		// A bare substitute returns false for every IsEnabled(...); the identity-assertion guide is not
		// feature-gated, so it stays visible regardless of toggle state.
		GuidanceGetTool tool = new(Substitute.For<IFeatureToggleService>());

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("identity-assertion"));

		// Assert
		result.Success.Should().BeTrue(because: "identity-assertion is a registered guidance name");
		result.Article.Should().NotBeNull(because: "the catalog resolves the identity-assertion guide");
		result.Article!.Uri.Should().Be("docs://mcp/guides/identity-assertion",
			because: "the guide URI must match the registered resource template");
		result.Article.Text.Should().Contain("EnableIdentityAssertionIssuer",
			because: "the guide must call out the server-side feature prerequisite");
		result.Article.Text.Should().Contain("get-identity-public-jwk",
			because: "the guide must reference the public-key export step of onboarding");
		result.Article.Text.Should().Contain("CanManageIdentityAssertionIssuer",
			because: "the guide must call out the management permission prerequisite");
	}

	[Test]
	[Category("Unit")]
	[Description("The identity-assertion guidance resource exposes a non-empty article at its docs:// URI for direct MCP resource reads.")]
	public void GetGuide_ShouldReturnArticleAtCanonicalUri_WhenRead() {
		// Arrange
		IdentityAssertionGuidanceResource resource = new();

		// Act
		object contents = resource.GetGuide();

		// Assert
		IdentityAssertionGuidanceResource.Guide.Uri.Should().Be("docs://mcp/guides/identity-assertion",
			because: "the resource and the catalog entry must share one canonical URI");
		IdentityAssertionGuidanceResource.Guide.Text.Should().NotBeNullOrWhiteSpace(
			because: "the guidance article must carry content for AI callers");
		contents.Should().BeSameAs(IdentityAssertionGuidanceResource.Guide,
			because: "GetGuide should return the shared guidance article instance");
	}

}
