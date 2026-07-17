using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WhenToUseRequestsGuidanceTests {

	[Test]
	[Description("Lists and resolves when-to-use-requests through get-guidance, returning the canonical article with the catalog-discipline anchors.")]
	public async Task GuidanceGet_ShouldResolveWhenToUseRequests_WhenRequestedByName() {
		// Arrange
		GuidanceGetTool tool = new(Substitute.For<IFeatureToggleService>());

		// Act
		GuidanceGetResponse listing = await tool.GetGuidance(new GuidanceGetArgs("not-a-guide"));
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("when-to-use-requests"));

		// Assert
		listing.AvailableGuides.Should().Contain("when-to-use-requests",
			because: "the guide must always be advertised in availableGuides");
		result.Success.Should().BeTrue(
			because: "the guide must resolve through get-guidance by its canonical name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/when-to-use-requests",
			because: "the canonical article URI must be stable");
		result.Article.Text.Should().Contain("clio MCP when-to-use-requests guide",
			because: "the canonical article header must be returned");
		result.Article.Text.Should().Contain("get-request-info",
			because: "the guide's core discipline is fetching the request contract from the catalog tool");
		result.Article.Text.Should().Contain("baseParameters",
			because: "the guide must forbid authoring platform-injected base fields through params");
		result.Article.Text.Should().Contain("accepts NO parameters",
			because: "the guide must explain the empty-parameters-map semantics (no params block at all)");
		result.Article.Text.Should().Contain("next?.handle(request)",
			because: "the guide must show the chaining rule for pre-action custom logic");
		result.Article.Text.Should().Contain("page-schema-handlers",
			because: "the guide must route handler mechanics to the owning guide instead of duplicating it");
		result.Article.Text.Should().Contain("crt.RunBusinessProcessRequest",
			because: "the run-a-business-process special path must route to the request catalog (get-request-info crt.RunBusinessProcessRequest)");
		result.Article.Text.Should().Contain("valueSource",
			because: "the guide must carry the hard rule that environment-dependent values come only from the probe named by the valueSource annotation");
		result.Article.Text.Should().Contain("list-printables",
			because: "the printables probe must be named as the templateId resolution path");
	}

	[Test]
	[Description("get-guidance name=routing carries the request-wiring rows, keeping the request catalog discoverable through the mandated get-guidance path.")]
	public async Task GuidanceGet_ShouldRouteRequestWiring_WhenRoutingGuideRequested() {
		// Arrange
		GuidanceGetTool tool = new(Substitute.For<IFeatureToggleService>());

		// Act
		GuidanceGetResponse routing = await tool.GetGuidance(new GuidanceGetArgs("routing"));

		// Assert
		routing.Success.Should().BeTrue(because: "the routing map is a core, always-available guide");
		routing.Article!.Text.Should().Contain("-> get-request-info + name=when-to-use-requests",
			because: "the map must route button/menu action wiring to the request catalog and selection guide");
		routing.Article.Text.Should().Contain("get-request-info (crt.RunBusinessProcessRequest)",
			because: "the run-a-process-button task must route to get-process-signature + the request catalog");
	}

	[Test]
	[Description("get-guidance serves page-modification with its when-to-use-requests GATE row and mobile-page-modification with its get-request-info catalog pointer.")]
	public async Task GuidanceGet_ShouldIncludeRequestWiring_WhenPageGuidesRequested() {
		// Arrange
		GuidanceGetTool tool = new(Substitute.For<IFeatureToggleService>());

		// Act
		GuidanceGetResponse web = await tool.GetGuidance(new GuidanceGetArgs("page-modification"));
		GuidanceGetResponse mobile = await tool.GetGuidance(new GuidanceGetArgs("mobile-page-modification"));

		// Assert
		web.Success.Should().BeTrue(because: "page-modification is a core, always-available guide");
		web.Article!.Text.Should().Contain("| `when-to-use-requests` |",
			because: "the GATE table must route the run-process task to the selection guide");
		mobile.Success.Should().BeTrue(because: "mobile-page-modification is a core, always-available guide");
		mobile.Article!.Text.Should().Contain("get-request-info request-type=crt.RunBusinessProcessRequest",
			because: "the mobile guide must carry the catalog pointer for the run-process contract");
	}
}
