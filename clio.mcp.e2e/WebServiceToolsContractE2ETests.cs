using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.WebServiceDesigner;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(RegisterWebServiceTool.ToolName)]
[Parallelizable(ParallelScope.Self)]
public sealed class WebServiceToolsContractE2ETests : McpContractFixtureBase {

	[Test]
	[AllureName("web service tools are discoverable on the lazy surface")]
	[Description("The five web service tools are reachable through the get-tool-contract index of the real clio mcp-server.")]
	public async Task WebServiceTools_Should_Be_Discoverable() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		IReadOnlyCollection<string> names =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		names.Should().Contain([
			RegisterWebServiceTool.ToolName, ListWebServicesTool.ToolName, DescribeWebServiceTool.ToolName,
			ModifyWebServiceTool.ToolName, TestWebServiceMethodTool.ToolName
		], because: "non-resident tools are discovered through the compact index");
	}

	[Test]
	[AllureName("register-web-service refuses an unknown argument before touching any environment")]
	[Description("A misspelled argument is refused with the valid field list instead of being dropped at bind time.")]
	public async Task RegisterWebService_Should_Refuse_Unknown_Argument() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult call = await context.Session.CallToolAsync(RegisterWebServiceTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["package-name"] = "Custom",
					["code"] = "UsrErp",
					["base-uri"] = "https://erp.example/",
					["methods"] = Array.Empty<object>(),
					["base-url"] = "https://typo.example/"
				}
			}, context.CancellationTokenSource.Token);
		WebServiceToolResult result = EntitySchemaStructuredResultParser.Extract<WebServiceToolResult>(call);

		// Assert
		result.Success.Should().BeFalse(because: "base-url is not an argument of register-web-service");
		result.Error.Should().Contain("base-url", because: "the refusal names the unknown key");
		result.Error.Should().Contain("base-uri", because: "the refusal lists the valid fields");
	}
}
