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
	[Description("Returns the canonical validator guidance article when the caller requests page-schema-validators.")]
	public void GuidanceGet_Should_Return_Page_Schema_Validators_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("page-schema-validators"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-validators is a registered guidance name");
		result.Guidance.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Guidance!.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "the guidance tool should preserve the canonical resource URI in the response");
		result.Guidance.Text.Should().Contain("clio MCP page-schema validators guide",
			because: "the guidance tool should return the canonical validator article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves guidance names case-insensitively so prompt-generated uppercase names still work.")]
	public void GuidanceGet_Should_Resolve_Guidance_Name_Case_Insensitively() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("PAGE-SCHEMA-VALIDATORS"));

		// Assert
		result.Success.Should().BeTrue(
			because: "the guidance catalog stores names with an ordinal-ignore-case comparer");
		result.Guidance.Should().NotBeNull(
			because: "a case-insensitive match should still return the canonical guidance article");
		result.Guidance!.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "case-insensitive lookup should still resolve to the canonical validator guide URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an explicit error and the known guide names when the requested guidance name is unknown.")]
	public void GuidanceGet_Should_Return_Known_Guide_Names_For_Unknown_Request() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = tool.GetGuidance(new GuidanceGetArgs("not-a-guide"));

		// Assert
		result.Success.Should().BeFalse(
			because: "unknown guidance names should not resolve silently");
		result.Error.Should().Contain("Unknown guidance 'not-a-guide'",
			because: "the failure should name the rejected guide explicitly");
		result.AvailableGuides.Should().Contain([
				"app-modeling",
				"existing-app-maintenance",
				"page-schema-handlers",
				"page-schema-converters",
				"page-schema-validators"
			],
			because: "the failure response should help the caller recover with one of the registered guidance names");
	}
}
