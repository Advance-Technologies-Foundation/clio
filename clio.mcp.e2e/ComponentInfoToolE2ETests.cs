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
		ComponentInfoResponse gallerySeeAlsoResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["component-type"] = "crt.DataGrid" });

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

		// ENG-91574: a collection/visual type's detail response must carry the decision-point
		// crt.Gallery see-also and the stateless discovery tip, end to end through the real server.
		gallerySeeAlsoResponse.Success.Should().BeTrue(
			because: "crt.DataGrid is a real catalog type and should resolve to a detail response");
		gallerySeeAlsoResponse.Mode.Should().Be("detail",
			because: "a component-type lookup should return the detail contract");
		gallerySeeAlsoResponse.RelatedComponents.Should().NotBeNull(
			because: "a collection/visual type must carry the decision-point see-also via the real MCP server");
		gallerySeeAlsoResponse.RelatedComponents!.Select(suggestion => suggestion.ComponentType)
			.Should().Contain("crt.Gallery",
				because: "the reopened ENG-91134 fix surfaces crt.Gallery on crt.DataGrid detail responses end to end");
		gallerySeeAlsoResponse.RelatedComponents!.Single(suggestion => suggestion.ComponentType == "crt.Gallery")
			.Reason.Should().NotBeNullOrWhiteSpace(
				because: "the see-also reason must round-trip through the real MCP JSON binding, not only the in-process helper — closing the JSON-binding gap the reviewer flagged");
		gallerySeeAlsoResponse.DiscoveryTip.Should().NotBeNullOrWhiteSpace(
			because: "every detail response from the real MCP server carries the stateless discovery breadcrumb");

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
		response.VersionWarning!.Should().Contain("do NOT silently assume",
			because: "ENG-91134: when the version is unknown the agent must not silently assume a default component set");
		response.VersionWarning.Should().Contain("request explicit",
			because: "ENG-91134: the agent must inform the user and request confirmation before proceeding against 'latest'");
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
	[Description("Surfaces Solution A selection metadata (synonyms, useCases, whenToUse, category, applicability) on the detail response end-to-end through the real MCP server, fed from a local-override catalog so the assertion does not depend on the producer backfill reaching the live CDN (ENG-91571; the live CDN seed is the A2 follow-up).")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info detail surfaces Solution A selection metadata from a local catalog")]
	[AllureDescription("Starts the real clio MCP server with CLIO_COMPONENT_REGISTRY_LOCAL_FILE pointing at a catalog carrying the new fields, then verifies crt.Gallery returns synonyms/useCases/whenToUse/category and crt.CommunicationOptions returns the entity-coupling constraint.")]
	public async Task ComponentInfoTool_Detail_Should_Surface_Selection_Metadata_From_Local_Catalog() {
		// Arrange — a local-override catalog carrying the Solution A fields, so the assertion is
		// independent of the producer backfill reaching the academy CDN (the A2/sequencing step).
		string catalogPath = Path.Combine(Path.GetTempPath(), $"clio-component-registry-a1-e2e-{Guid.NewGuid():N}.json");
		await File.WriteAllTextAsync(catalogPath, LocalSelectionMetadataCatalogJson);
		try {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables["CLIO_COMPONENT_REGISTRY_LOCAL_FILE"] = catalogPath;
			await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

			// Act
			ComponentInfoResponse gallery = await CallComponentInfoAsync(
				arrangeContext.Session,
				arrangeContext.CancellationTokenSource.Token,
				new Dictionary<string, object?> { ["component-type"] = "crt.Gallery" });
			ComponentInfoResponse communication = await CallComponentInfoAsync(
				arrangeContext.Session,
				arrangeContext.CancellationTokenSource.Token,
				new Dictionary<string, object?> { ["component-type"] = "crt.CommunicationOptions" });

			// Assert — selection metadata round-trips through the real MCP serialization path.
			gallery.Category.Should().Be("media",
				because: "the taxonomy category must reach the agent through the real MCP server for faceted discovery");
			gallery.Synonyms.Should().Contain("photo grid",
				because: "synonyms must round-trip end-to-end so a 'photo grid' prompt can resolve to crt.Gallery");
			gallery.UseCases.Should().NotBeNullOrEmpty(
				because: "use-cases must round-trip end-to-end for Solution B's ranked search and agent reasoning");
			gallery.WhenToUse.Should().NotBeNullOrWhiteSpace(
				because: "the 'when to use' guidance must reach the agent on the detail call it actually makes");

			// Assert — applicability constraint round-trips (ENG-91134 comment 453013).
			communication.AppliesToCustomEntities.Should().BeFalse(
				because: "the entity-coupling constraint must reach the agent so it does not build crt.CommunicationOptions on a custom entity");
			communication.EntityCouplingNote.Should().NotBeNullOrWhiteSpace(
				because: "the coupling reason must round-trip so the agent can relay it to the user");
		}
		finally {
			File.Delete(catalogPath);
		}
	}

	/// <summary>
	/// A minimal local-override registry payload (<c>CLIO_COMPONENT_REGISTRY_LOCAL_FILE</c>) carrying
	/// the Solution A selection-metadata fields. Used so the end-to-end assertion exercises the real
	/// MCP serialization path without waiting for the producer to publish the fields to the CDN.
	/// </summary>
	private const string LocalSelectionMetadataCatalogJson =
		"""
		{
		  "components": [
		    {
		      "componentType": "crt.Gallery",
		      "description": "Gallery list component with selectable cards, pagination events, and bulk menu actions.",
		      "category": "media",
		      "synonyms": ["photo grid", "image gallery", "thumbnails"],
		      "useCases": ["Display a collection of images or photos as browsable preview cards"],
		      "whenToUse": "Use to display a collection of images/photos as browsable preview cards.",
		      "whenNotToUse": "Not for a single image — use crt.ImageInput."
		    },
		    {
		      "componentType": "crt.CommunicationOptions",
		      "description": "Contact communication channels editor.",
		      "category": "communication",
		      "synonyms": ["communication", "contact options"],
		      "useCases": ["Manage a contact's communication channels"],
		      "whenToUse": "Use on Contact/Account pages to manage communication channels.",
		      "whenNotToUse": "Cannot be used on custom entities.",
		      "appliesToCustomEntities": false,
		      "entityCouplingNote": "Bound to the built-in Contact/Account communication model; not available on custom entities."
		    }
		  ],
		  "references": {}
		}
		""";

	[Test]
	[Description("Ranks list-mode results by relevance end-to-end through the real MCP server: a multi-term natural-language query returns the best-fit component first (crt.Gallery ahead of crt.ImageInput), driven by Solution B's scored ranking (ENG-91572). Fed from a local-override catalog so the ranking signal does not depend on the producer backfill reaching the live CDN.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info ranks list results by relevance from a local catalog")]
	[AllureDescription("Starts the real clio MCP server with CLIO_COMPONENT_REGISTRY_LOCAL_FILE pointing at a catalog carrying selection metadata, searches a natural-language phrase, and verifies the best-fit component is ranked first through the real MCP serialization path.")]
	public async Task ComponentInfoTool_List_Should_Rank_By_Relevance_From_Local_Catalog() {
		// Arrange — local-override catalog with selection metadata so the ranking signal is present
		// without waiting for the producer backfill to reach the academy CDN (the A2 step).
		string catalogPath = Path.Combine(Path.GetTempPath(), $"clio-component-registry-b-rank-e2e-{Guid.NewGuid():N}.json");
		await File.WriteAllTextAsync(catalogPath, LocalRankingCatalogJson);
		try {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables["CLIO_COMPONENT_REGISTRY_LOCAL_FILE"] = catalogPath;
			await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

			// Act — a natural-language need that hits crt.Gallery's synonyms/useCases hardest.
			ComponentInfoResponse ranked = await CallComponentInfoAsync(
				arrangeContext.Session,
				arrangeContext.CancellationTokenSource.Token,
				new Dictionary<string, object?> { ["search"] = "image cards collection" });

			// Assert — the ranked order surfaces through the real MCP server; best fit is first.
			ranked.Success.Should().BeTrue(
				because: "a ranked list search must succeed against the local catalog");
			ranked.Mode.Should().Be("list",
				because: "a search without component-type stays in list mode");
			ranked.Items.Should().NotBeNullOrEmpty(
				because: "the query matches at least crt.Gallery in the local catalog");
			List<string> order = ranked.Items!.Select(item => item.ComponentType).ToList();
			order[0].Should().Be("crt.Gallery",
				because: "the scored ranking must put the best-fit image-card component first, end to end through the real server (ENG-91572)");
			if (order.Contains("crt.ImageInput")) {
				order.IndexOf("crt.Gallery").Should().BeLessThan(order.IndexOf("crt.ImageInput"),
					because: "crt.Gallery (a collection of image cards) must outrank crt.ImageInput (a single image) for this need");
			}
		}
		finally {
			File.Delete(catalogPath);
		}
	}

	/// <summary>
	/// A local-override registry payload carrying selection metadata for several components, used to
	/// exercise Solution B's scored ranking end-to-end (best-fit first) without depending on the
	/// producer backfill reaching the CDN.
	/// </summary>
	private const string LocalRankingCatalogJson =
		"""
		{
		  "components": [
		    {
		      "componentType": "crt.Gallery",
		      "description": "Image/card gallery showing a collection of records as preview cards.",
		      "category": "media",
		      "synonyms": ["photo grid", "image gallery", "picture cards"],
		      "useCases": ["Display a collection of images as browsable preview cards"]
		    },
		    {
		      "componentType": "crt.ImageInput",
		      "description": "Single image upload field such as an avatar or logo.",
		      "category": "media",
		      "synonyms": ["single image", "avatar", "image upload"],
		      "useCases": ["Upload or display a single image"]
		    },
		    {
		      "componentType": "crt.DataGrid",
		      "description": "Editable data table of records with columns.",
		      "category": "data",
		      "synonyms": ["data table", "records grid", "spreadsheet"],
		      "useCases": ["Show records in an editable table with columns"]
		    }
		  ],
		  "references": {}
		}
		""";

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
