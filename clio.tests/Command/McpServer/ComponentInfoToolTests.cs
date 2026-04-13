using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentInfoToolTests {
	private static readonly string RegistryRoot = GetRootedPath("clio");
	private static readonly string RegistryPath = Path.Combine(RegistryRoot, "Command", "McpServer", "Data", "ComponentRegistry.json");
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

	[TearDown]
	public void TearDown() {
		WorkingDirectoriesProvider._executingDirectory = null;
	}

	[Test]
	[Description("Advertises the stable MCP tool name for component-info so callers and tests share the same production identifier.")]
	public void ComponentInfoTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = ComponentInfoTool.ToolName;

		// Assert
		toolName.Should().Be("component-info",
			because: "the MCP tool name must stay centralized on the production tool type");
	}

	[Test]
	[Description("Serializes component-info arguments using kebab-case field names.")]
	public void ComponentInfoArgs_Should_Serialize_Using_Kebab_Case_Field_Names() {
		// Arrange
		ComponentInfoArgs args = new("crt.TabContainer", "tab");

		// Act
		string json = JsonSerializer.Serialize(args);

		// Assert
		json.Should().Contain("\"component-type\":\"crt.TabContainer\"",
			because: "component-info should expose the normalized component-type request field");
		json.Should().Contain("\"search\":\"tab\"",
			because: "component-info should expose the optional search request field");
		json.Should().NotContain("\"componentType\"",
			because: "component-info should not serialize removed camelCase request fields");
	}

	[Test]
	[Description("Returns grouped component summaries when component-type is omitted.")]
	public void ComponentInfoTool_Should_Return_Grouped_List_When_Component_Type_Is_Omitted() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = tool.GetComponentInfo(new ComponentInfoArgs());

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
	}

	[Test]
	[Description("Returns full component details when component-type matches a curated entry.")]
	public void ComponentInfoTool_Should_Return_Detail_When_Component_Type_Matches() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = tool.GetComponentInfo(new ComponentInfoArgs("crt.TabContainer"));

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
	}

	[Test]
	[Description("Filters grouped list results by keyword search across the curated registry.")]
	public void ComponentInfoTool_Should_Filter_List_By_Search() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = tool.GetComponentInfo(new ComponentInfoArgs(Search: "tab"));

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
	public void ComponentInfoTool_Should_Search_Across_Property_Metadata() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = tool.GetComponentInfo(new ComponentInfoArgs(Search: "bulkActions"));

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
	public void ComponentInfoTool_Should_Return_Detail_For_Nested_Menu_Component() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = tool.GetComponentInfo(new ComponentInfoArgs("crt.MenuItem"));

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
	public void ComponentInfoTool_Should_Return_Readable_Error_When_Component_Type_Is_Unknown() {
		// Arrange
		ComponentInfoTool tool = CreateTool();

		// Act
		ComponentInfoResponse response = tool.GetComponentInfo(new ComponentInfoArgs("crt.Unknown"));

		// Assert
		response.Success.Should().BeFalse(
			because: "unknown component types should not pretend that a detail lookup succeeded");
		response.Error.Should().Contain("crt.Unknown",
			because: "the failure should identify the missing component type");
		response.Groups.Should().NotBeNull(
			because: "the tool should still return available types for discovery");
		response.Count.Should().Be(6,
			because: "the fallback list should expose the full catalog when no search filter is applied");
	}

	private static ComponentInfoTool CreateTool() {
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData> {
			[RegistryPath] = new(TestRegistryJson)
		}, RegistryRoot);
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.ExecutingDirectory.Returns(RegistryRoot);
		ComponentInfoCatalog catalog = new(fileSystem, workingDirectoriesProvider);
		return new ComponentInfoTool(catalog);
	}

	private static string GetRootedPath(params string[] segments) {
		string path = OperatingSystem.IsWindows()
			? @"C:\"
			: Path.DirectorySeparatorChar.ToString();
		foreach (string segment in segments) {
			path = Path.Combine(path, segment);
		}
		return path;
	}
}
