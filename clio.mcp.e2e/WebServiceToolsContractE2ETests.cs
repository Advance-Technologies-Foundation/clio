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

	[Test]
	[AllureName("describe-web-service and list-web-services contracts carry methods and filter")]
	[Description("The full contracts of describe-web-service and list-web-services name the methods and filter arguments, and list-web-services refuses an unknown argument with filter among the valid ones, before touching any environment.")]
	public async Task WebServiceTools_Should_Publish_Methods_And_Filter() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult contract = await context.Session.CallToolAsync("get-tool-contract",
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { DescribeWebServiceTool.ToolName, ListWebServicesTool.ToolName }
				}
			}, context.CancellationTokenSource.Token);
		string contractText = string.Join(" ", contract.Content.OfType<TextContentBlock>().Select(block => block.Text));
		CallToolResult call = await context.Session.CallToolAsync(ListWebServicesTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["search"] = "Erp"
				}
			}, context.CancellationTokenSource.Token);
		WebServiceListResult listed = EntitySchemaStructuredResultParser.Extract<WebServiceListResult>(call);

		// Assert
		contractText.Should().Contain("methods-not-shown", because: "describe-web-service says how a large service comes back");
		contractText.Should().Contain("\"filter\"", because: "list-web-services publishes its filter argument");
		listed.Success.Should().BeFalse(because: "search is not an argument of list-web-services");
		listed.Error.Should().Contain("Valid: environment-name, filter.", because: "the refusal lists the valid fields");
	}

	[Test]
	[AllureName("test-web-service-method names the argument an agent guessed")]
	[Description("A test call sent with method-code instead of method is refused with a rename hint naming method, before touching any environment.")]
	public async Task TestWebServiceMethod_Should_Hint_Method_For_Method_Code() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult call = await context.Session.CallToolAsync(TestWebServiceMethodTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-{Guid.NewGuid():N}",
					["code"] = "UsrErp",
					["method-code"] = "UsrGetOrders"
				}
			}, context.CancellationTokenSource.Token);
		WebServiceTestResult result = EntitySchemaStructuredResultParser.Extract<WebServiceTestResult>(call);

		// Assert
		result.Success.Should().BeFalse(because: "method-code is not an argument of test-web-service-method");
		result.Error.Should().Contain("'method-code' -> 'method'",
			because: "the refusal names the argument to use, so the retry is right the first time");
	}
}
