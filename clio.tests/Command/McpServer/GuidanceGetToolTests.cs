using System.Linq;
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
	[Description("Returns the canonical data-bindings guidance article when the caller requests data-bindings.")]
	public void GuidanceGet_Should_Return_Data_Bindings_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("data-bindings")).Result;

		// Assert
		result.Success.Should().BeTrue(
			because: "data-bindings is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/data-bindings",
			because: "the guidance tool should preserve the canonical binding resource URI in the response");
		result.Article.Text.Should().Contain("clio MCP data-bindings guide",
			because: "the guidance tool should return the canonical binding article text");
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
				"data-bindings",
				"existing-app-maintenance",
				"page-schema-validators"
			],
			because: "the failure response should help the caller recover with one of the registered guidance names");
	}
}
