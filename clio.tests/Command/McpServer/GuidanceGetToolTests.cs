using System.Linq;
using System.Reflection;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GuidanceGetToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for get-guidance.")]
	public void GuidanceGet_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GuidanceGetTool)
			.GetMethod(nameof(GuidanceGetTool.GetGuidance))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(GuidanceGetTool.ToolName,
			because: "the MCP guidance tool name must stay stable for prompts that route guide lookups through it");
	}

	[Test]
	[Category("Unit")]
	[Description("Documents page-schema-handlers as a known guidance name on the get-guidance argument contract.")]
	public void GuidanceGet_Should_Document_Page_Schema_Handlers_In_Argument_Descriptions() {
		// Arrange
		ParameterInfo argsParameter = typeof(GuidanceGetTool)
			.GetMethod(nameof(GuidanceGetTool.GetGuidance))!
			.GetParameters()
			.Single();
		PropertyInfo nameProperty = typeof(GuidanceGetArgs).GetProperty(nameof(GuidanceGetArgs.Name))!;

		// Act
		System.ComponentModel.DescriptionAttribute parameterDescription = argsParameter
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();
		System.ComponentModel.DescriptionAttribute propertyDescription = nameProperty
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();

		// Assert
		parameterDescription.Description.Should().Contain("page-schema-handlers",
			because: "the top-level argument hint should mention the dedicated handler guidance name");
		propertyDescription.Description.Should().Contain("page-schema-handlers",
			because: "the serialized name field hint should stay aligned with the known handler guidance name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical handler guidance article when the caller requests page-schema-handlers.")]
	public void GuidanceGet_Should_Return_Page_Schema_Handlers_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("page-schema-handlers"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-handlers is a registered guidance name");
		result.Guidance.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Guidance!.Uri.Should().Be("docs://mcp/guides/page-schema-handlers",
			because: "the guidance tool should preserve the canonical handler guide URI in the response");
		result.Guidance.Text.Should().Contain("clio MCP page-schema handlers guide",
			because: "the guidance tool should return the canonical handler article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical SDK common guidance article when the caller requests page-schema-sdk-common.")]
	public void GuidanceGet_Should_Return_Page_Schema_Sdk_Common_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("page-schema-sdk-common"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-sdk-common is a registered guidance name");
		result.Guidance.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Guidance!.Uri.Should().Be("docs://mcp/guides/page-schema-sdk-common",
			because: "the guidance tool should preserve the canonical SDK common guide URI in the response");
		result.Guidance.Text.Should().Contain("clio MCP page-schema sdk common guide",
			because: "the guidance tool should return the canonical SDK common article text");
		result.Guidance.Text.Should().Contain("Pattern selection order for handler-side data/service work is mandatory",
			because: "the guidance tool should return the updated SDK common routing rules for request sdk and fetch selection");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical validator guidance article when the caller requests page-schema-validators.")]
	public void GuidanceGet_Should_Return_Page_Schema_Validators_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("page-schema-validators")).Result;

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-validators is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "the guidance tool should preserve the canonical resource URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-schema validators guide",
			because: "the guidance tool should return the canonical validator article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves guidance names case-insensitively so prompt-generated uppercase names still work.")]
	public void GuidanceGet_Should_Resolve_Guidance_Name_Case_Insensitively() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("PAGE-SCHEMA-VALIDATORS")).Result;

		// Assert
		result.Success.Should().BeTrue(
			because: "the guidance catalog stores names with an ordinal-ignore-case comparer");
		result.Article.Should().NotBeNull(
			because: "a case-insensitive match should still return the canonical guidance article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "case-insensitive lookup should still resolve to the canonical validator guide URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns structured error with availableGuides when args omit the required name parameter")]
	public void GuidanceGet_Should_Return_Structured_Error_On_Missing_Name() {
		GuidanceGetTool tool = new();

		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs(null)).Result;

		result.Success.Should().BeFalse();
		result.Error.Should().Contain("Missing required parameter 'name'",
			because: "calling without name should surface a clear structured error instead of throwing");
		result.AvailableGuides.Should().NotBeNullOrEmpty(
			because: "missing-name errors should still return the list of valid guides to unblock the caller");
	}

	[Test]
	[Category("Unit")]
	[Description("Legacy alias 'topic' is accepted as 'name' with a hint when ExtensionData carries the value")]
	public void GuidanceGet_Should_Accept_Legacy_Alias_Topic() {
		GuidanceGetTool tool = new();
		var element = System.Text.Json.JsonDocument.Parse("\"page-schema-validators\"").RootElement;
		GuidanceGetArgs args = new(null) {
			ExtensionData = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> {
				["topic"] = element
			}
		};

		GuidanceGetResponse result = tool.GetGuidance(args).Result;

		result.Success.Should().BeTrue(
			because: "legacy 'topic' alias should resolve to 'name' so the caller's first attempt succeeds");
		result.Article!.Name.Should().Be("page-schema-validators");
		result.Hint.Should().Contain("rename to 'name'",
			because: "the hint should teach the caller the canonical field name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an explicit error and the known guide names when the requested guidance name is unknown.")]
	public void GuidanceGet_Should_Return_Known_Guide_Names_For_Unknown_Request() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("not-a-guide")).Result;

		// Assert
		result.Success.Should().BeFalse(
			because: "unknown guidance names should not resolve silently");
		result.Error.Should().Contain("Unknown guidance 'not-a-guide'",
			because: "the failure should name the rejected guide explicitly");
		result.AvailableGuides.Should().Contain([
				"app-modeling",
				"existing-app-maintenance",
				"page-schema-handlers",
				"page-schema-sdk-common",
				"page-schema-validators"
			],
			because: "the failure response should help the caller recover with one of the registered guidance names");
	}
}
