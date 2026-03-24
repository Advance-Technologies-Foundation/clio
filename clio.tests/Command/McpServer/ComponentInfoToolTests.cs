using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class ComponentInfoToolTests {
	private const string RegistryRoot = "/clio";
	private const string RegistryPath = "/clio/Command/McpServer/Data/ComponentRegistry.json";
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
	      "control": { "type": "string", "description": "Bound attribute." }
	    },
	    "typicalChildren": [],
	    "example": {
	      "operation": "insert",
	      "name": "UsrName",
	      "values": { "type": "crt.Input", "control": "$PDS_Name" },
	      "parentName": "MainGrid",
	      "propertyName": "items",
	      "index": 1
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
		response.Count.Should().Be(3,
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
		response.Count.Should().Be(3,
			because: "the fallback list should expose the full catalog when no search filter is applied");
	}

	private static ComponentInfoTool CreateTool() {
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData> {
			[RegistryPath] = new(TestRegistryJson)
		}, "/");
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.ExecutingDirectory.Returns(RegistryRoot);
		ComponentInfoCatalog catalog = new(fileSystem, workingDirectoriesProvider);
		return new ComponentInfoTool(catalog);
	}
}
