using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentInfoToolTests {
	private const string TestRegistryJson = """
	[
	  {
	    "componentType": "crt.TabContainer",
	    "category": "containers",
	    "description": "Tab body container.",
	    "container": true,
	    "parentTypes": ["crt.TabPanel"],
	    "properties": {
	      "caption": { "type": "string", "description": "Tab caption." },
	      "items": { "type": "array", "description": "Tab children." }
	    },
	    "typicalChildren": ["crt.GridContainer"],
	    "example": {
	      "operation": "insert",
	      "name": "DetailsTab",
	      "values": { "type": "crt.TabContainer", "items": [] },
	      "parentName": "Tabs",
	      "propertyName": "items",
	      "index": 0
	    }
	  },
	  {
	    "componentType": "crt.Input",
	    "category": "fields",
	    "description": "Text input.",
	    "container": false,
	    "parentTypes": ["crt.GridContainer"],
	    "properties": {
	      "control": { "type": "string", "description": "Bound attribute." },
	      "tools": { "type": "array", "description": "Trailing tool slot." }
	    },
	    "typicalChildren": ["crt.Button"],
	    "example": {
	      "operation": "insert",
	      "name": "UsrName",
	      "values": { "type": "crt.Input", "control": "$PDS_Name", "tools": [] },
	      "parentName": "MainGrid",
	      "propertyName": "items",
	      "index": 1
	    }
	  },
	  {
	    "componentType": "crt.Button",
	    "category": "interactive",
	    "description": "Menu button.",
	    "container": false,
	    "parentTypes": ["crt.FlexContainer"],
	    "properties": {
	      "caption": { "type": "string", "description": "Button caption." },
	      "menuItems": { "type": "array", "description": "Nested menu items." }
	    },
	    "typicalChildren": ["crt.MenuItem"],
	    "example": {
	      "operation": "insert",
	      "name": "ActionsButton",
	      "values": { "type": "crt.Button", "caption": "Actions", "menuItems": [] },
	      "parentName": "Header",
	      "propertyName": "items",
	      "index": 0
	    }
	  },
	  {
	    "componentType": "crt.Gallery",
	    "category": "interactive",
	    "description": "Gallery with bulk actions.",
	    "container": false,
	    "parentTypes": ["crt.GridContainer"],
	    "properties": {
	      "bulkActions": { "type": "array", "description": "Bulk selection actions." }
	    },
	    "typicalChildren": ["crt.MenuItem"],
	    "example": {
	      "operation": "insert",
	      "name": "ProductsGallery",
	      "values": { "type": "crt.Gallery", "bulkActions": [] },
	      "parentName": "MainGrid",
	      "propertyName": "items",
	      "index": 2
	    }
	  },
	  {
	    "componentType": "crt.MenuItem",
	    "category": "interactive",
	    "description": "Nested menu action.",
	    "container": true,
	    "parentTypes": ["crt.Button", "crt.MenuItem"],
	    "properties": {
	      "caption": { "type": "string", "description": "Menu caption." },
	      "items": { "type": "array", "description": "Submenu items." },
	      "clicked": { "type": "object", "description": "Request descriptor." }
	    },
	    "typicalChildren": ["crt.MenuItem"],
	    "example": {
	      "operation": "insert",
	      "name": "ExportAction",
	      "values": { "type": "crt.MenuItem", "caption": "Export", "items": [] },
	      "parentName": "ActionsButton",
	      "propertyName": "menuItems",
	      "index": 0
	    }
	  },
	  {
	    "componentType": "crt.Label",
	    "category": "display",
	    "description": "Static label.",
	    "container": false,
	    "parentTypes": ["crt.FlexContainer"],
	    "properties": {
	      "caption": { "type": "string", "description": "Visible text." }
	    },
	    "typicalChildren": [],
	    "example": {
	      "operation": "insert",
	      "name": "TitleLabel",
	      "values": { "type": "crt.Label", "caption": "Title" },
	      "parentName": "Header",
	      "propertyName": "items",
	      "index": 0
	    }
	  }
	]
	""";

	private const string TestMobileRegistryJson = """
	[
	  {
	    "componentType": "crt.Toggle",
	    "category": "fields",
	    "description": "Boolean toggle switch. Mobile-only.",
	    "container": false,
	    "parentTypes": ["crt.GridContainer"],
	    "properties": {
	      "control": { "type": "string", "description": "Bound Boolean attribute." }
	    },
	    "typicalChildren": [],
	    "example": {
	      "operation": "insert",
	      "name": "ActiveToggle",
	      "values": { "type": "crt.Toggle", "control": "$PDS_IsActive" },
	      "parentName": "DetailsGrid",
	      "propertyName": "items",
	      "index": 0
	    }
	  },
	  {
	    "componentType": "crt.BarcodeScanner",
	    "category": "fields",
	    "description": "Barcode scanner field. Mobile-only.",
	    "container": false,
	    "parentTypes": ["crt.GridContainer"],
	    "properties": {
	      "control": { "type": "string", "description": "Bound scanned value attribute." }
	    },
	    "typicalChildren": [],
	    "example": {
	      "operation": "insert",
	      "name": "BarcodeField",
	      "values": { "type": "crt.BarcodeScanner", "control": "$PDS_Barcode" },
	      "parentName": "DetailsGrid",
	      "propertyName": "items",
	      "index": 0
	    }
	  }
	]
	""";

	[Test]
	[Description("Advertises the stable MCP tool name for get-component-info so callers and tests share the same production identifier.")]
	public void ComponentInfoTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = ComponentInfoTool.ToolName;

		// Assert
		toolName.Should().Be("get-component-info",
			because: "the MCP tool name must stay centralized on the production tool type");
	}

	[Test]
	[Description("Serializes get-component-info arguments using kebab-case field names.")]
	public void ComponentInfoArgs_Should_Serialize_Using_Kebab_Case_Field_Names() {
		// Arrange
		ComponentInfoArgs args = new("crt.TabContainer", "tab");

		// Act
		string json = JsonSerializer.Serialize(args);

		// Assert
		json.Should().Contain("\"component-type\":\"crt.TabContainer\"",
			because: "get-component-info should expose the normalized component-type request field");
		json.Should().Contain("\"search\":\"tab\"",
			because: "get-component-info should expose the optional search request field");
		json.Should().NotContain("\"componentType\"",
			because: "get-component-info should not serialize removed camelCase request fields");
	}

	[Test]
	[Description("Returns grouped component summaries when component-type is omitted.")]
	public async Task ComponentInfoTool_Should_Return_Grouped_List_When_Component_Type_Is_Omitted() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo();

		// Assert
		response.Success.Should().BeTrue(
			because: "list mode should succeed when the shipped registry is available");
		response.Mode.Should().Be("list",
			because: "omitting component-type should switch the tool into list mode");
		response.Count.Should().Be(6,
			because: "all registry entries should be returned in list mode");
		response.Items.Should().NotBeNull(
			because: "list mode should return the flat item list");
		response.Items![0].ComponentType.Should().Be("crt.Button",
			because: "items are sorted alphabetically by component type");
		response.ResolvedTargetVersion.Should().NotBeNullOrEmpty(
			because: "the AI must know which catalog version produced the response");
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "until the platform version resolver lands the tool always reports latest-fallback");
	}

	[Test]
	[Description("Returns full component details when component-type matches a curated entry.")]
	public async Task ComponentInfoTool_Should_Return_Detail_When_Component_Type_Matches() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(componentType: "crt.TabContainer");

		// Assert
		response.Success.Should().BeTrue(
			because: "a known component type should resolve to a detail response");
		response.Mode.Should().Be("detail",
			because: "component-type should switch the tool into detail mode");
		response.ComponentType.Should().Be("crt.TabContainer",
			because: "the detail response should echo the resolved component type");
		response.Container.Should().BeTrue(
			because: "the curated registry marks TabContainer as a container");
		response.Properties.Should().ContainKey("caption",
			because: "detail mode should expose the curated property catalog");
		response.TypicalChildren.Should().Contain("crt.GridContainer",
			because: "detail mode should expose common child component hints");
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "detail responses should also expose the resolver tier marker");
	}

	[Test]
	[Description("Filters grouped list results by keyword search across the curated registry.")]
	public async Task ComponentInfoTool_Should_Filter_List_By_Search() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(search: "tab");

		// Assert
		response.Success.Should().BeTrue(
			because: "search-only list mode should still succeed");
		response.Mode.Should().Be("list",
			because: "search without component-type should keep the tool in list mode");
		response.Count.Should().Be(1,
			because: "only matching entries should be returned");
		response.Items.Should().ContainSingle(
			because: "only crt.TabContainer matches the sample registry search")
			.Which.ComponentType.Should().Be("crt.TabContainer");
	}

	[Test]
	[Description("Finds components by frontend-derived property metadata such as bulkActions.")]
	public async Task ComponentInfoTool_Should_Search_Across_Property_Metadata() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(search: "bulkActions");

		// Assert
		response.Success.Should().BeTrue(
			because: "property-name searches should work against the curated registry metadata");
		response.Mode.Should().Be("list",
			because: "search-only queries should stay in list mode");
		response.Count.Should().Be(1,
			because: "only crt.Gallery exposes bulkActions in the sample registry");
		response.Items.Should().ContainSingle()
			.Which.ComponentType.Should().Be("crt.Gallery",
				because: "bulkActions should surface the gallery contract");
	}

	[Test]
	[Description("Returns nested menu component details so action collections can be expanded safely.")]
	public async Task ComponentInfoTool_Should_Return_Detail_For_Nested_Menu_Component() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(componentType: "crt.MenuItem");

		// Assert
		response.Success.Should().BeTrue(
			because: "nested menu contracts are curated component types in the registry");
		response.Mode.Should().Be("detail",
			because: "component-type lookups should return the detail contract");
		response.ComponentType.Should().Be("crt.MenuItem",
			because: "the detail response should echo the requested nested component type");
		response.Container.Should().BeTrue(
			because: "menu items can host submenu items");
		response.Properties.Should().ContainKey("items",
			because: "nested menu components should document their submenu slot");
		response.TypicalChildren.Should().Contain("crt.MenuItem",
			because: "menu items can recursively contain other menu items");
	}

	[Test]
	[Description("Returns a readable error and available types when component-type does not exist.")]
	public async Task ComponentInfoTool_Should_Return_Readable_Error_When_Component_Type_Is_Unknown() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(componentType: "crt.Unknown");

		// Assert
		response.Success.Should().BeFalse(
			because: "unknown component types should not pretend that a detail lookup succeeded");
		response.Error.Should().Contain("crt.Unknown",
			because: "the failure should identify the missing component type");
		response.Items.Should().NotBeNull(
			because: "the tool should still return available types for discovery");
		response.Count.Should().Be(6,
			because: "the fallback list should expose the full catalog when no search filter is applied");
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "the resolver tier marker should still ship with error responses to help diagnosis");
	}

	[Test]
	[Description("When the platform version resolver succeeds the response reports resolvedFrom=environment and the resolved version.")]
	public async Task ComponentInfoTool_Should_Report_Environment_When_Resolver_Succeeds() {
		// Arrange
		ComponentInfoCatalog catalog = new(new InMemoryRegistryClient(TestRegistryJson));
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		ComponentInfoTool tool = new(catalog, mobileCatalog, StubPlatformVersionResolver.Environment("8.1.5"), new FakeDocsClient());

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo();

		// Assert
		response.Success.Should().BeTrue(
			because: "the catalog still loads successfully through the in-memory client stub");
		response.ResolvedTargetVersion.Should().Be("8.1.5",
			because: "the catalog state must echo the version selected by the resolver");
		response.ResolvedFrom.Should().Be("environment",
			because: "a successful cliogate probe must surface as the 'environment' tier on the response");
	}

	[Test]
	[Description("When the registry client falls back to a different version, the response reports latest-fallback even if the resolver succeeded.")]
	public async Task ComponentInfoTool_Should_Report_Latest_Fallback_When_Catalog_Falls_Back() {
		// Arrange — resolver says "8.1.5" (environment probe succeeded) but the client falls
		// back to "latest" because 8.1.5 is not yet published on the CDN.
		FallbackRegistryClient client = new(TestRegistryJson, fallbackVersion: "latest");
		ComponentInfoCatalog catalog = new(client);
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		ComponentInfoTool tool = new(catalog, mobileCatalog, StubPlatformVersionResolver.Environment("8.1.5"), new FakeDocsClient());

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo();

		// Assert
		response.Success.Should().BeTrue(
			because: "the catalog still loads through the fallback chain");
		response.ResolvedTargetVersion.Should().Be("latest",
			because: "the response must echo the version actually loaded by the client, not the requested one");
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "AI must see latest-fallback whenever the loaded catalog does not match the target environment");
	}

	[Test]
	[Description("Successive LoadAsync calls re-parse the underlying bytes so refreshed CDN/cache writes are visible without a process restart.")]
	public async Task ComponentInfoCatalog_Should_Not_Pin_Parsed_State_Across_Calls() {
		// Arrange — two registries differing by a single componentType. Simulates the
		// scenario where the background refresh writes newer bytes to disk between calls.
		const string firstPayload = """[{"componentType":"crt.First","category":"interactive","description":"first","container":false,"properties":{}}]""";
		const string secondPayload = """[{"componentType":"crt.Second","category":"interactive","description":"second","container":false,"properties":{}}]""";
		SequenceRegistryClient client = new(firstPayload, secondPayload);
		ComponentInfoCatalog catalog = new(client);

		// Act
		ComponentRegistryEntry? firstHit = await catalog.FindAsync("latest", "crt.First");
		ComponentRegistryEntry? secondHit = await catalog.FindAsync("latest", "crt.Second");

		// Assert
		firstHit.Should().NotBeNull(
			because: "the first LoadAsync observes the initial payload");
		secondHit.Should().NotBeNull(
			because: "the second LoadAsync must re-parse the newer payload, not return the cached first one");
	}

	[Test]
	[Description("When the underlying client throws ComponentRegistryUnavailableException (cache + CDN both exhausted), the tool catch-all turns it into a graceful MCP error response that points operators at the local-override env var.")]
	public async Task ComponentInfoTool_Should_Surface_Registry_Unavailable_As_Graceful_Error() {
		// Arrange
		ThrowingRegistryClient throwingClient = new(new ComponentRegistryUnavailableException("latest", "https://cdn.test/api/mcp/"));
		ComponentInfoCatalog catalog = new(throwingClient);
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		ComponentInfoTool tool = new(
			catalog,
			mobileCatalog,
			StubPlatformVersionResolver.LatestFallback(),
			new FakeDocsClient());

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo();

		// Assert
		response.Success.Should().BeFalse(
			because: "the registry chain was exhausted — the response must signal failure to AI");
		response.Error.Should().NotBeNull();
		response.Error.Should().Contain("CLIO_COMPONENT_REGISTRY_LOCAL_FILE",
			because: "the error must point operators at the documented offline override");
	}

	private static ComponentInfoTool CreateTool(IComponentRegistryDocsClient? docsClient = null) {
		ComponentInfoCatalog catalog = new(new InMemoryRegistryClient(TestRegistryJson));
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		return new ComponentInfoTool(
			catalog,
			mobileCatalog,
			StubPlatformVersionResolver.LatestFallback(),
			docsClient ?? new FakeDocsClient());
	}

	[Test]
	[Description("Returns grouped mobile component summaries when schema-type is 'mobile'.")]
	public async Task ComponentInfoTool_Should_Return_Mobile_Catalog_When_SchemaType_Is_Mobile() {
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(schemaType: "mobile");

		response.Success.Should().BeTrue(
			because: "listing mobile components should succeed when the mobile registry is available");
		response.Mode.Should().Be("list",
			because: "omitting component-type should return list mode for the mobile catalog");
		response.Count.Should().Be(2,
			because: "the test mobile registry has exactly two entries");
		response.ResolvedTargetVersion.Should().BeNull(
			because: "mobile responses must omit the web-only version marker — mobile catalog has no CDN/version concept");
		response.ResolvedFrom.Should().BeNull(
			because: "mobile responses must omit the resolver tier marker — there is no per-environment probe for mobile");
	}

	[Test]
	[Description("Returns mobile component detail when schema-type is 'mobile' and a known mobile type is requested.")]
	public async Task ComponentInfoTool_Should_Return_Mobile_Detail_When_SchemaType_Is_Mobile_And_Type_Is_Known() {
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(componentType: "crt.Toggle", schemaType: "mobile");

		response.Success.Should().BeTrue(
			because: "crt.Toggle exists in the mobile registry");
		response.Mode.Should().Be("detail",
			because: "specifying a known mobile component type should return detail mode");
		response.ComponentType.Should().Be("crt.Toggle",
			because: "the detail response should echo the requested mobile component type");
	}

	[Test]
	[Description("Returns not-found error when requesting a web-only component from the mobile catalog.")]
	public async Task ComponentInfoTool_Should_Return_Error_When_Web_Only_Type_Is_Requested_From_Mobile_Catalog() {
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(componentType: "crt.Label", schemaType: "mobile");

		response.Success.Should().BeFalse(
			because: "crt.Label is a web component and should not appear in the mobile catalog");
		response.Error.Should().Contain("crt.Label",
			because: "the failure should identify the missing component type");
	}

	[Test]
	[Description("Detail responses concatenate every documentation file listed in content.docs[] into the documentation field with --- separators between them.")]
	public async Task ComponentInfoTool_Should_Inline_Documentation_For_Components_With_Docs() {
		// Arrange — minimal wrapped registry shape with a single component that lists two
		// documentation files. The fake docs client returns one of them and reports the
		// second as missing, exercising the partial-failure-skip path at the same time.
		const string registryJson = """
		{
		  "components": [
		    {
		      "componentType": "crt.WithDocs",
		      "category": "interactive",
		      "description": "Sample with attached documentation.",
		      "container": false,
		      "properties": {},
		      "content": {
		        "docs": [
		          "docs/with-docs.intro.md",
		          "docs/with-docs.missing.md"
		        ]
		      }
		    }
		  ]
		}
		""";
		FakeDocsClient docs = new FakeDocsClient()
			.Seed("latest", "docs/with-docs.intro.md", "# Intro\n\nBody.");
		ComponentInfoTool tool = new(
			new ComponentInfoCatalog(new InMemoryRegistryClient(registryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson),
			StubPlatformVersionResolver.LatestFallback(),
			docs);

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(componentType: "crt.WithDocs");

		// Assert
		response.Success.Should().BeTrue();
		response.Mode.Should().Be("detail");
		response.Documentation.Should().Be("# Intro\n\nBody.",
			because: "the present doc must be returned; the missing one must be silently skipped");
		docs.Requests.Should().Equal(
			("latest", "docs/with-docs.intro.md"),
			("latest", "docs/with-docs.missing.md")
		);
	}

	[Test]
	[Description("Components without a content.docs[] block produce a detail response with documentation omitted entirely (null, JsonIgnore strips it from the wire).")]
	public async Task ComponentInfoTool_Should_Omit_Documentation_When_No_Docs_Are_Listed() {
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(componentType: "crt.TabContainer");

		response.Documentation.Should().BeNull(
			because: "the curated registry entry has no content.docs[] so the docs client must not be called");
	}

	[Test]
	[Description("Defaults to the web catalog when schema-type is omitted.")]
	public async Task ComponentInfoTool_Should_Default_To_Web_Catalog_When_SchemaType_Is_Omitted() {
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(componentType: "crt.TabContainer");

		response.Success.Should().BeTrue(
			because: "crt.TabContainer exists in the web registry — omitting schema-type should use the web catalog");
		response.ComponentType.Should().Be("crt.TabContainer",
			because: "web catalog lookup should still work when schema-type is not specified");
	}

	/// <summary>Test double that serves the same in-memory JSON for every version request.</summary>
	private sealed class InMemoryRegistryClient(string registryJson) : IComponentRegistryClient {
		private readonly byte[] _payload = Encoding.UTF8.GetBytes(registryJson);

		public Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			return Task.FromResult(new ComponentRegistryFetchResult(
				new MemoryStream(_payload, writable: false),
				requestedVersion,
				ComponentRegistrySource.Cdn));
		}

		public Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) {
			return Task.FromResult(false);
		}
	}

	/// <summary>Test double for the mobile catalog: parses a JSON snippet in-memory, no filesystem.</summary>
	private sealed class InMemoryMobileCatalog : IMobileComponentInfoCatalog {
		private readonly ComponentCatalogState _state;

		public InMemoryMobileCatalog(string registryJson) {
			ComponentRegistryEntry[] entries = JsonSerializer.Deserialize<ComponentRegistryEntry[]>(
				registryJson,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
			_state = ComponentInfoCatalog.BuildState(entries, "In-memory mobile test catalog", "mobile", ComponentRegistrySource.Local);
		}

		public IReadOnlyList<ComponentRegistryEntry> GetAll() => _state.Entries;

		public IReadOnlyList<ComponentRegistryEntry> Search(string? search) =>
			ComponentInfoGrouping.FilterEntries(_state.Entries, search);

		public ComponentRegistryEntry? Find(string componentType) =>
			string.IsNullOrWhiteSpace(componentType)
				? null
				: _state.Lookup.TryGetValue(componentType.Trim(), out ComponentRegistryEntry? entry) ? entry : null;
	}

	/// <summary>Test double that returns a pre-configured platform version resolution.</summary>
	private sealed class StubPlatformVersionResolver(PlatformVersionResolution resolution) : IPlatformVersionResolver {
		public static StubPlatformVersionResolver LatestFallback() =>
			new(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback));

		public static StubPlatformVersionResolver Environment(string semver) =>
			new(new PlatformVersionResolution(semver, VersionResolutionSource.Environment));

		public Task<PlatformVersionResolution> ResolveAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(resolution);
	}

	/// <summary>Test double that always reports a different resolved version than was requested.</summary>
	private sealed class FallbackRegistryClient(string registryJson, string fallbackVersion) : IComponentRegistryClient {
		private readonly byte[] _payload = Encoding.UTF8.GetBytes(registryJson);

		public Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			return Task.FromResult(new ComponentRegistryFetchResult(
				new MemoryStream(_payload, writable: false),
				fallbackVersion,
				ComponentRegistrySource.Cdn));
		}

		public Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) {
			return Task.FromResult(false);
		}
	}

	/// <summary>Test double that returns a different payload on each successive GetAsync call.</summary>
	private sealed class SequenceRegistryClient : IComponentRegistryClient {
		private readonly Queue<byte[]> _payloads;

		public SequenceRegistryClient(params string[] payloads) {
			_payloads = new Queue<byte[]>();
			foreach (string payload in payloads) {
				_payloads.Enqueue(Encoding.UTF8.GetBytes(payload));
			}
		}

		public Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			byte[] payload = _payloads.Count > 0 ? _payloads.Dequeue() : throw new InvalidOperationException("No more payloads queued.");
			return Task.FromResult(new ComponentRegistryFetchResult(
				new MemoryStream(payload, writable: false),
				requestedVersion,
				ComponentRegistrySource.Cdn));
		}

		public Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) {
			return Task.FromResult(false);
		}
	}

	/// <summary>
	/// Test double that always throws a pre-built <see cref="ComponentRegistryUnavailableException"/>.
	/// Used to verify that <see cref="ComponentInfoTool"/>'s catch-all converts the exception
	/// into a graceful MCP response.
	/// </summary>
	private sealed class ThrowingRegistryClient(ComponentRegistryUnavailableException exception) : IComponentRegistryClient {
		public Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			throw exception;
		}

		public Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) {
			return Task.FromResult(false);
		}
	}

	/// <summary>
	/// Test double for the docs client. Returns a pre-seeded markdown blob for the
	/// matching (version, path) tuple or <see langword="null"/> otherwise — matching
	/// the contract that the real client uses to signal "skip this doc".
	/// </summary>
	private sealed class FakeDocsClient : IComponentRegistryDocsClient {
		private readonly Dictionary<(string Version, string DocPath), string> _docs = new();
		public List<(string Version, string DocPath)> Requests { get; } = new();

		public FakeDocsClient Seed(string version, string docPath, string content) {
			_docs[(version, docPath)] = content;
			return this;
		}

		public Task<string?> GetDocAsync(string version, string docPath, CancellationToken cancellationToken = default) {
			Requests.Add((version, docPath));
			return Task.FromResult(_docs.TryGetValue((version, docPath), out string? value) ? value : null);
		}
	}
}
