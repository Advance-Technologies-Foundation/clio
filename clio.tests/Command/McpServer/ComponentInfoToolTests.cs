using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs());

		// Assert
		response.Success.Should().BeTrue(
			because: "list mode should succeed when the shipped registry is available");
		response.Mode.Should().Be("list",
			because: "omitting component-type should switch the tool into list mode");
		response.Count.Should().Be(6,
			because: "all registry entries should be returned in grouped list mode");
		response.Groups.Should().NotBeNull(
			because: "list mode should return grouped component summaries");
		response.Groups![0].Category.Should().Be("containers",
			because: "container entries should appear before other groups");
		response.Groups[0].Items[0].ComponentType.Should().Be("crt.TabContainer",
			because: "group items should preserve the component type");
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
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.TabContainer"));

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
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Search: "tab"));

		// Assert
		response.Success.Should().BeTrue(
			because: "search-only list mode should still succeed");
		response.Mode.Should().Be("list",
			because: "search without component-type should keep the tool in list mode");
		response.Count.Should().Be(1,
			because: "only matching entries should be returned");
		response.Groups.Should().ContainSingle(
			because: "only one category should remain after filtering");
		response.Groups![0].Items.Should().ContainSingle(
			because: "only crt.TabContainer matches the sample registry search");
	}

	[Test]
	[Description("Finds components by frontend-derived property metadata such as bulkActions.")]
	public async Task ComponentInfoTool_Should_Search_Across_Property_Metadata() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Search: "bulkActions"));

		// Assert
		response.Success.Should().BeTrue(
			because: "property-name searches should work against the curated registry metadata");
		response.Mode.Should().Be("list",
			because: "search-only queries should stay in list mode");
		response.Count.Should().Be(1,
			because: "only crt.Gallery exposes bulkActions in the sample registry");
		response.Groups.Should().ContainSingle(
			because: "property metadata search should keep only matching categories");
		response.Groups![0].Items[0].ComponentType.Should().Be("crt.Gallery",
			because: "bulkActions should surface the gallery contract");
	}

	[Test]
	[Description("Returns nested menu component details so action collections can be expanded safely.")]
	public async Task ComponentInfoTool_Should_Return_Detail_For_Nested_Menu_Component() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.MenuItem"));

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
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.Unknown"));

		// Assert
		response.Success.Should().BeFalse(
			because: "unknown component types should not pretend that a detail lookup succeeded");
		response.Error.Should().Contain("crt.Unknown",
			because: "the failure should identify the missing component type");
		response.Groups.Should().NotBeNull(
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
		ComponentInfoTool tool = new(catalog, StubPlatformVersionResolver.Environment("8.1.5"));

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs());

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
		ComponentInfoTool tool = new(catalog, StubPlatformVersionResolver.Environment("8.1.5"));

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs());

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
	[Description("Catalog loaded through the embedded fallback exposes the full curated Freedom UI surface.")]
	public async Task ComponentInfoCatalog_Should_Load_From_Embedded_Resource() {
		// Arrange
		ComponentInfoCatalog catalog = new(new EmbeddedFallbackRegistryClient());

		// Act
		IReadOnlyList<ComponentRegistryEntry> entries = await catalog.GetAllAsync("latest");

		// Assert
		entries.Should().NotBeEmpty(
			because: "the embedded registry resource must ship with clio.dll");
		entries.Count.Should().BeGreaterThan(50,
			because: "the curated catalog contains the full Freedom UI surface, not the test sample");
	}

	[Test]
	[Description("Embedded registry exposes canonical Freedom UI components that AI flows depend on.")]
	public async Task ComponentInfoCatalog_Embedded_Resource_Should_Expose_Canonical_Components() {
		// Arrange
		ComponentInfoCatalog catalog = new(new EmbeddedFallbackRegistryClient());

		// Act / Assert
		(await catalog.FindAsync("latest", "crt.TabContainer")).Should().NotBeNull(
			because: "TabContainer is part of the canonical container surface");
		(await catalog.FindAsync("latest", "crt.Button")).Should().NotBeNull(
			because: "Button is part of the canonical interactive surface");
		(await catalog.FindAsync("latest", "crt.MenuItem")).Should().NotBeNull(
			because: "MenuItem is part of the canonical interactive surface");
	}

	[Test]
	[Description("Both embedded resources required by the CDN-driven loader are present in the assembly.")]
	public void Clio_Assembly_Should_Expose_Component_Registry_Embedded_Resources() {
		// Arrange
		Assembly clioAssembly = typeof(ComponentInfoCatalog).Assembly;

		// Act
		string[] resourceNames = clioAssembly.GetManifestResourceNames();

		// Assert
		resourceNames.Should().Contain("Clio.ComponentRegistry.ComponentRegistry.json",
			because: "ResolveCdnSnapshot MSBuild target must embed the registry JSON resource");
		resourceNames.Should().Contain("Clio.ComponentRegistry.embedded-metadata.json",
			because: "ResolveCdnSnapshot MSBuild target must embed the provenance metadata resource");
	}

	private static ComponentInfoTool CreateTool() {
		ComponentInfoCatalog catalog = new(new InMemoryRegistryClient(TestRegistryJson));
		return new ComponentInfoTool(catalog, StubPlatformVersionResolver.LatestFallback());
	}

	/// <summary>Test double that serves the same in-memory JSON for every version request.</summary>
	private sealed class InMemoryRegistryClient(string registryJson) : IComponentRegistryClient {
		private readonly byte[] _payload = Encoding.UTF8.GetBytes(registryJson);

		public Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			return Task.FromResult(new ComponentRegistryFetchResult(
				new MemoryStream(_payload, writable: false),
				requestedVersion,
				ComponentRegistrySource.Embedded));
		}

		public Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) {
			return Task.FromResult(false);
		}
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

	/// <summary>Test double that exercises the real embedded resource in clio.dll.</summary>
	private sealed class EmbeddedFallbackRegistryClient : IComponentRegistryClient {
		private readonly IEmbeddedRegistryReader _reader = new EmbeddedRegistryReader();

		public Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			return Task.FromResult(new ComponentRegistryFetchResult(
				_reader.OpenRegistryStream(),
				_reader.EmbeddedVersion,
				ComponentRegistrySource.Embedded));
		}

		public Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) {
			return Task.FromResult(false);
		}
	}
}
