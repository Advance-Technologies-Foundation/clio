using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-component-info MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("get-component-info")]
[NonParallelizable]
public sealed class ComponentInfoToolE2ETests {
	private const string ToolName = ComponentInfoTool.ToolName;

	[Test]
	[Description("Advertises get-component-info in the MCP tool list so callers can discover the component catalog.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info tool is advertised by the MCP server")]
	[AllureDescription("Verifies that get-component-info appears in the MCP server tool manifest.")]
	public async Task ComponentInfoTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "get-component-info must be discoverable through the MCP tool manifest");
	}

	[Test]
	[Description("Returns legacy list results and new frontend-derived metadata using the real MCP server process.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info returns legacy and frontend-derived metadata")]
	[AllureDescription("Starts the real clio MCP server, verifies a legacy tab search, verifies property-metadata search for bulkActions, then requests full metadata for crt.MenuItem.")]
	public async Task ComponentInfoTool_Should_Return_List_Search_And_Detail_Metadata() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ComponentInfoResponse tabListResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["search"] = "tab" });
		ComponentInfoResponse propertySearchResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["search"] = "bulkActions" });
		ComponentInfoResponse detailResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["component-type"] ="crt.MenuItem" });

		// Assert
		tabListResponse.Success.Should().BeTrue(
			because: "list mode should succeed with the shipped component registry");
		tabListResponse.Mode.Should().Be("list",
			because: "search-only queries should keep get-component-info in list mode");
		tabListResponse.Count.Should().BeGreaterThan(0,
			because: "the shipped registry should contain tab-related component metadata");
		tabListResponse.Items.Should().NotBeNullOrEmpty(
			because: "list mode should return a flat item list");
		tabListResponse.Items!.Select(item => item.ComponentType)
			.Should().Contain("crt.TabContainer",
				because: "the tab search should surface crt.TabContainer from the shipped registry");
		propertySearchResponse.Success.Should().BeTrue(
			because: "searches by property metadata should work against the shipped registry");
		propertySearchResponse.Mode.Should().Be("list",
			because: "property searches should stay in list mode");
		propertySearchResponse.Items.Should().NotBeNullOrEmpty(
			because: "property metadata matches should still surface in the flat item list");
		propertySearchResponse.Items!.Select(item => item.ComponentType)
			.Should().Contain("crt.Gallery",
				because: "bulkActions should surface gallery metadata derived from the frontend");
		detailResponse.Success.Should().BeTrue(
			because: "detail mode should succeed for frontend-derived nested component contracts");
		detailResponse.Mode.Should().Be("detail",
			because: "component-type lookups should return the detail contract");
		detailResponse.ComponentType.Should().Be("crt.MenuItem",
			because: "the detail response should echo the requested component type");
		// The wrapped (static-files-mcp) registry shape does not emit the `container`
		// flag at all — no component carries it, so it deserialises to null. The legacy
		// top-level-array shape did. Accept either: a true value (legacy) or null
		// (wrapped). This mirrors the items legacy/wrapped tolerance just below.
		detailResponse.Container.Should().NotBe(false,
			because: "container is a legacy-shape flag absent from the wrapped registry payload — accept true (legacy) or null (wrapped), reject only an explicit non-container");
		// crt.MenuItem details may arrive either through the legacy properties block
		// (older catalogs) or through the wrapped-shape inputs/outputs blocks (current
		// static-files-mcp registry). Accept either — both describe the same surface.
		bool hasItemsInLegacy = detailResponse.Properties is not null
			&& detailResponse.Properties.ContainsKey("items");
		bool hasItemsInWrapped = detailResponse.Inputs is not null
			&& detailResponse.Inputs.ContainsKey("items");
		(hasItemsInLegacy || hasItemsInWrapped).Should().BeTrue(
			because: "nested menu contracts should expose their submenu slot metadata in either properties or inputs");

		// CDN migration markers: every response from the real clio MCP server must now
		// expose the resolver tier and the catalog version it landed on, so AI can react
		// to a latest-fallback by adjusting the active environment.
		string[] allowedResolvedFrom = ["environment", "latest-fallback"];
		foreach (ComponentInfoResponse response in new[] { tabListResponse, propertySearchResponse, detailResponse }) {
			response.ResolvedTargetVersion.Should().NotBeNullOrEmpty(
				because: "every response should expose the catalog version actually loaded");
			response.ResolvedFrom.Should().BeOneOf(allowedResolvedFrom,
				because: "resolvedFrom must surface the resolver tier so AI can interpret the catalog correctly");
		}
	}

	[Test]
	[Description("Returns mobile-specific component catalog when schema-type is 'mobile', including crt.Toggle and excluding web-only types like crt.DataGrid.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info returns mobile catalog when schema-type is mobile")]
	[AllureDescription("Starts the real clio MCP server, calls get-component-info with schema-type=mobile, and verifies the response contains mobile-specific components and excludes web-only types.")]
	public async Task ComponentInfoTool_Should_Return_Mobile_Catalog_When_SchemaType_Is_Mobile() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ComponentInfoResponse mobileListResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["schema-type"] = "mobile" });

		// Assert
		mobileListResponse.Success.Should().BeTrue(
			because: "the mobile catalog is shipped with the registry and must be discoverable");
		mobileListResponse.Mode.Should().Be("list",
			because: "schema-type=mobile without a search term should return the full mobile catalog in list mode");
		mobileListResponse.Count.Should().BeGreaterThan(0,
			because: "the shipped mobile registry should contain at least one component entry");
		mobileListResponse.Items.Should().NotBeNullOrEmpty(
			because: "the mobile catalog response should expose a flat item list like the web catalog");
		IEnumerable<string> mobileTypes = mobileListResponse.Items!
			.Select(item => item.ComponentType)
			.ToList();
		mobileTypes.Should().Contain("crt.Toggle",
			because: "crt.Toggle is a mobile-specific Boolean control present in the mobile registry");
		mobileTypes.Should().NotContain("crt.DataGrid",
			because: "crt.DataGrid is a web-only component that must not appear in the mobile catalog");
	}

	[Test]
	[Description("Returns a readable not-found response when get-component-info receives an unknown component type.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info reports unknown component types")]
	[AllureDescription("Starts the real clio MCP server, requests an unknown component type, and verifies that the failure stays structured and readable.")]
	public async Task ComponentInfoTool_Should_Report_Unknown_Component_Types() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ComponentInfoResponse response = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["component-type"] ="crt.DoesNotExist" });

		// Assert
		response.Success.Should().BeFalse(
			because: "unknown component lookups should return a structured failure envelope");
		response.Error.Should().Contain("crt.DoesNotExist",
			because: "the failure should identify the missing component type");
		response.Items.Should().NotBeNullOrEmpty(
			because: "the fallback response should still expose available types for discovery in the flat item list");
		response.Items!.Count.Should().BeLessThan(20,
			because: "an unknown type must return a bounded closest-match shortlist, not the full ~199-item catalog (acceptance #2)");
	}

	[Test]
	[Description("Surfaces the dataSourceBindingContract on detail responses for standard field components so MCP agents see the three-part inserted-field contract next to the component's example payload.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info surfaces dataSourceBindingContract for standard field components")]
	[AllureDescription("Starts the real clio MCP server, queries detail for crt.NumberInput (a standard field component) and crt.TabContainer (a non-field container), and verifies that the field component response carries the dataSourceBindingContract field while the container response does not.")]
	public async Task ComponentInfoTool_Should_Surface_DataSourceBindingContract_For_Standard_Field_Components() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ComponentInfoResponse fieldResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["component-type"] ="crt.NumberInput" });
		ComponentInfoResponse containerResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["component-type"] ="crt.TabContainer" });

		// Assert
		fieldResponse.Success.Should().BeTrue(
			because: "crt.NumberInput is a shipped standard field component");
		fieldResponse.DataSourceBindingContract.Should().NotBeNullOrWhiteSpace(
			because: "every standard field component must advertise the three-part inserted-field contract that update-page enforces");
		fieldResponse.DataSourceBindingContract!.Should().Contain("viewModelConfigDiff",
			because: "the contract must name the section where the binding attribute is declared");
		fieldResponse.DataSourceBindingContract.Should().Contain("$Resources.Strings.<columnCode>",
			because: "the contract must describe the auto-provided label form that the strict IsAutoProvidedLabelResourceKey rule accepts");
		fieldResponse.DataSourceBindingContract.Should().Contain("operation:\"merge\"",
			because: "the contract must clarify that merge operations are exempt from the new strict rule");
		containerResponse.Success.Should().BeTrue(
			because: "crt.TabContainer is a shipped container component");
		containerResponse.DataSourceBindingContract.Should().BeNull(
			because: "the contract only applies to standard field components and must not surface for containers");
	}

	[Test]
	[Description("Couples versionWarning to the resolver tier on the real MCP server: a latest-fallback response (no environment passed) carries the superset caveat, and an environment-matched response omits it.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info emits versionWarning only on latest-fallback")]
	[AllureDescription("Starts the real clio MCP server, requests detail without an environment, and verifies versionWarning is present exactly when resolvedFrom is latest-fallback.")]
	public async Task ComponentInfoTool_Should_Emit_VersionWarning_On_Latest_Fallback() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act — no environment-name / version passed. Version resolution is driven solely by
		// per-call arguments (the ambient singleton was removed), so the server deterministically
		// reports latest-fallback regardless of any environment registered on the CI runner.
		ComponentInfoResponse response = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["component-type"] ="crt.TabContainer" });

		// Assert
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "with no environment-name/version the resolver cannot scope to a real version and must report latest-fallback");
		response.VersionWarning.Should().NotBeNullOrWhiteSpace(
			because: "a latest-fallback catalog is a superset of the target version and the caveat must warn AI the component may not exist there");
	}

	[Test]
	[Description("Rejects passing both version and environment-name on the real MCP server — the two version sources are mutually exclusive.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info rejects version + environment-name together")]
	[AllureDescription("Starts the real clio MCP server and verifies that supplying both version and environment-name returns a structured mutually-exclusive error.")]
	public async Task ComponentInfoTool_Should_Reject_Version_And_Environment_Together() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ComponentInfoResponse response = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["component-type"] ="crt.TabContainer",
				["version"] = "8.3.3",
				["environment-name"] = "any-env"
			});

		// Assert
		response.Success.Should().BeFalse(
			because: "version and environment-name select the target version two different ways and must not be combined");
		response.Error.Should().Contain("mutually exclusive",
			because: "the caller must be told why the request was rejected");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static async Task<ComponentInfoResponse> CallComponentInfoAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		// get-component-info binds a single `args` record (kebab-case fields), like every other
		// clio MCP tool — wrap the per-call fields so the real binding engages instead of dropping
		// them as unknown top-level keys.
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>(arguments) },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-component-info should return structured responses instead of top-level MCP failures");
		return EntitySchemaStructuredResultParser.Extract<ComponentInfoResponse>(callResult);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
