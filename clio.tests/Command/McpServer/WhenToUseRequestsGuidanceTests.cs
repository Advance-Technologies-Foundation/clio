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
public sealed class WhenToUseRequestsGuidanceTests {

	[Test]
	[Description("Lists and resolves when-to-use-requests when the requests-registry feature gate is enabled, returning the canonical article with the catalog-discipline anchors.")]
	public async Task GuidanceGet_ShouldResolveWhenToUseRequests_WhenFeatureEnabled() {
		// Arrange — enable the requests-registry gate so the guide becomes visible, exactly like process-designer.
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		featureToggleService.IsEnabled(typeof(WhenToUseRequestsGuidanceResource)).Returns(true);
		GuidanceGetTool tool = new(featureToggleService);

		// Act
		GuidanceGetResponse listing = await tool.GetGuidance(new GuidanceGetArgs("not-a-guide"));
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("when-to-use-requests"));

		// Assert
		listing.AvailableGuides.Should().Contain("when-to-use-requests",
			because: "the guide must be advertised when the requests-registry gate is enabled");
		result.Success.Should().BeTrue(
			because: "the gated guide must resolve when the requests-registry gate is enabled");
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
	[Description("Treats when-to-use-requests as an unknown guide while the requests-registry feature gate is disabled: it is neither advertised nor resolvable, exactly like process-modeling under a disabled process-designer.")]
	public async Task GuidanceGet_ShouldHideWhenToUseRequests_WhenFeatureDisabled() {
		// Arrange — a bare substitute: IsEnabled(...) returns false for everything, so the gated guide is hidden.
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		GuidanceGetTool tool = new(featureToggleService);

		// Act
		GuidanceGetResponse listing = await tool.GetGuidance(new GuidanceGetArgs("not-a-guide"));
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("when-to-use-requests"));

		// Assert
		listing.AvailableGuides.Should().NotContain("when-to-use-requests",
			because: "the gated guide must not be advertised while the requests-registry gate is disabled");
		result.Success.Should().BeFalse(
			because: "a disabled gated guide must resolve as an unknown name");
		result.Article.Should().BeNull(
			because: "an unknown guidance name returns no article");
	}

	[Test]
	[Description("get-guidance name=routing omits the request-wiring rows while the requests-registry feature is gated off: the map must not advertise the hidden get-request-info / when-to-use-requests surface (mirrors the omitted process-modeling row).")]
	public async Task GuidanceGet_ShouldNotRouteRequestWiring_WhileFeatureGated() {
		// Arrange — a bare substitute: IsEnabled(...) returns false, so the routing builder omits the request rows.
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		GuidanceGetTool tool = new(featureToggleService);

		// Act
		GuidanceGetResponse routing = await tool.GetGuidance(new GuidanceGetArgs("routing"));

		// Assert
		routing.Success.Should().BeTrue(because: "the routing map is a core, always-available guide");
		routing.Article!.Text.Should().NotContain("get-request-info",
			because: "the routing map must not advertise the gated request catalog while requests-registry is off");
		routing.Article.Text.Should().NotContain("when-to-use-requests",
			because: "the routing map must not advertise the gated request-wiring guide while requests-registry is off");
	}

	[Test]
	[Description("get-guidance name=routing includes the request-wiring rows once the requests-registry feature is enabled, so the feature-aware routing builder keeps the request catalog discoverable through the mandated get-guidance path.")]
	public async Task GuidanceGet_ShouldRouteRequestWiring_WhenFeatureEnabled() {
		// Arrange — enable the requests-registry gate so the routing builder emits the request rows.
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		featureToggleService.IsEnabled(typeof(WhenToUseRequestsGuidanceResource)).Returns(true);
		GuidanceGetTool tool = new(featureToggleService);

		// Act
		GuidanceGetResponse routing = await tool.GetGuidance(new GuidanceGetArgs("routing"));

		// Assert
		routing.Success.Should().BeTrue(because: "the routing map is a core, always-available guide");
		routing.Article!.Text.Should().Contain("-> get-request-info + name=when-to-use-requests",
			because: "with requests-registry enabled the map must route button/menu action wiring to the request catalog and selection guide");
		routing.Article.Text.Should().Contain("get-request-info (crt.RunBusinessProcessRequest)",
			because: "with requests-registry enabled the run-a-process-button task must route to get-process-signature + the request catalog");
	}

	[Test]
	[Description("get-guidance serves page-modification and mobile-page-modification WITHOUT their request-wiring pointers while requests-registry is gated off — the always-on page guides must not mandate the hidden surface (the GATE row and the mobile catalog pointer hide together with the routing rows).")]
	public async Task GuidanceGet_ShouldOmitRequestWiring_FromPageGuides_WhileFeatureGated() {
		// Arrange — a bare substitute: IsEnabled(...) returns false, so the feature-aware builders omit the pointers.
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		GuidanceGetTool tool = new(featureToggleService);

		// Act
		GuidanceGetResponse web = await tool.GetGuidance(new GuidanceGetArgs("page-modification"));
		GuidanceGetResponse mobile = await tool.GetGuidance(new GuidanceGetArgs("mobile-page-modification"));

		// Assert
		web.Success.Should().BeTrue(because: "page-modification is ungated and always resolves");
		web.Article!.Text.Should().NotContain("when-to-use-requests",
			because: "the GATE table must not mandate the gated guide while requests-registry is off");
		web.Article.Text.Should().NotContain("get-request-info",
			because: "the web page guide must not route to the gated catalog tool while requests-registry is off");
		mobile.Success.Should().BeTrue(because: "mobile-page-modification is ungated and always resolves");
		mobile.Article!.Text.Should().NotContain("get-request-info",
			because: "the mobile guide must not route to the gated catalog tool while requests-registry is off");
	}

	[Test]
	[Description("get-guidance serves page-modification and mobile-page-modification WITH their request-wiring pointers once requests-registry is enabled — the per-entry feature-aware ArticleBuilder restores the GATE row and the mobile catalog pointer.")]
	public async Task GuidanceGet_ShouldIncludeRequestWiring_InPageGuides_WhenFeatureEnabled() {
		// Arrange — enable the requests-registry gate so the feature-aware builders emit the pointers.
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		featureToggleService.IsEnabled(typeof(WhenToUseRequestsGuidanceResource)).Returns(true);
		GuidanceGetTool tool = new(featureToggleService);

		// Act
		GuidanceGetResponse web = await tool.GetGuidance(new GuidanceGetArgs("page-modification"));
		GuidanceGetResponse mobile = await tool.GetGuidance(new GuidanceGetArgs("mobile-page-modification"));

		// Assert
		web.Success.Should().BeTrue(because: "page-modification is ungated and always resolves");
		web.Article!.Text.Should().Contain("| `when-to-use-requests` |",
			because: "with requests-registry enabled the GATE table must route the run-process task to the selection guide");
		mobile.Success.Should().BeTrue(because: "mobile-page-modification is ungated and always resolves");
		mobile.Article!.Text.Should().Contain("get-request-info request-type=crt.RunBusinessProcessRequest",
			because: "with requests-registry enabled the mobile guide must restore the catalog pointer for the run-process contract");
	}
}
