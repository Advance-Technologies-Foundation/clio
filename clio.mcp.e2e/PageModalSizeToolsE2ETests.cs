using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-page-modal-size and set-page-modal-size MCP tools, driven through the real clio MCP
/// server.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(SetPageModalSizeTool.ToolName)]
[NonParallelizable]
public sealed class PageModalSizeToolsE2ETests : McpContractFixtureBase {

	[Test]
	[Description("Exposes both modal size tools through the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(GetPageModalSizeTool.ToolName)]
	[AllureTag(SetPageModalSizeTool.ToolName)]
	[AllureName("modal size MCP tools are discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies get-page-modal-size and set-page-modal-size are reachable through get-tool-contract.")]
	public async Task PageModalSizeTools_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain([GetPageModalSizeTool.ToolName, SetPageModalSizeTool.ToolName],
			because: "an agent asked to resize a modal window must be able to find the tools that do it");
	}

	[Test]
	[Description("get-page-modal-size reports an invalid environment inside the structured response rather than an MCP binding error.")]
	[AllureTag(GetPageModalSizeTool.ToolName)]
	[AllureName("get-page-modal-size reports an invalid environment")]
	[AllureDescription("Calls get-page-modal-size with an intentionally missing environment and verifies the structured response reports it.")]
	public async Task GetPageModalSize_ShouldReportInvalidEnvironment_WhenEnvironmentMissing() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-modal-size-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetPageModalSizeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["page-schema-name"] = "UsrOrder_MiniPage"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetPageModalSizeResponse response =
			EntitySchemaStructuredResultParser.Extract<GetPageModalSizeResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a resolvable payload should stay inside the structured tool response, not surface as an MCP binding error");
		response.Success.Should().BeFalse(because: "the intentionally missing environment fails before the add-on read");
		response.Error.Should().MatchRegex(
			$"(?is)({System.Text.RegularExpressions.Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should come from resolving the requested environment");
	}

	[Test]
	[Description("set-page-modal-size binds page, package and size and reports an invalid environment inside the structured response.")]
	[AllureTag(SetPageModalSizeTool.ToolName)]
	[AllureName("set-page-modal-size binds its arguments and reports an invalid environment")]
	[AllureDescription("Calls set-page-modal-size with every required argument and an intentionally missing environment, and verifies the arguments bind and the structured response reports the environment.")]
	public async Task SetPageModalSize_ShouldReportInvalidEnvironment_WhenEnvironmentMissing() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-modal-size-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			SetPageModalSizeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["page-schema-name"] = "UsrOrder_MiniPage",
					["size"] = "large"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		SetPageModalSizeResponse response =
			EntitySchemaStructuredResultParser.Extract<SetPageModalSizeResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the arguments should bind and the failure stay inside the structured tool response");
		response.Success.Should().BeFalse(because: "the intentionally missing environment fails before any write");
		response.Error.Should().MatchRegex(
			$"(?is)({System.Text.RegularExpressions.Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should come from resolving the requested environment");
	}

	[Test]
	[Description("set-page-modal-size refuses a misspelled argument instead of silently dropping it.")]
	[AllureTag(SetPageModalSizeTool.ToolName)]
	[AllureName("set-page-modal-size refuses an unknown argument")]
	[AllureDescription("Calls set-page-modal-size with a misspelled size argument and verifies the structured response names it and nothing is written.")]
	public async Task SetPageModalSize_ShouldRefuseUnknownArgument() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			SetPageModalSizeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"noop-{Guid.NewGuid():N}",
					["package-name"] = "Custom",
					["page-schema-name"] = "UsrOrder_MiniPage",
					["size"] = "large",
					["window-size"] = "large"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		SetPageModalSizeResponse response =
			EntitySchemaStructuredResultParser.Extract<SetPageModalSizeResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "the refusal is a structured tool response");
		response.Success.Should().BeFalse(because: "an unknown argument may be the value the caller meant");
		response.Error.Should().Contain("window-size", because: "the response names the unknown argument");
	}

	private async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = Session;
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
