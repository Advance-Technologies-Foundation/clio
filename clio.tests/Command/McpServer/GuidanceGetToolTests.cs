using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
	[Description("Documents configuration web-service and page-schema guides as known guidance names on the get-guidance argument contract.")]
	public void GuidanceGet_Should_Document_Known_Guides_In_Argument_Descriptions() {
		// Arrange
		ParameterInfo argsParameter = typeof(GuidanceGetTool)
			.GetMethod(nameof(GuidanceGetTool.GetGuidance))!
			.GetParameters()
			.Single(parameter => parameter.ParameterType == typeof(GuidanceGetArgs));
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
		parameterDescription.Description.Should().Contain("configuration-webservice",
			because: "the top-level argument hint should mention the configuration web-service implementation guidance name");
		parameterDescription.Description.Should().Contain("atf-repository-dev",
			because: "the top-level argument hint should mention generated composable-app skill guidance names");
		parameterDescription.Description.Should().Contain("feature-toggle-tests",
			because: "the top-level argument hint should mention generated composable-app test guidance names");
		parameterDescription.Description.Should().Contain("page-schema-handlers",
			because: "the top-level argument hint should mention the dedicated handler guidance name");
		propertyDescription.Description.Should().Contain("configuration-webservice",
			because: "the serialized name field hint should mention the configuration web-service implementation guidance name");
		propertyDescription.Description.Should().Contain("atf-repository-dev",
			because: "the serialized name field hint should mention generated composable-app skill guidance names");
		propertyDescription.Description.Should().Contain("feature-toggle-tests",
			because: "the serialized name field hint should mention generated composable-app test guidance names");
		propertyDescription.Description.Should().Contain("page-schema-handlers",
			because: "the serialized name field hint should stay aligned with the known handler guidance name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical handler guidance article when the caller requests page-schema-handlers.")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Handlers_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-schema-handlers"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-handlers is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-handlers",
			because: "the guidance tool should preserve the canonical handler guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-schema handlers guide",
			because: "the guidance tool should return the canonical handler article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical SDK common guidance article when the caller requests page-schema-creatio-devkit-common.")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Sdk_Common_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-schema-creatio-devkit-common"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-creatio-devkit-common is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-creatio-devkit-common",
			because: "the guidance tool should preserve the canonical SDK common guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-schema sdk common guide",
			because: "the guidance tool should return the canonical SDK common article text");
		result.Article.Text.Should().Contain("Pattern selection order for handler-side data/service work is mandatory",
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
				"agent-execution",
				"app-modeling",
				"atf-repository-dev",
				"atf-repository-model-management",
				"atf-repository-tests",
				"composable-app-e2e-test-implementation",
				"composable-app-repo-bootstrap",
				"configuration-webservice",
				"configuration-webservice-tests",
				"configuration-entity-event-listener",
				"configuration-entity-event-listener-tests",
				"creatio-composable-app-development",
				"creatio-freedom-iframe-section",
				"data-bindings",
				"existing-app-maintenance",
				"feature-toggle",
				"feature-toggle-tests",
				"page-schema-converters",
				"page-schema-handlers",
				"page-schema-creatio-devkit-common",
				"page-schema-validators",
				"sys-setting",
				"sys-setting-tests",
				"support-mode"
			],
			because: "the failure response should help the caller recover with one of the registered guidance names");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical configuration web-service guidance article when the caller requests configuration-webservice.")]
	public async Task GuidanceGet_Should_Return_Configuration_WebService_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("configuration-webservice"));

		// Assert
		result.Success.Should().BeTrue(
			because: "configuration-webservice is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/configuration-webservice",
			because: "the guidance tool should preserve the canonical configuration web-service guide URI in the response");
		result.Article.Text.Should().Contain("creatio-config-webservice",
			because: "the guidance tool should return the canonical configuration web-service article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical configuration web-service test guidance article when the caller requests configuration-webservice-tests.")]
	public async Task GuidanceGet_Should_Return_Configuration_WebService_Tests_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("configuration-webservice-tests"));

		// Assert
		result.Success.Should().BeTrue(
			because: "configuration-webservice-tests is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/configuration-webservice-tests",
			because: "the guidance tool should preserve the canonical configuration web-service test guide URI in the response");
		result.Article.Text.Should().Contain("configuration-webservice-tests",
			because: "the guidance tool should return the canonical configuration web-service test article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns generated composable-app skill guidance articles by their skill names.")]
	public async Task GuidanceGet_Should_Return_Generated_Composable_App_Skill_Articles() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse atfResult = await tool.GetGuidance(new GuidanceGetArgs("atf-repository-dev"));
		GuidanceGetResponse sysSettingTestsResult = await tool.GetGuidance(new GuidanceGetArgs("sys-setting-tests"));

		// Assert
		atfResult.Success.Should().BeTrue(
			because: "atf-repository-dev is a generated guidance name");
		atfResult.Article.Should().NotBeNull(
			because: "successful generated guidance lookups should return an article");
		atfResult.Article!.Uri.Should().Be("docs://mcp/guides/atf-repository-dev",
			because: "generated guidance lookup should preserve the stable guide URI");
		atfResult.Article.Text.Should().Contain("ATF.Repository",
			because: "the generated guidance article should preserve the source skill content");

		sysSettingTestsResult.Success.Should().BeTrue(
			because: "sys-setting-tests is a generated guidance name");
		sysSettingTestsResult.Article.Should().NotBeNull(
			because: "successful generated test guidance lookups should return an article");
		sysSettingTestsResult.Article!.Uri.Should().Be("docs://mcp/guides/sys-setting-tests",
			because: "generated test guidance lookup should preserve the stable guide URI");
		sysSettingTestsResult.Article.Text.Should().Contain("SetupSysSettings",
			because: "the generated test guidance article should preserve the source skill content");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical converter guidance article when the caller requests page-schema-converters.")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Converters_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("page-schema-converters"));

		// Assert
		result.Success.Should().BeTrue(
			because: "page-schema-converters is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-converters",
			because: "the guidance tool should preserve the canonical converter guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP page-schema converters guide",
			because: "the guidance tool should return the canonical converter article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical agent-execution guidance article when the caller requests agent-execution.")]
	public async Task GuidanceGet_Should_Return_Agent_Execution_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("agent-execution"));

		// Assert
		result.Success.Should().BeTrue(
			because: "agent-execution is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/agent-execution",
			because: "the guidance tool should preserve the canonical agent-execution guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP agent execution guide",
			because: "the guidance tool should return the canonical agent-execution article text");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical support-mode guidance article when the caller requests support-mode.")]
	public async Task GuidanceGet_Should_Return_Support_Mode_Article() {
		// Arrange
		GuidanceGetTool tool = new();

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("support-mode"));

		// Assert
		result.Success.Should().BeTrue(
			because: "support-mode is a registered guidance name");
		result.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/support-mode",
			because: "the guidance tool should preserve the canonical support-mode guide URI in the response");
		result.Article.Text.Should().Contain("clio MCP support-mode guide",
			because: "the guidance tool should return the canonical support-mode article text");
	}
}
