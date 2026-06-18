using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
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
	[Description("Binds the get-component-info argument record from kebab-case JSON using the real MCP serializer options, and routes camelCase spellings into the overflow bag. This is the exact JSON->record binding the MCP host performs (the layer the camelCase-vs-kebab bug lived in); direct method calls in the other tests bypass it.")]
	public void ComponentInfoArgs_Should_Bind_KebabCase_And_Route_CamelCase_To_ExtensionData() {
		// Arrange — the very options the MCP host feeds WithToolsFromAssembly for argument binding.
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act — canonical kebab payload (what get-tool-contract advertises and the schema now emits)
		// vs the camelCase payload an LLM trusting the old flat schema would send.
		ComponentInfoArgs kebab = JsonSerializer.Deserialize<ComponentInfoArgs>(
			"""{"component-type":"crt.NumberInput","schema-type":"mobile","environment-name":"local"}""", options)!;
		ComponentInfoArgs camel = JsonSerializer.Deserialize<ComponentInfoArgs>(
			"""{"componentType":"crt.NumberInput"}""", options)!;

		// Assert — kebab binds to the typed parameters; nothing overflows.
		kebab.ComponentType.Should().Be("crt.NumberInput",
			because: "the kebab 'component-type' field must bind so detail mode engages — the bug was this never happening");
		kebab.SchemaType.Should().Be("mobile");
		kebab.EnvironmentName.Should().Be("local");
		(kebab.ExtensionData is null || kebab.ExtensionData.Count == 0).Should().BeTrue(
			because: "every kebab field binds to a declared parameter, so nothing overflows");

		// camelCase does NOT bind to ComponentType; it lands in ExtensionData where GetLegacyAliasError rejects it.
		camel.ComponentType.Should().BeNull(
			because: "componentType is not a declared parameter name, so it must not bind — silently binding it is exactly what produced the 199-item list");
		camel.ExtensionData.Should().ContainKey("componentType",
			because: "the unbound camelCase spelling must surface in the overflow bag so the tool can return a rename hint");
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
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Search: "bulkActions"));

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
		response.Items.Should().NotBeNull(
			because: "the tool should still return closest available types for discovery");
		response.Count.Should().BeLessThanOrEqualTo(6,
			because: "a not-found response returns a bounded closest-match shortlist, never more than the catalog holds");
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "the resolver tier marker should still ship with error responses to help diagnosis");
	}

	[Test]
	[Description("An unknown component-type returns a bounded closest-match shortlist, never the full catalog — even against a registry much larger than the suggestion cap with no search filter applied.")]
	public async Task ComponentInfoTool_Should_Return_Bounded_Suggestions_For_Unknown_Type() {
		// Arrange — a registry far larger than MaxNotFoundSuggestions so the bound is observable
		// (the shared TestRegistryJson has only 6 entries, fewer than the cap, so it cannot prove it).
		const string largeRegistryJson = """
		[
		  {"componentType":"crt.Widget01","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget02","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget03","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget04","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget05","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget06","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget07","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget08","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget09","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget10","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget11","category":"c","description":"d","container":false,"properties":{}},
		  {"componentType":"crt.Widget12","category":"c","description":"d","container":false,"properties":{}}
		]
		""";
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(largeRegistryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.DoesNotExist"));

		// Assert
		response.Success.Should().BeFalse(
			because: "an unknown component type must not pretend a detail lookup succeeded");
		response.Error.Should().Contain("crt.DoesNotExist",
			because: "the failure should identify the missing component type");
		response.Count.Should().BeGreaterThan(0,
			because: "a not-found response should still offer the closest known types for discovery");
		response.Count.Should().BeLessThan(12,
			because: "suggestions must be a bounded shortlist, never the entire 12-entry catalog (acceptance #2)");
		response.Items.Should().NotBeNull();
		response.Items!.Count.Should().Be(response.Count,
			because: "the item list and the reported count must agree");
	}

	[Test]
	[Description("A camelCase parameter spelling captured by the args overflow bag is rejected with a rename hint, instead of silently dropping the value and degrading a detail request into the full catalog — this is the exact failure get-tool-contract advertises as a rejected alias.")]
	public async Task ComponentInfoTool_Should_Reject_CamelCase_Parameter_Alias() {
		// Arrange — simulate the deserializer routing a camelCase field into ExtensionData
		// (the canonical bound parameter is the kebab-case 'component-type').
		ComponentInfoTool tool = CreateTool();
		ComponentInfoArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["componentType"] = JsonSerializer.SerializeToElement("crt.TabContainer")
			}
		};

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "a mis-spelled componentType must not silently fall through to list mode");
		response.Error.Should().Contain("componentType",
			because: "the rename hint must name the offending field");
		response.Error.Should().Contain("component-type",
			because: "the rename hint must point at the canonical kebab-case parameter");
	}

	[Test]
	[Description("When the platform version resolver succeeds the response reports resolvedFrom=environment and the resolved version.")]
	public async Task ComponentInfoTool_Should_Report_Environment_When_Resolver_Succeeds() {
		// Arrange
		ComponentInfoCatalog catalog = new(new InMemoryRegistryClient(TestRegistryJson));
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		ComponentInfoTool tool = BuildTool(catalog, mobileCatalog, environmentVersion: "8.1.5");

		// Act — passing environment-name routes resolution through the cliogate probe stub.
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(EnvironmentName: "test-stand"));

		// Assert
		response.Success.Should().BeTrue(
			because: "the catalog still loads successfully through the in-memory client stub");
		response.ResolvedTargetVersion.Should().Be("8.1.5",
			because: "the catalog state must echo the version selected by the resolver");
		response.ResolvedFrom.Should().Be("environment",
			because: "a successful cliogate probe must surface as the 'environment' tier on the response");
	}

	[Test]
	[Description("When the platform version is KNOWN but its exact catalog is not published, the response reports 'environment-superset' with a soft caveat — the version is not a mystery but the catalog may be wider than the target.")]
	public async Task ComponentInfoTool_Should_Report_EnvironmentSuperset_When_Version_Known_But_Catalog_Falls_Back() {
		// Arrange — resolver says "8.1.5" (environment probe succeeded) but the client falls
		// back to "latest" because 8.1.5 is not yet published on the CDN.
		FallbackRegistryClient client = new(TestRegistryJson, fallbackVersion: "latest");
		ComponentInfoCatalog catalog = new(client);
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		ComponentInfoTool tool = BuildTool(catalog, mobileCatalog, environmentVersion: "8.1.5");

		// Act — probe resolves 8.1.5 but the client only has "latest" published.
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(EnvironmentName: "test-stand"));

		// Assert
		response.Success.Should().BeTrue(
			because: "the catalog still loads through the fallback chain");
		response.ResolvedTargetVersion.Should().Be("latest",
			because: "the response must echo the catalog version actually loaded by the client");
		response.ResolvedFrom.Should().Be("environment-superset",
			because: "the platform version was known but the exact catalog was absent — the 'environment-superset' tier signals a known-version approximation rather than blind-latest");
		response.VersionWarning.Should().Be(ComponentInfoResolution.EnvironmentSupersetWarning,
			because: "'latest' is a superset of older GA versions and the soft caveat must be surfaced so the agent checks critical components against the actual environment");
	}

	[Test]
	[Description("Latest-fallback responses carry the versionWarning so AI does not mistake the 'latest' superset for the target environment's real component set.")]
	public async Task ComponentInfoTool_Should_Emit_Version_Warning_On_Latest_Fallback() {
		// Arrange — resolver falls back to latest (no environment / probe failure).
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs());

		// Assert
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "the fixture resolver reports latest-fallback");
		response.VersionWarning.Should().Be(ComponentInfoResolution.LatestFallbackWarning,
			because: "every latest-fallback response must surface the superset caveat so AI verifies the component exists in the target version");
	}

	[Test]
	[Description("The latest-fallback warning directs the agent NOT to silently assume the component set, but to inform the user the version is unknown and request confirmation before proceeding (ENG-91134).")]
	public void LatestFallbackWarning_Should_Direct_Agent_To_Confirm_Unknown_Version_With_User() {
		string warning = ComponentInfoResolution.LatestFallbackWarning;

		warning.Should().Contain("could not be determined",
			because: "the agent must learn the target platform version is unknown, not silently assume one");
		warning.Should().Contain("do NOT silently assume",
			because: "the AC forbids silently assuming a default component set when the version is unknown");
		warning.Should().Contain("request explicit",
			because: "the agent must request the user's confirmation before proceeding against the 'latest' catalog");
		warning.Should().Contain("superset",
			because: "the original superset caveat must remain so existing renderer/consumer expectations hold");
	}

	[Test]
	[Description("When the catalog matches the resolved environment version the response omits versionWarning — there is no superset risk to flag.")]
	public async Task ComponentInfoTool_Should_Not_Emit_Version_Warning_When_Environment_Matches() {
		// Arrange — resolver says 8.1.5 and the catalog loads exactly that version.
		ComponentInfoCatalog catalog = new(new InMemoryRegistryClient(TestRegistryJson));
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		ComponentInfoTool tool = BuildTool(catalog, mobileCatalog, environmentVersion: "8.1.5");

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(EnvironmentName: "test-stand"));

		// Assert
		response.ResolvedFrom.Should().Be("environment",
			because: "a successful probe whose catalog matches must surface the 'environment' tier");
		response.VersionWarning.Should().BeNull(
			because: "an environment-matched catalog carries no superset risk, so the warning must be omitted");
	}

	[Test]
	[Description("On latest-fallback (version unknown) the response sets the machine-readable requiresVersionConfirmation flag so the client can branch on it instead of parsing the prose warning (ENG-91583 AC#1).")]
	public async Task ComponentInfoTool_Should_Set_RequiresVersionConfirmation_True_On_Latest_Fallback() {
		// Arrange — a bare call (no environment, no version) degrades to latest-fallback.
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs());

		// Assert
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "a version-less, environment-less call cannot determine the platform version");
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "latest-fallback is the hard stop — the machine-readable flag must force the client to confirm the unknown version with the user (AC#1)");
		response.ResolvedFromReason.Should().Be("no-active-environment",
			because: "with nothing to probe the reason must classify the gap as no-active-environment, not a transient probe error (AC#3)");
	}

	[Test]
	[Description("On the environment tier (version known and catalog matched) requiresVersionConfirmation and resolvedFromReason are omitted — there is no confirmation gate (ENG-91583 AC#4).")]
	public async Task ComponentInfoTool_Should_Omit_RequiresVersionConfirmation_When_Version_Known() {
		// Arrange — resolver succeeds and the catalog matches exactly.
		ComponentInfoCatalog catalog = new(new InMemoryRegistryClient(TestRegistryJson));
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		ComponentInfoTool tool = BuildTool(catalog, mobileCatalog, environmentVersion: "8.1.5");

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(EnvironmentName: "test-stand"));

		// Assert
		response.ResolvedFrom.Should().Be("environment",
			because: "the probe resolved the version and the catalog matched it");
		response.RequiresVersionConfirmation.Should().BeNull(
			because: "a known version carries no confirmation gate, so the flag must be omitted from the wire shape (AC#4)");
		response.ResolvedFromReason.Should().BeNull(
			because: "resolvedFromReason is a latest-fallback-only marker and must be absent on the environment tier");
	}

	[Test]
	[Description("On environment-superset (version known, catalog approximate) requiresVersionConfirmation stays omitted — it is a soft caveat, not the hard stop (ENG-91583 AC#4).")]
	public async Task ComponentInfoTool_Should_Omit_RequiresVersionConfirmation_On_Environment_Superset() {
		// Arrange — probe resolves 8.1.5 but the CDN only has "latest" published.
		FallbackRegistryClient client = new(TestRegistryJson, fallbackVersion: "latest");
		ComponentInfoCatalog catalog = new(client);
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		ComponentInfoTool tool = BuildTool(catalog, mobileCatalog, environmentVersion: "8.1.5");

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(EnvironmentName: "test-stand"));

		// Assert
		response.ResolvedFrom.Should().Be("environment-superset",
			because: "the version was known but the exact catalog was absent");
		response.RequiresVersionConfirmation.Should().BeNull(
			because: "environment-superset is a soft caveat — the version is known, so no hard-stop confirmation flag applies (AC#4)");
		response.VersionWarning.Should().Be(ComponentInfoResolution.EnvironmentSupersetWarning,
			because: "the soft caveat is still surfaced as prose so the agent verifies critical types");
	}

	[Test]
	[Description("A transient probe failure degrades to latest-fallback with resolvedFromReason=probe-error so the agent learns a retry / reachable environment may resolve the version (ENG-91583 AC#3).")]
	public async Task ComponentInfoTool_Should_Report_Probe_Error_Reason_On_Transient_Failure() {
		// Arrange — environment supplied, but the resolver reports a transient probe error.
		ComponentInfoCatalog catalog = new(new InMemoryRegistryClient(TestRegistryJson));
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		ComponentInfoTool tool = BuildTool(catalog, mobileCatalog,
			resolver: StubPlatformVersionResolver.LatestFallback(VersionFallbackReason.ProbeError));

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(EnvironmentName: "test-stand"));

		// Assert
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "a probe error still degrades to the latest superset");
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "the hard stop is unchanged regardless of why the version is unknown (AC#4)");
		response.ResolvedFromReason.Should().Be("probe-error",
			because: "a thrown probe is transient — the agent must be able to tell it apart from a genuinely undeterminable version and consider a retry (AC#3)");
	}

	[Test]
	[Description("LABELLED CASE (version unknown → agent must communicate + request confirmation): end-to-end through the tool, a latest-fallback response carries BOTH the machine-readable hard-stop flag AND the prose directive to inform the user and request confirmation (ENG-91583 AC#5).")]
	public async Task ComponentInfoTool_VersionUnknown_Should_Force_Communicate_And_Confirm_Signal() {
		// Arrange — version cannot be determined.
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs());

		// Assert — the enforced (machine-readable) signal …
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "the version-unknown case must raise the enforced confirmation flag the client branches on, not rely on the agent reading prose");
		// … and the prose directive that spells out the required user communication.
		response.VersionWarning.Should().NotBeNull(
			because: "the version-unknown case must also carry the human-readable caveat");
		response.VersionWarning.Should().Contain("request explicit",
			because: "the labelled case asserts the agent is told to request the user's explicit confirmation before proceeding");
		response.VersionWarning.Should().Contain("do NOT silently assume",
			because: "the labelled case asserts the agent is told not to silently assume a component set when the version is unknown");
	}

	[Test]
	[Description("ComponentInfoResolution.RequiresVersionConfirmation is true only on latest-fallback, and GetFallbackReason emits the kebab reason only on that tier (ENG-91583 AC#1/AC#3).")]
	public void ComponentInfoResolution_Should_Gate_Confirmation_And_Reason_On_Latest_Fallback_Only() {
		// Arrange / Act / Assert — confirmation gate.
		ComponentInfoResolution.RequiresVersionConfirmation("latest-fallback").Should().BeTrue(
			because: "latest-fallback is the only tier where the version is unknown");
		ComponentInfoResolution.RequiresVersionConfirmation("environment").Should().BeFalse(
			because: "the environment tier has a known, exact version");
		ComponentInfoResolution.RequiresVersionConfirmation("environment-superset").Should().BeFalse(
			because: "environment-superset still has a known version — only a soft caveat applies");
		ComponentInfoResolution.RequiresVersionConfirmation(null).Should().BeFalse(
			because: "a missing tier (e.g. mobile with no markers) carries no confirmation gate");

		// reason is emitted only on latest-fallback …
		ComponentInfoResolution.GetFallbackReason("latest-fallback", VersionFallbackReason.ProbeError)
			.Should().Be("probe-error", because: "transient probe failures must surface as the probe-error token");
		ComponentInfoResolution.GetFallbackReason("latest-fallback", VersionFallbackReason.NoActiveEnvironment)
			.Should().Be("no-active-environment", because: "a missing environment is a stable, undeterminable reason");
		ComponentInfoResolution.GetFallbackReason("latest-fallback", VersionFallbackReason.CoreVersionUnparseable)
			.Should().Be("core-version-unparseable", because: "an unparseable core version is a stable reason");
		// … and suppressed everywhere else (including None on the fallback tier).
		ComponentInfoResolution.GetFallbackReason("latest-fallback", VersionFallbackReason.None)
			.Should().BeNull(because: "None carries no diagnostic value and must be omitted even on the fallback tier");
		ComponentInfoResolution.GetFallbackReason("environment", VersionFallbackReason.ProbeError)
			.Should().BeNull(because: "resolvedFromReason is a latest-fallback-only marker");
	}

	[Test]
	[Description("GetFallbackReason has an explicit arm for every declared VersionFallbackReason, so adding a new reason without a wire-token mapping fails here (via the guard) instead of silently surfacing as resolvedFromReason: null (ENG-91583 AC#3).")]
	public void GetFallbackReason_Should_Map_Every_Declared_Reason_Without_Throwing() {
		// Arrange / Act / Assert — every value the enum declares must be handled explicitly; the day a new
		// reason is added without a token, this iteration trips the _ => throw guard and fails the build.
		foreach (VersionFallbackReason reason in Enum.GetValues<VersionFallbackReason>()) {
			Action act = () => ComponentInfoResolution.GetFallbackReason("latest-fallback", reason);
			act.Should().NotThrow(
				because: $"every declared VersionFallbackReason needs an explicit GetFallbackReason arm; {reason} fell through to the guard");
		}
	}

	[Test]
	[Description("The pretty renderer prints the versionWarning on a WARNING line for latest-fallback responses, carries the machine-readable markers (requiresVersionConfirmation / resolvedFromReason) for human parity, and omits the warning for environment-matched responses.")]
	public void ComponentInfoPrettyRenderer_Should_Render_Version_Warning_Only_On_Latest_Fallback() {
		// Arrange
		ComponentInfoResponse fallback = new() {
			Success = true, Mode = "list", Count = 0, Items = [],
			ResolvedTargetVersion = "latest", ResolvedFrom = "latest-fallback",
			ResolvedFromReason = "probe-error"
		};
		ComponentInfoResponse matched = new() {
			Success = true, Mode = "list", Count = 0, Items = [],
			ResolvedTargetVersion = "8.1.5", ResolvedFrom = "environment"
		};

		// Act
		string fallbackText = ComponentInfoPrettyRenderer.Render(fallback);
		string matchedText = ComponentInfoPrettyRenderer.Render(matched);

		// Assert
		fallbackText.Should().Contain("WARNING:",
			because: "the pretty surface must flag the latest superset so a human operator sees the same caveat AI gets");
		fallbackText.Should().Contain("superset",
			because: "the rendered warning must carry the LatestFallbackWarning text");
		fallbackText.Should().Contain("requiresVersionConfirmation=true",
			because: "the pretty WARNING line must reach parity with the JSON hard-stop marker, not only the prose caveat");
		fallbackText.Should().Contain("resolvedFromReason=probe-error",
			because: "the transient/stable reason must be visible to a human operator too, not only to JSON consumers");
		matchedText.Should().NotContain("WARNING:",
			because: "an environment-matched catalog has no superset risk to flag");
	}

	[Test]
	[Description("Passing environment-name routes version resolution through the cliogate probe for that environment (factory + settings repository), mirroring the CLI verb, instead of an ambient server-bound resolver.")]
	public async Task ComponentInfoTool_Should_Resolve_Version_From_Passed_Environment() {
		// Arrange
		ComponentInfoCatalog catalog = new(new InMemoryRegistryClient(TestRegistryJson));
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		IPlatformVersionResolverFactory factory = Substitute.For<IPlatformVersionResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>())
			.Returns(StubPlatformVersionResolver.Environment("8.2.1"));
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>())
			.Returns(new EnvironmentSettings { Uri = "http://prod-stand" });
		ComponentInfoTool tool = new(catalog, mobileCatalog, new FakeDocsClient(), factory, settingsRepository);

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(EnvironmentName: "prod-stand"));

		// Assert
		response.ResolvedTargetVersion.Should().Be("8.2.1",
			because: "the version must come from the probe of the environment passed in the call, not from ambient state");
		response.ResolvedFrom.Should().Be("environment",
			because: "an environment-name-driven probe that matches the catalog is the 'environment' tier");
		settingsRepository.Received(1).GetEnvironment(Arg.Is<EnvironmentOptions>(o => o.Environment == "prod-stand"));
		factory.Received(1).Create(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("A malformed explicit version is rejected up front with a readable error instead of silently degrading to latest-fallback when the CDN load fails.")]
	public async Task ComponentInfoTool_Should_Reject_Malformed_Explicit_Version() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Version: "not-a-version"));

		// Assert
		response.Success.Should().BeFalse(
			because: "an unparseable version must surface a clear error rather than a silent latest-fallback");
		response.Error.Should().Contain("not a valid platform version",
			because: "the caller must be told the version value is malformed");
	}

	[Test]
	[Description("Passing both version and environment-name is rejected with a readable error — the two version sources are mutually exclusive, mirroring the CLI verb guard.")]
	public async Task ComponentInfoTool_Should_Reject_Both_Version_And_Environment() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(
			new ComponentInfoArgs(EnvironmentName: "test-stand", Version: "8.3.3"));

		// Assert
		response.Success.Should().BeFalse(
			because: "version and environment-name select the target version two different ways and must not be combined");
		response.Error.Should().Contain("mutually exclusive",
			because: "the caller must be told why the request was rejected");
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
		ComponentInfoTool tool = BuildTool(catalog, mobileCatalog);

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs());

		// Assert
		response.Success.Should().BeFalse(
			because: "the registry chain was exhausted — the response must signal failure to AI");
		response.Error.Should().NotBeNull();
		response.Error.Should().Contain("CLIO_COMPONENT_REGISTRY_LOCAL_FILE",
			because: "the error must point operators at the documented offline override");
	}

	/// <summary>
	/// Wrapped registry with a composite-only component (<c>crt.NextSteps</c>), a normal
	/// component (<c>crt.ExpansionPanel</c>), and a top-level <c>composites</c> array.
	/// Backs the composites/compositeOnly tool-surface tests.
	/// </summary>
	private const string CompositeRegistryJson = """
	{
	  "components": [
	    {
	      "componentType": "crt.ExpansionPanel",
	      "category": "containers",
	      "description": "Collapsible panel.",
	      "container": true,
	      "properties": {}
	    },
	    {
	      "componentType": "crt.NextSteps",
	      "category": "interactive",
	      "description": "Next steps widget.",
	      "compositeOnly": true,
	      "container": false,
	      "properties": {}
	    }
	  ],
	  "composites": [
	    {
	      "caption": "Next steps",
	      "description": "Expansion panel wrapping a crt.NextSteps list.",
	      "docs": ["docs/expansion-panel-next-steps.component.md"]
	    },
	    {
	      "caption": "Expanded list",
	      "description": "Expansion panel pre-filled with a crt.DataGrid and a toolbar.",
	      "docs": ["docs/expansion-panel-expanded-list.component.md"]
	    }
	  ]
	}
	""";

	[Test]
	[Description("List mode surfaces the top-level composites (sorted by caption) alongside components, and flags composite-only components with compositeOnly on their list item.")]
	public async Task ComponentInfoTool_List_Should_Surface_Composites_And_CompositeOnly() {
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(CompositeRegistryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs());

		response.Success.Should().BeTrue(because: "a list request against a well-formed registry succeeds");
		response.Composites.Should().NotBeNull(because: "the registry declares a top-level composites array");
		// Composites are surfaced sorted by caption.
		response.Composites!.Select(c => c.Caption).Should().BeEquivalentTo(
			new[] { "Expanded list", "Next steps" },
			options => options.WithStrictOrdering(),
			because: "composites are emitted sorted by caption, so 'Expanded list' precedes 'Next steps'");
		ComponentInfoListItem nextSteps = response.Items!.Single(i => i.ComponentType == "crt.NextSteps");
		nextSteps.CompositeOnly.Should().BeTrue(because: "crt.NextSteps has no standalone toolbar presence");
		response.Items!.Single(i => i.ComponentType == "crt.ExpansionPanel").CompositeOnly.Should().BeNull(
			because: "a normal standalone component must not carry the flag");
	}

	[Test]
	[Description("composite='<caption>' returns a mode:composite detail with the composite's concatenated assembly docs.")]
	public async Task ComponentInfoTool_Composite_Detail_Should_Return_Docs() {
		FakeDocsClient docs = new FakeDocsClient()
			.Seed("latest", "docs/expansion-panel-next-steps.component.md", "# Next steps\n\nAssemble the panel.");
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(CompositeRegistryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson),
			docs);

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Composite: "Next steps"));

		response.Success.Should().BeTrue(because: "the composite caption resolves to a known composite");
		response.Mode.Should().Be("composite", because: "a composite lookup returns the dedicated composite mode");
		response.Caption.Should().Be("Next steps", because: "the response echoes the matched composite caption");
		response.Description.Should().Be("Expansion panel wrapping a crt.NextSteps list.",
			because: "the composite's registry description is surfaced verbatim");
		response.Documentation.Should().Be("# Next steps\n\nAssemble the panel.",
			because: "the composite's declared docs are fetched and concatenated into documentation");
	}

	[Test]
	[Description("composite lookup is case-insensitive against the caption.")]
	public async Task ComponentInfoTool_Composite_Detail_Lookup_Should_Be_Case_Insensitive() {
		FakeDocsClient docs = new FakeDocsClient()
			.Seed("latest", "docs/expansion-panel-expanded-list.component.md", "# Expanded list");
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(CompositeRegistryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson),
			docs);

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Composite: "expanded LIST"));

		response.Success.Should().BeTrue(because: "composite lookup is case-insensitive, so 'expanded LIST' matches");
		response.Caption.Should().Be("Expanded list",
			because: "the response echoes the canonical caption, not the caller's casing");
	}

	[Test]
	[Description("An unknown composite caption returns a failure that lists the known captions and points back to list mode.")]
	public async Task ComponentInfoTool_Composite_Detail_Should_Fail_With_Known_Captions_When_Unknown() {
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(CompositeRegistryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Composite: "Does Not Exist"));

		response.Success.Should().BeFalse(because: "an unknown composite caption is a lookup failure");
		response.Mode.Should().Be("composite", because: "the failure stays in composite mode so callers branch on one shape");
		response.Error.Should().Contain("Next steps").And.Contain("Expanded list",
			because: "the not-found error must list the known composite captions");
	}

	[TestCase(true, "composites are a web-only Designer feature",
		TestName = "CompositeNotFound: mobile empty catalog → web-only hint")]
	[TestCase(false, "this catalog declares no composites",
		TestName = "CompositeNotFound: web empty catalog → no-composites message")]
	[Description("CreateCompositeNotFoundResponse emits flavor-correct empty-catalog guidance: the mobile path gets the web-only hint, the web path gets the no-composites message. Pins each branch of the flattened web/mobile/empty selector so a future refactor can't silently swap or drop one.")]
	public void ComponentInfoTool_CompositeNotFound_EmptyCatalog_Message_Matches_Flavor(bool isMobile, string expectedFragment) {
		ComponentInfoResponse response = ComponentInfoTool.CreateCompositeNotFoundResponse(
			composites: [],
			caption: "Anything",
			isMobile: isMobile,
			resolvedTargetVersion: "latest",
			resolvedFrom: "latest-fallback",
			resolvedFromReason: null);

		response.Success.Should().BeFalse(because: "an unknown composite caption is a lookup failure");
		response.Mode.Should().Be("composite", because: "the not-found response stays in composite mode");
		response.Error.Should().Contain(expectedFragment,
			because: $"an empty catalog with isMobile={isMobile} must emit the matching guidance");
	}

	[Test]
	[Description("Detail of a composite-only component surfaces compositeOnly:true plus the actionable hint steering to the composite. Calls CreateDetailResponse directly (like the snapshot test) so the assertion targets the projection, not the version-resolution pipeline.")]
	public void ComponentInfoTool_Detail_Should_Surface_CompositeOnly_And_Hint() {
		ComponentRegistryEntry entry = new() {
			ComponentType = "crt.NextSteps",
			Description = "Next steps widget.",
			CompositeOnly = true
		};

		ComponentInfoResponse response = ComponentInfoTool.CreateDetailResponse(
			entry,
			resolvedTargetVersion: "latest",
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: null);

		response.CompositeOnly.Should().BeTrue(because: "crt.NextSteps is declared compositeOnly in the registry");
		response.CompositeOnlyHint.Should().NotBeNullOrEmpty(
			because: "a composite-only component must tell the agent not to insert it standalone");
		response.CompositeOnlyHint.Should().Contain("composite=",
			because: "the hint must steer the agent to the composite lookup that assembles this component");
	}

	[Test]
	[Description("CreateDetailResponse omits compositeOnly/compositeOnlyHint for a normal standalone component.")]
	public void ComponentInfoTool_Detail_Should_Omit_CompositeOnly_For_Standalone_Component() {
		ComponentRegistryEntry entry = new() {
			ComponentType = "crt.ExpansionPanel",
			Description = "Collapsible panel."
		};

		ComponentInfoResponse response = ComponentInfoTool.CreateDetailResponse(
			entry,
			resolvedTargetVersion: "latest",
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: null);

		response.CompositeOnly.Should().BeNull(because: "a normal standalone component must not carry the compositeOnly flag");
		response.CompositeOnlyHint.Should().BeNull(because: "no hint is emitted when the component is not composite-only");
	}

	[Test]
	[Description("Passing both composite and component-type is rejected as mutually exclusive.")]
	public async Task ComponentInfoTool_Should_Reject_Both_Composite_And_ComponentType() {
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(CompositeRegistryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		ComponentInfoResponse response = await tool.GetComponentInfo(
			new ComponentInfoArgs("crt.NextSteps", Composite: "Next steps"));

		response.Success.Should().BeFalse(because: "composite and component-type cannot be combined");
		response.Error.Should().Contain("mutually exclusive",
			because: "the guard must name the conflict so the caller knows to pass only one");
	}

	[Test]
	[Description("The registry envelope deserialises composites and compositeOnly without leaving any field on an UnmappedExtensions bucket (snapshot-safety for the new producer fields).")]
	public void ComponentRegistry_Should_Map_Composites_And_CompositeOnly_With_No_Unmapped_Fields() {
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(CompositeRegistryJson));
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);

		state.Composites.Should().NotBeNull(because: "the envelope declares a top-level composites array");
		state.Composites!.Select(c => c.Caption).Should().BeEquivalentTo(
			new[] { "Expanded list", "Next steps" },
			options => options.WithStrictOrdering(),
			because: "BuildState orders composites by caption");
		state.Composites!.Single(c => c.Caption == "Next steps").Docs.Should()
			.ContainSingle(because: "the 'Next steps' composite declares exactly one doc")
			.Which.Should().Be("docs/expansion-panel-next-steps.component.md");
		state.Lookup["crt.NextSteps"].CompositeOnly.Should().BeTrue(
			because: "crt.NextSteps carries compositeOnly:true in the registry");
		state.Lookup["crt.ExpansionPanel"].CompositeOnly.Should().BeNull(
			because: "a standalone component must not carry the flag");
		foreach (ComponentRegistryEntry entry in state.Entries) {
			(entry.UnmappedExtensions is null || entry.UnmappedExtensions.Count == 0).Should().BeTrue(
				because: $"entry '{entry.ComponentType}' must not leave fields unmapped");
		}
		foreach (CompositeDefinition composite in state.Composites!) {
			(composite.UnmappedExtensions is null || composite.UnmappedExtensions.Count == 0).Should().BeTrue(
				because: $"composite '{composite.Caption}' must not leave fields unmapped");
		}
	}

	[Test]
	[Description("List-mode search filters composites: a non-matching composite is excluded while matching components are still returned.")]
	public async Task ComponentInfoTool_List_Search_Should_Exclude_NonMatching_Composites() {
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(CompositeRegistryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Search: "next steps"));

		response.Success.Should().BeTrue(because: "a filtered list request still succeeds");
		response.Composites.Should().NotBeNull(because: "at least one composite matches the search term");
		response.Composites!.Select(c => c.Caption).Should().Contain("Next steps")
			.And.NotContain("Expanded list", because: "the search term matches only the 'Next steps' composite");
		response.Items!.Select(i => i.ComponentType).Should().Contain("crt.NextSteps",
			because: "matching components must still surface alongside the filtered composites");
	}

	[Test]
	[Description("A composite that declares docs which all fail to load yields documentationUnavailable:true — a fetch failure must be distinguishable from a genuine no-docs composite.")]
	public async Task ComponentInfoTool_Composite_Detail_Should_Flag_DocumentationUnavailable_When_Docs_Fail() {
		// The FakeDocsClient is left unseeded, so every doc fetch returns null (all-failed).
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(CompositeRegistryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson),
			new FakeDocsClient());

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Composite: "Next steps"));

		response.Success.Should().BeTrue(because: "a docs fetch failure does not fail the composite lookup itself");
		response.Mode.Should().Be("composite", because: "the response stays in composite mode");
		response.Documentation.Should().BeNull(because: "no doc loaded, so there is no concatenated documentation");
		response.DocumentationUnavailable.Should().BeTrue(
			because: "the composite declares docs but none loaded — the agent must see a fetch failure, not a no-docs composite");
	}

	[Test]
	[Description("Two composites with the same caption fail the catalog build loudly, mirroring the duplicate-componentType guard, instead of silently shadowing one via the caption lookup.")]
	public void ComponentRegistry_Should_Reject_Duplicate_Composite_Captions() {
		const string json = """
		{
		  "components": [ { "componentType": "crt.X", "properties": {} } ],
		  "composites": [
		    { "caption": "Dup", "docs": ["docs/a.md"] },
		    { "caption": "Dup", "docs": ["docs/b.md"] }
		  ]
		}
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
		Action act = () => ComponentInfoCatalog.LoadFromStream(stream);
		act.Should().Throw<InvalidOperationException>(
				because: "a duplicate caption would silently shadow one composite, so the build must fail loudly")
			.WithMessage("*duplicate composite captions*Dup*");
	}

	[Test]
	[Description("A composite with a blank caption fails the catalog build loudly — a blank caption has no lookup key, so it must not be silently dropped.")]
	public void ComponentRegistry_Should_Reject_Blank_Composite_Caption() {
		const string json = """
		{
		  "components": [ { "componentType": "crt.X", "properties": {} } ],
		  "composites": [
		    { "caption": "  ", "docs": ["docs/a.md"] }
		  ]
		}
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
		Action act = () => ComponentInfoCatalog.LoadFromStream(stream);
		act.Should().Throw<InvalidOperationException>(
				because: "a blank composite caption has no usable lookup key and must surface as a build failure")
			.WithMessage("*blank caption*");
	}

	[Test]
	[Description("compositeOnly and compositeOnlyHint are reachable through the PUBLIC GetComponentInfo detail path, not only via a direct CreateDetailResponse call.")]
	public async Task ComponentInfoTool_GetComponentInfo_Detail_Should_Surface_CompositeOnly() {
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(CompositeRegistryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.NextSteps"));

		response.Success.Should().BeTrue(because: "a detail lookup of a known component succeeds");
		response.Mode.Should().Be("detail", because: "a component-type lookup returns detail mode");
		response.CompositeOnly.Should().BeTrue(because: "crt.NextSteps is composite-only and the public path must surface it");
		response.CompositeOnlyHint.Should().NotBeNullOrEmpty(because: "the actionable hint must reach the public GetComponentInfo path");
	}

	[Test]
	[Description("A composite that declares NO docs yields Documentation null AND documentationUnavailable omitted — the no-docs case must be distinguishable from the docs-declared-but-failed case (documentationUnavailable:true).")]
	public async Task ComponentInfoTool_Composite_Detail_With_No_Docs_Should_Omit_DocumentationUnavailable() {
		const string json = """
		{
		  "components": [ { "componentType": "crt.X", "properties": {} } ],
		  "composites": [
		    { "caption": "No docs", "description": "A composite that ships no docs." }
		  ]
		}
		""";
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(json)),
			new InMemoryMobileCatalog(TestMobileRegistryJson),
			new FakeDocsClient());

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Composite: "No docs"));

		response.Success.Should().BeTrue(because: "a composite with no docs is still a valid composite");
		response.Mode.Should().Be("composite", because: "the lookup resolved to a known composite");
		response.Documentation.Should().BeNull(because: "the composite declares no docs, so there is nothing to concatenate");
		response.DocumentationUnavailable.Should().BeNull(
			because: "documentationUnavailable flags only a fetch failure on declared docs, not a genuine no-docs composite");
	}

	[Test]
	[Description("A composite that declares multiple docs concatenates every successfully-fetched block with the canonical separator, same as component documentation.")]
	public async Task ComponentInfoTool_Composite_Detail_With_Multiple_Docs_Should_Concatenate() {
		const string json = """
		{
		  "components": [ { "componentType": "crt.X", "properties": {} } ],
		  "composites": [
		    { "caption": "Multi", "description": "Multi-doc composite.", "docs": ["docs/multi.a.md", "docs/multi.b.md"] }
		  ]
		}
		""";
		FakeDocsClient docs = new FakeDocsClient()
			.Seed("latest", "docs/multi.a.md", "# Part A")
			.Seed("latest", "docs/multi.b.md", "# Part B");
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(json)),
			new InMemoryMobileCatalog(TestMobileRegistryJson),
			docs);

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Composite: "Multi"));

		response.Success.Should().BeTrue(because: "both declared docs resolve");
		response.Documentation.Should().NotBeNullOrEmpty(because: "a multi-doc composite surfaces concatenated documentation");
		response.Documentation!.Should().Contain("# Part A").And.Contain("# Part B",
			because: "every successfully-fetched doc block must appear in the concatenation");
		response.Documentation.Should().Contain("\n\n---\n\n",
			because: "multiple doc blocks are joined with the canonical separator, same as component docs");
		response.DocumentationUnavailable.Should().BeNull(
			because: "all declared docs loaded, so no fetch-failure flag is emitted");
	}

	private static ComponentInfoTool CreateTool(IComponentRegistryDocsClient? docsClient = null) {
		ComponentInfoCatalog catalog = new(new InMemoryRegistryClient(TestRegistryJson));
		InMemoryMobileCatalog mobileCatalog = new(TestMobileRegistryJson);
		return BuildTool(catalog, mobileCatalog, docsClient);
	}

	/// <summary>
	/// Builds a <see cref="ComponentInfoTool"/> for tests. Version resolution mirrors the CLI verb:
	/// with <paramref name="environmentVersion"/> null the tool resolves nothing (the caller makes a
	/// bare or version-less call → <c>latest-fallback</c>); pass a semver to wire the stub factory so a
	/// call carrying <c>environment-name</c> resolves to that version (<c>environment</c> tier).
	/// </summary>
	private static ComponentInfoTool BuildTool(
		IComponentInfoCatalog catalog,
		IMobileComponentInfoCatalog mobileCatalog,
		IComponentRegistryDocsClient? docsClient = null,
		string? environmentVersion = null,
		IPlatformVersionResolver? resolver = null) {
		IPlatformVersionResolverFactory factory = Substitute.For<IPlatformVersionResolverFactory>();
		if (resolver is not null) {
			// Explicit resolver override — wins over environmentVersion so a test can inject a
			// specific latest-fallback reason (e.g. probe-error) carried on the resolution.
			factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		} else if (environmentVersion is not null) {
			factory.Create(Arg.Any<EnvironmentSettings>())
				.Returns(StubPlatformVersionResolver.Environment(environmentVersion));
		}
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>())
			.Returns(new EnvironmentSettings { Uri = "http://test-stand" });
		return new ComponentInfoTool(
			catalog,
			mobileCatalog,
			docsClient ?? new FakeDocsClient(),
			factory,
			settingsRepository);
	}

	[Test]
	[Description("Returns grouped mobile component summaries when schema-type is 'mobile'. Mobile responses now carry the same resolvedTargetVersion + resolvedFrom markers as web — both flavors share the same async pipeline and the same wrapped envelope, so AI consumers no longer need to branch on schema-type to discover version metadata.")]
	public async Task ComponentInfoTool_Should_Return_Mobile_Catalog_When_SchemaType_Is_Mobile() {
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(SchemaType: "mobile"));

		response.Success.Should().BeTrue(
			because: "listing mobile components should succeed when the mobile registry is available");
		response.Mode.Should().Be("list",
			because: "omitting component-type should return list mode for the mobile catalog");
		response.Count.Should().Be(2,
			because: "the test mobile registry has exactly two entries");
		response.ResolvedTargetVersion.Should().NotBeNullOrEmpty(
			because: "mobile and web now share the same async pipeline and both carry the resolved catalog version");
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "the stub resolver in the test fixture reports latest-fallback regardless of flavor");
	}

	[Test]
	[Description("Returns mobile component detail when schema-type is 'mobile' and a known mobile type is requested.")]
	public async Task ComponentInfoTool_Should_Return_Mobile_Detail_When_SchemaType_Is_Mobile_And_Type_Is_Known() {
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.Toggle", SchemaType: "mobile"));

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

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.Label", SchemaType: "mobile"));

		response.Success.Should().BeFalse(
			because: "crt.Label is a web component and should not appear in the mobile catalog");
		response.Error.Should().Contain("crt.Label",
			because: "the failure should identify the missing component type");
	}

	[Test]
	[Description("Detail responses concatenate every documentation file listed in references.docs[] into the documentation field with --- separators between them.")]
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
		      "references": {
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
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(registryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson),
			docs);

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.WithDocs"));

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
	[Description("Components without a references.docs[] block produce a detail response with documentation omitted entirely (null, JsonIgnore strips it from the wire).")]
	public async Task ComponentInfoTool_Should_Omit_Documentation_When_No_Docs_Are_Listed() {
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.TabContainer"));

		response.Documentation.Should().BeNull(
			because: "the curated registry entry has no references.docs[] so the docs client must not be called");
	}

	[Test]
	[Description("Defaults to the web catalog when schema-type is omitted.")]
	public async Task ComponentInfoTool_Should_Default_To_Web_Catalog_When_SchemaType_Is_Omitted() {
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.TabContainer"));

		response.Success.Should().BeTrue(
			because: "crt.TabContainer exists in the web registry — omitting schema-type should use the web catalog");
		response.ComponentType.Should().Be("crt.TabContainer",
			because: "web catalog lookup should still work when schema-type is not specified");
	}

	[Test]
	[Description("A wrapped-shape entry without references.docs[] still returns inputs/outputs in the detail response — these blocks are the regular per-component metadata for the new payload, not opt-in extras.")]
	public async Task ComponentInfoTool_Should_Return_Inputs_And_Outputs_For_Wrapped_Registry_Entry() {
		// Arrange — a "simple" wrapped-shape component: no references.docs[], no
		// description, but with inputs and outputs (this is the shape that produced
		// the empty crt.Button bug — inputs/outputs were silently dropped).
		const string registryJson = """
		{
		  "components": [
		    {
		      "componentType": "crt.SimpleButton",
		      "inputs": {
		        "caption": {
		          "type": "string",
		          "default": "",
		          "description": "Component title."
		        },
		        "disabled": {
		          "type": "boolean",
		          "default": false
		        },
		        "color": {
		          "type": "string",
		          "values": ["primary", "accent", "default"]
		        }
		      },
		      "outputs": {
		        "clicked": { "type": "RequestBindingConfig" },
		        "blurred": { "type": "RequestBindingConfig" }
		      }
		    }
		  ]
		}
		""";
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(registryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.SimpleButton"));

		// Assert
		response.Success.Should().BeTrue();
		response.Mode.Should().Be("detail");
		response.Inputs.Should().NotBeNull(
			because: "the wrapped registry inputs block is the regular per-component metadata in the new payload shape — it must round-trip through the response");
		response.Inputs!.Should().ContainKeys("caption", "disabled", "color");
		response.Outputs.Should().NotBeNull(
			because: "outputs must also round-trip through the response for the new payload shape");
		response.Outputs!.Should().ContainKeys("clicked", "blurred");
		response.Documentation.Should().BeNull(
			because: "references.docs[] is the opt-in extra for complex components — its absence must not suppress inputs/outputs");
		response.Properties.Should().BeNull(
			because: "the wrapped registry does not populate the legacy properties block — it must be omitted, not surfaced as an empty object");
	}

	[Test]
	[Description("After the source JsonDocument used for catalog deserialisation is disposed, inputs/outputs JsonElement values remain accessible — System.Text.Json gives each JsonElement property its own root document.")]
	public async Task ComponentInfoTool_Inputs_Should_Survive_Source_Document_Disposal() {
		// Arrange — exercises the lifetime contract that makes JsonElement-typed POCO
		// fields safe to retain past the deserialiser scope. The catalog implementation
		// disposes its JsonDocument inside LoadCatalogStateAsync; without this contract
		// every subsequent access here would throw ObjectDisposedException.
		const string registryJson = """
		{
		  "components": [
		    {
		      "componentType": "crt.Persistent",
		      "inputs": {
		        "caption": { "type": "string", "description": "Title." }
		      }
		    }
		  ]
		}
		""";
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(registryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.Persistent"));
		GC.Collect();
		GC.WaitForPendingFinalizers();

		// Assert
		response.Inputs.Should().NotBeNull();
		JsonElement caption = response.Inputs!["caption"];
		caption.ValueKind.Should().Be(JsonValueKind.Object,
			because: "the JsonElement must still point at a live backing document after the source JsonDocument is disposed and GC has run");
		caption.GetProperty("type").GetString().Should().Be("string");
		caption.GetProperty("description").GetString().Should().Be("Title.");
	}

	[Test]
	[Description("A wrapped-shape entry's references.typeDefinitions block is surfaced verbatim under response.references.typeDefinitions — AI needs it to resolve the type names referenced in inputs/outputs `type` strings.")]
	public async Task ComponentInfoTool_Should_Surface_TypeDefinitions_Under_Content() {
		const string registryJson = """
		{
		  "components": [
		    {
		      "componentType": "crt.WithTypes",
		      "inputs": {
		        "icon": { "type": "string | ButtonIcon | ButtonAnimatedIcon" }
		      },
		      "references": {
		        "typeDefinitions": {
		          "ButtonIcon": {
		            "type": "string",
		            "values": ["close-icon", "edit-icon"]
		          },
		          "ButtonAnimatedIcon": {
		            "fields": {
		              "animationData": { "type": "() => Promise<any>" },
		              "loop":          { "type": "boolean | number" }
		            }
		          }
		        }
		      }
		    }
		  ]
		}
		""";
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(registryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.WithTypes"));

		response.Success.Should().BeTrue();
		response.Mode.Should().Be("detail");
		response.References.Should().NotBeNull(
			because: "the wrapped registry's content block must round-trip when typeDefinitions is present");
		response.References!.TypeDefinitions.Should().NotBeNull();
		response.References.TypeDefinitions!.Should().ContainKeys("ButtonIcon", "ButtonAnimatedIcon");

		JsonElement buttonIcon = response.References.TypeDefinitions["ButtonIcon"];
		buttonIcon.GetProperty("type").GetString().Should().Be("string");
		buttonIcon.GetProperty("values").EnumerateArray().Select(e => e.GetString())
			.Should().BeEquivalentTo(new[] { "close-icon", "edit-icon" });

		JsonElement animatedIcon = response.References.TypeDefinitions["ButtonAnimatedIcon"];
		animatedIcon.GetProperty("fields").GetProperty("loop").GetProperty("type").GetString()
			.Should().Be("boolean | number");
	}

	[Test]
	[Description("Entries without a references.typeDefinitions block omit response.references entirely (JsonIgnore strips it from the wire) — the response stays small for simple components.")]
	public async Task ComponentInfoTool_Should_Omit_Content_When_No_TypeDefinitions() {
		// crt.TabContainer in TestRegistryJson has no content block at all.
		ComponentInfoTool tool = CreateTool();

		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs("crt.TabContainer"));

		response.References.Should().BeNull(
			because: "components without producer-side type definitions must not carry an empty content node");
	}

	[Test]
	[Description("List-mode search matches wrapped-shape entries through inputs/outputs keys when the legacy descriptive fields are empty — without this the search filter would be useless on the new payload shape.")]
	public async Task ComponentInfoTool_Should_Match_Search_Query_Against_Wrapped_Registry_Inputs() {
		// Arrange — three components, two with inputs/outputs, one without. The search
		// query "selection" matches a property *inside* one component's inputs block.
		const string registryJson = """
		{
		  "components": [
		    { "componentType": "crt.NoMatch" },
		    {
		      "componentType": "crt.HitOnInput",
		      "inputs": {
		        "selectionMode": { "type": "string", "values": ["single", "multiple"] }
		      }
		    },
		    {
		      "componentType": "crt.HitOnOutputDesc",
		      "outputs": {
		        "fired": { "type": "RequestBindingConfig", "description": "Triggered on selection change." }
		      }
		    }
		  ]
		}
		""";
		ComponentInfoTool tool = BuildTool(
			new ComponentInfoCatalog(new InMemoryRegistryClient(registryJson)),
			new InMemoryMobileCatalog(TestMobileRegistryJson));

		// Act
		ComponentInfoResponse response = await tool.GetComponentInfo(new ComponentInfoArgs(Search: "selection"));

		// Assert
		response.Success.Should().BeTrue();
		response.Mode.Should().Be("list");
		response.Items.Should().NotBeNull();
		response.Items!.Select(item => item.ComponentType).Should().BeEquivalentTo(
			new[] { "crt.HitOnInput", "crt.HitOnOutputDesc" },
			because: "search must find matches inside the inputs key set AND inside output description fields");
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

		public Task<ComponentCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default) =>
			Task.FromResult(_state);

		public Task<IReadOnlyList<ComponentRegistryEntry>> GetAllAsync(string requestedVersion, CancellationToken cancellationToken = default) =>
			Task.FromResult(_state.Entries);

		public Task<IReadOnlyList<ComponentRegistryEntry>> SearchAsync(string requestedVersion, string? search, CancellationToken cancellationToken = default) =>
			Task.FromResult(ComponentInfoGrouping.FilterEntries(_state.Entries, search));

		public Task<ComponentRegistryEntry?> FindAsync(string requestedVersion, string componentType, CancellationToken cancellationToken = default) =>
			Task.FromResult(string.IsNullOrWhiteSpace(componentType)
				? null
				: _state.Lookup.TryGetValue(componentType.Trim(), out ComponentRegistryEntry? entry) ? entry : null);
	}

	/// <summary>Test double that returns a pre-configured platform version resolution.</summary>
	private sealed class StubPlatformVersionResolver(PlatformVersionResolution resolution) : IPlatformVersionResolver {
		public static StubPlatformVersionResolver LatestFallback() =>
			new(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback));

		public static StubPlatformVersionResolver LatestFallback(VersionFallbackReason reason) =>
			new(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback) { Reason = reason });

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
