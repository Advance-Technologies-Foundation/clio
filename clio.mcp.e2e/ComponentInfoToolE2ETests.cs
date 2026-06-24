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
[Category("McpE2E.NoEnvironment")]
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
		fieldResponse.DataSourceBindingContract.Should().Contain("$Resources.Strings.<bindingAttribute>",
			because: "the contract must describe the auto-provided label form — keyed by the view-model attribute name — that the IsAutoProvidedLabelResourceKey rule accepts");
		fieldResponse.DataSourceBindingContract.Should().Contain("operation:\"merge\"",
			because: "the contract must clarify that merge operations are exempt from the new strict rule");
		containerResponse.Success.Should().BeTrue(
			because: "crt.TabContainer is a shipped container component");
		containerResponse.DataSourceBindingContract.Should().BeNull(
			because: "the contract only applies to standard field components and must not surface for containers");
	}

	[Test]
	[Description("Couples the version signals to the resolver tier on the real MCP server: a latest-fallback response (no environment passed) carries the prose superset caveat AND the machine-readable requiresVersionConfirmation flag + resolvedFromReason (ENG-91583).")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info emits versionWarning + requiresVersionConfirmation on latest-fallback")]
	[AllureDescription("Starts the real clio MCP server, requests detail without an environment, and verifies that a latest-fallback response carries the prose caveat, the enforced requiresVersionConfirmation flag, and a resolvedFromReason classification.")]
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
		response.VersionWarning!.Should().Contain("do NOT silently assume",
			because: "ENG-91134: when the version is unknown the agent must not silently assume a default component set");
		response.VersionWarning.Should().Contain("request explicit",
			because: "ENG-91134: the agent must inform the user and request confirmation before proceeding against 'latest'");
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "ENG-91583: latest-fallback must set the machine-readable hard-stop flag so the client can branch on it programmatically, not only by parsing the prose warning");
		response.ResolvedFromReason.Should().NotBeNullOrWhiteSpace(
			because: "ENG-91583: a latest-fallback response must classify why the version is unknown (e.g. no-active-environment) so the agent can decide whether a retry would help");
	}

	[Test]
	[Description("List mode without a search term returns the full component catalog including non-obvious components like crt.Gallery, so the agent discovers them proactively without an explicit user prompt (ENG-91134).")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info list mode surfaces crt.Gallery without an explicit search")]
	[AllureDescription("Starts the real clio MCP server, lists the full web catalog with no search/component-type, and verifies crt.Gallery is present in the known component list.")]
	public async Task ComponentInfoTool_List_Mode_Should_Surface_Gallery_Without_Explicit_Search() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act — no search, no component-type: the full catalog the agent sees on a proactive sweep.
		ComponentInfoResponse listResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?>());

		// Assert
		listResponse.Success.Should().BeTrue(
			because: "list mode must succeed so the agent can index every available component at the start of a session");
		listResponse.Mode.Should().Be("list",
			because: "omitting component-type and search returns the full catalog in list mode");
		listResponse.Items.Should().NotBeNullOrEmpty(
			because: "the full catalog must expose a flat item list to index");
		listResponse.Items!.Select(item => item.ComponentType)
			.Should().Contain("crt.Gallery",
				because: "crt.Gallery is a non-obvious component that must appear in the known component list without the user asking to search for it (ENG-91134)");
	}

	[Test]
	[Description("Detail mode on the real MCP server surfaces the Solution A selection-metadata (whenToUse / whenNotToUse / synonyms) the producer publishes on crt.DataGrid, so the agent receives the 'pick this when…' guidance that steers component choice (ENG-91134 / ENG-91571).")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info surfaces whenToUse selection-metadata on detail")]
	[AllureDescription("Starts the real clio MCP server, requests detail for crt.DataGrid, and verifies the selection-metadata fields the producer publishes (@whenToUse/@whenNotToUse/@synonym) reach the response.")]
	public async Task ComponentInfoTool_Should_Surface_Selection_Metadata_On_Detail() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ComponentInfoResponse response = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["component-type"] = "crt.DataGrid" });

		// Assert
		response.Success.Should().BeTrue(
			because: "crt.DataGrid is a shipped component and detail mode must succeed");
		response.Mode.Should().Be("detail",
			because: "a component-type lookup returns the detail contract");
		response.WhenToUse.Should().NotBeNullOrWhiteSpace(
			because: "the producer publishes @whenToUse on crt.DataGrid and clio must surface the selection guidance to the agent (Solution A, ENG-91571)");
		response.WhenNotToUse.Should().NotBeNullOrWhiteSpace(
			because: "the producer publishes @whenNotToUse on crt.DataGrid to steer the agent toward crt.Gallery/crt.List for the wrong use-cases");
		response.Synonyms.Should().NotBeNullOrEmpty(
			because: "crt.DataGrid publishes @synonym tags that must round-trip so informal terms (e.g. 'table') discover it");
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

	[Test]
	[Description("Binds the composite arg over the wire and routes to a structured mode:composite response — proves the new composite argument and branch are reachable end to end through the real MCP server, independently of whether the live registry ships any composites yet.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info binds the composite arg and returns mode:composite")]
	[AllureDescription("Starts the real clio MCP server and requests a composite by caption; verifies the arg binds and the composite branch returns a structured mode:composite envelope.")]
	public async Task ComponentInfoTool_Should_Accept_Composite_Arg_Over_The_Wire() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act — a caption that cannot exist, so the assertion is deterministic regardless of
		// whether the live registry has been refreshed to a payload that ships composites.
		ComponentInfoResponse response = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["composite"] = "Definitely Not A Composite XYZ" });

		// Assert
		response.Mode.Should().Be("composite",
			because: "the composite arg must bind and route to the composite branch over the wire");
		response.Success.Should().BeFalse(
			because: "an unknown composite caption returns a structured not-found composite envelope");
		response.Error.Should().Contain("Definitely Not A Composite XYZ",
			because: "the not-found error must echo the requested caption");
	}

	[Test]
	[Description("Rejects passing both composite and component-type over the wire — they are mutually exclusive. The error uses mode:list, mirroring the version/environment guard.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info rejects composite + component-type together")]
	[AllureDescription("Starts the real clio MCP server and verifies that supplying both composite and component-type returns a structured mutually-exclusive error.")]
	public async Task ComponentInfoTool_Should_Reject_Composite_And_ComponentType_Together() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ComponentInfoResponse response = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["composite"] = "Expanded list",
				["component-type"] = "crt.TabContainer"
			});

		// Assert
		response.Success.Should().BeFalse(
			because: "composite and component-type select two different things and must not be combined");
		response.Error.Should().Contain("mutually exclusive",
			because: "the caller must be told why the request was rejected");
		response.Mode.Should().Be("list",
			because: "argument-validation errors use mode:list, consistent with the version/environment guard");
	}

	[Test]
	[Description("Happy path over the wire: a caption that actually resolves returns a mode:composite success envelope. Points the real clio process at a local registry fixture (CLIO_COMPONENT_REGISTRY_LOCAL_FILE) that ships a composite, since the live CDN catalog may not ship composites yet.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info returns a resolved composite detail over the wire")]
	[AllureDescription("Starts the real clio MCP server pointed at a local registry fixture containing one composite, requests it by caption, and verifies the mode:composite success shape (caption matches, documentationUnavailable omitted for a no-docs composite).")]
	public async Task ComponentInfoTool_Should_Return_Resolved_Composite_Detail_Over_The_Wire() {
		// Arrange — write a minimal registry fixture that ships a resolvable composite, and point the
		// spawned clio process at it via the Tier-0 local-file override (read before cache/CDN). docs:[]
		// keeps the happy path fully offline and deterministic (no doc fetch).
		string fixturePath = Path.Combine(Path.GetTempPath(), $"clio-e2e-composites-{Guid.NewGuid():N}.json");
		const string registryJson = """
		{
		  "components": [
		    { "componentType": "crt.ExpansionPanel", "category": "containers", "description": "Collapsible panel.", "container": true, "properties": {} }
		  ],
		  "composites": [
		    { "caption": "E2E Composite Probe", "description": "An expansion panel assembled for the e2e wire test.", "docs": [] }
		  ]
		}
		""";
		await File.WriteAllTextAsync(fixturePath, registryJson);
		try {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			// Documented Tier-0 override (see docs/commands/get-component-info.md); read every call,
			// before the disk cache and CDN, so the spawned process serves this composite-bearing catalog.
			settings.ProcessEnvironmentVariables["CLIO_COMPONENT_REGISTRY_LOCAL_FILE"] = fixturePath;
			await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

			// Act
			ComponentInfoResponse response = await CallComponentInfoAsync(
				arrangeContext.Session,
				arrangeContext.CancellationTokenSource.Token,
				new Dictionary<string, object?> { ["composite"] = "E2E Composite Probe" });

			// Assert — the success path the other two composite e2e tests do not cover.
			response.Success.Should().BeTrue(
				because: "the caption resolves in the local registry fixture, so the composite detail succeeds over the wire");
			response.Mode.Should().Be("composite",
				because: "a resolved composite returns the dedicated composite mode");
			response.Caption.Should().Be("E2E Composite Probe",
				because: "the response echoes the matched composite caption");
			response.DocumentationUnavailable.Should().BeNull(
				because: "the composite declares no docs, so documentationUnavailable is omitted rather than signalling a fetch failure");
		}
		finally {
			File.Delete(fixturePath);
		}
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
