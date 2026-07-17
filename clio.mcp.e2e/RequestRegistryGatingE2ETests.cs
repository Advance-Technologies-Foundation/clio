using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the OFF (gated-hidden) state of the request-catalog surface. The fixture starts
/// the real clio MCP server with an isolated <c>CLIO_HOME</c> whose feature set is EMPTY, so the
/// <c>requests-registry</c> gate is off — exactly the fresh-install default. It pins the gate on the surfaces
/// that actually govern use: the resident <c>get-request-info</c> tool is absent from <c>tools/list</c>, the
/// <c>when-to-use-requests</c> guide resolves as unknown, and the feature-aware routing map omits the
/// request-wiring rows. The ENABLED behavior is covered by <see cref="RequestInfoToolE2ETests"/>.
/// </summary>
/// <remarks>
/// Deliberately NOT asserted here: the <c>get-tool-contract</c> discovery index still lists gated-off tools
/// (a known discovery-vs-dispatch leak, tracked separately — the gate governs registration and dispatch, not
/// the ungated schema catalog). These tests therefore assert on the raw <c>tools/list</c>
/// (<see cref="McpServerSession.IsToolAdvertisedAsync"/>) and on <c>get-guidance</c>, both of which honor the
/// gate, rather than on <c>ListReachableToolNamesAsync</c> (which unions the leaking index).
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(RequestInfoTool.ToolName)]
[NonParallelizable]
public sealed class RequestRegistryGatingE2ETests : McpContractFixtureBase {

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		string clioHome = CreateIsolatedClioHome(
			"""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Autoupdate": false,
			  "Features": {},
			  "Environments": {
			    "dev": {
			      "Uri": "http://localhost",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true
			    }
			  }
			}
			""",
			GetType().Name);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
	}

	[Test]
	[Description("get-request-info is a resident core tool gated behind requests-registry: with the feature off it must NOT appear in the real tools/list (the surface that governs native dispatch), even though the discovery index still lists it.")]
	[AllureTag(RequestInfoTool.ToolName)]
	[AllureName("get-request-info is not advertised when requests-registry is disabled")]
	[AllureDescription("Starts the real clio MCP server with an empty feature set and verifies get-request-info is absent from tools/list.")]
	public async Task RequestInfoTool_Should_Not_Be_Advertised_When_RequestsRegistry_Disabled() {
		// Arrange
		await using var context = Arrange();

		// Act
		bool advertised = await context.Session.IsToolAdvertisedAsync(
			RequestInfoTool.ToolName, context.CancellationTokenSource.Token);

		// Assert
		advertised.Should().BeFalse(
			because: "get-request-info is gated behind requests-registry and must not register in tools/list while the feature is off");
	}

	[Test]
	[Description("list-printables is gated under requests-registry: with the feature off the probe is not dispatchable over the real wire — the session routes the call through clio-run (the tool is absent from tools/list) and the feature-filtered invoker registry rejects it as an unregistered tool, pinning the [FeatureToggle] on ListPrintablesTool end-to-end.")]
	[AllureTag(RequestInfoTool.ToolName)]
	[AllureName("list-printables is not dispatchable when requests-registry is disabled")]
	[AllureDescription("Calls list-printables against an empty-feature-set server and verifies the clio-run dispatch rejects it as an unregistered tool.")]
	public async Task ListPrintables_Should_Not_Be_Dispatchable_When_RequestsRegistry_Disabled() {
		// Arrange
		await using var context = Arrange();

		// Act — list-printables is absent from tools/list here, so the session shim dispatches the call
		// through clio-run, where the feature-filtered invoker registry must reject it.
		CallToolResult callResult = await context.Session.CallToolAsync(
			ListPrintablesTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "a gated-off tool must not be runnable through clio-run while requests-registry is off");
		// Parse the serialized content back so escaped characters (e.g. ' for the apostrophe)
		// are decoded before matching the human-readable rejection text.
		using JsonDocument contentDocument = JsonDocument.Parse(JsonSerializer.Serialize(callResult.Content));
		string contentText = string.Concat(contentDocument.RootElement.EnumerateArray()
			.Where(item => item.TryGetProperty("text", out _))
			.Select(item => item.GetProperty("text").GetString()));
		contentText.Should().Contain("unknown tool 'list-printables'",
			because: "the dispatch rejection must name the unregistered tool so the gate failure is explicit");
	}

	[Test]
	[Description("The when-to-use-requests guide is gated behind requests-registry: with the feature off get-guidance treats it as an unknown name and omits it from availableGuides, while an ungated guide (page-schema-handlers) still resolves.")]
	[AllureTag(RequestInfoTool.ToolName)]
	[AllureName("when-to-use-requests guide is unknown when requests-registry is disabled")]
	[AllureDescription("Calls get-guidance for when-to-use-requests against an empty-feature-set server and verifies it resolves as unknown while ungated guides stay available.")]
	public async Task WhenToUseRequestsGuide_Should_Be_Unknown_When_RequestsRegistry_Disabled() {
		// Arrange
		await using var context = Arrange();

		// Act
		GuidanceGetResponse guide = await RequestInfoToolE2ETests.CallGuidanceAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["name"] = "when-to-use-requests" });

		// Assert
		guide.Success.Should().BeFalse(
			because: "the when-to-use-requests guide is gated behind requests-registry and must resolve as unknown while the feature is off");
		guide.Article.Should().BeNull(
			because: "an unknown guidance name returns no article");
		guide.AvailableGuides.Should().NotContain("when-to-use-requests",
			because: "the gated guide must not be advertised in availableGuides while requests-registry is off");
		guide.AvailableGuides.Should().Contain("page-schema-handlers",
			because: "ungated guides must stay advertised while requests-registry is off");
	}

	[Test]
	[Description("The feature-aware routing map omits the request-wiring rows while requests-registry is off, so it never routes an agent to the hidden get-request-info / when-to-use-requests surface.")]
	[AllureTag(RequestInfoTool.ToolName)]
	[AllureName("routing map omits request-wiring rows when requests-registry is disabled")]
	[AllureDescription("Reads get-guidance name=routing against an empty-feature-set server and verifies the request-wiring rows are absent.")]
	public async Task RoutingMap_Should_Omit_RequestWiring_When_RequestsRegistry_Disabled() {
		// Arrange
		await using var context = Arrange();

		// Act
		GuidanceGetResponse routing = await RequestInfoToolE2ETests.CallGuidanceAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["name"] = "routing" });

		// Assert
		routing.Success.Should().BeTrue(
			because: "the routing map is a core, always-available guide");
		routing.Article!.Text.Should().NotContain("get-request-info",
			because: "the feature-aware routing map must not advertise the gated request catalog while requests-registry is off");
		routing.Article.Text.Should().NotContain("when-to-use-requests",
			because: "the feature-aware routing map must not advertise the gated request-wiring guide while requests-registry is off");
	}

	[Test]
	[Description("The always-on page-schema-handlers guide is feature-aware: with requests-registry off get-guidance serves it (it stays available) but its Standard handler parameter catalog omits the gated get-request-info / when-to-use-requests pointers and keeps the ungated get-process-signature route.")]
	[AllureTag(RequestInfoTool.ToolName)]
	[AllureName("page-schema-handlers guide omits request-catalog pointers when requests-registry is disabled")]
	[AllureDescription("Reads get-guidance name=page-schema-handlers against an empty-feature-set server and verifies the gated request-catalog pointers are absent while the ungated resolution path remains.")]
	public async Task PageSchemaHandlersGuide_Should_Omit_RequestCatalogPointers_When_RequestsRegistry_Disabled() {
		// Arrange
		await using var context = Arrange();

		// Act
		GuidanceGetResponse handlers = await RequestInfoToolE2ETests.CallGuidanceAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["name"] = "page-schema-handlers" });

		// Assert
		handlers.Success.Should().BeTrue(
			because: "page-schema-handlers is an ungated, always-available guide");
		handlers.Article!.Text.Should().NotContain("get-request-info",
			because: "the feature-aware handler guide must not mandate the gated request catalog while requests-registry is off");
		handlers.Article.Text.Should().NotContain("when-to-use-requests",
			because: "the feature-aware handler guide must not point at the gated request-wiring guide while requests-registry is off");
		handlers.Article.Text.Should().Contain("get-process-signature",
			because: "the ungated get-process-signature probe remains the run-process resolution path while the catalog is hidden");
	}

	[Test]
	[Description("The four feature-aware guidance resources still RESOLVE over the DIRECT resources/read MCP path with requests-registry off (they are ungated resources, unlike the gated get-guidance NAME), but their request-catalog pointers are omitted - an always-on guide never routes an agent to the hidden get-request-info / when-to-use-requests surface.")]
	[AllureTag(RequestInfoTool.ToolName)]
	[AllureName("feature-aware guides omit request-catalog pointers over resources/read when requests-registry is disabled")]
	[AllureDescription("Reads routing, page-modification, mobile-page-modification and page-schema-handlers over resources/read against an empty-feature-set server and verifies each resolves with its request-catalog pointers omitted.")]
	public async Task FeatureAwareGuidanceResources_Should_Omit_RequestCatalog_Pointers_Over_ResourcesRead_When_Disabled() {
		// Arrange
		await using var context = Arrange();
		CancellationToken token = context.CancellationTokenSource.Token;

		// Act + Assert
		TextResourceContents routing = await RequestInfoToolE2ETests.ReadGuideAsync(
			context.Session, RequestInfoToolE2ETests.RoutingUri, token);
		routing.Text.Should().NotContain("get-request-info",
			because: "the feature-off routing map must not advertise the gated request catalog over resources/read");
		routing.Text.Should().NotContain("when-to-use-requests",
			because: "the feature-off routing map must not advertise the gated request-wiring guide over resources/read");
		routing.Text.Should().NotBeNullOrEmpty(
			because: "the routing resource must still resolve with content over resources/read while the feature is off");

		TextResourceContents page = await RequestInfoToolE2ETests.ReadGuideAsync(
			context.Session, RequestInfoToolE2ETests.PageModificationUri, token);
		page.Text.Should().NotContain("when-to-use-requests",
			because: "the feature-off page-modification guide must not mandate the gated request-wiring guide over resources/read");
		page.Text.Should().NotContain("get-request-info",
			because: "removing the run-process GATE row also drops its get-request-info pointer over resources/read");
		page.Text.Should().Contain("clio MCP page modification guide",
			because: "the page-modification guide still resolves with its stable body when the run-process GATE row is removed (get-process-signature lived only on that removed row, so it is not asserted here)");

		TextResourceContents mobile = await RequestInfoToolE2ETests.ReadGuideAsync(
			context.Session, RequestInfoToolE2ETests.MobilePageUri, token);
		mobile.Text.Should().NotContain("get-request-info",
			because: "the feature-off mobile guide must not point at the gated request catalog over resources/read");
		mobile.Text.Should().Contain("get-process-signature",
			because: "the ungated process-signature probe remains the mobile run-process resolution path when the catalog is hidden");

		TextResourceContents handlers = await RequestInfoToolE2ETests.ReadGuideAsync(
			context.Session, RequestInfoToolE2ETests.PageSchemaHandlersUri, token);
		handlers.Text.Should().NotContain("get-request-info",
			because: "the feature-off handler catalog must not name the gated request catalog over resources/read");
		handlers.Text.Should().NotContain("when-to-use-requests",
			because: "the feature-off handler catalog must not point at the gated request-wiring guide over resources/read");
		handlers.Text.Should().Contain("get-process-signature",
			because: "the ungated process-signature probe remains the run-process resolution path over resources/read");
	}
}
