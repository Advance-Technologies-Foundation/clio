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
/// End-to-end tests for the get-related-page-addon MCP tool, driven through the real clio MCP server.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetRelatedPageAddonTool.ToolName)]
[NonParallelizable]
public sealed class GetRelatedPageAddonToolE2ETests {
	private const string ToolName = GetRelatedPageAddonTool.ToolName;

	[Test]
	[Description("Exposes get-related-page-addon through the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(ToolName)]
	[AllureName("get-related-page-addon MCP tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies get-related-page-addon is discoverable via the get-tool-contract compact index even though long-tail tools are not resident in tools/list.")]
	public async Task GetRelatedPageAddon_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "get-related-page-addon must be discoverable through get-tool-contract even though it is not resident in tools/list");
	}

	[Test]
	[Description("Reports an invalid environment inside the structured response rather than an MCP binding error.")]
	[AllureTag(ToolName)]
	[AllureName("get-related-page-addon MCP tool reports an invalid environment")]
	[AllureDescription("Starts the real clio MCP server, calls get-related-page-addon with an intentionally missing environment, and verifies the structured response reports the unresolved environment.")]
	public async Task GetRelatedPageAddon_ShouldReportInvalidEnvironment_WhenEnvironmentMissing() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-get-related-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["entity-schema-name"] = "UsrOrder"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<GetRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a resolvable payload should stay inside the structured tool response, not surface as an MCP binding error");
		response.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail before the add-on read");
		response.Error.Should().MatchRegex(
			$"(?is)({System.Text.RegularExpressions.Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should come from resolving the requested environment");
	}

	[Test]
	[Description("Binds schema-type=mobile through the real MCP server and reports an invalid environment inside the structured response, proving the mobile MobileRelatedPage read path is on the tool contract.")]
	[AllureTag(ToolName)]
	[AllureName("get-related-page-addon MCP tool binds schema-type=mobile")]
	[AllureDescription("Starts the real clio MCP server, calls get-related-page-addon with schema-type=mobile and an intentionally missing environment, and verifies the mobile schema-type binds and the structured response reports the unresolved environment instead of an MCP binding error.")]
	public async Task GetRelatedPageAddon_ShouldBindMobileSchemaType_AndReportInvalidEnvironment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-get-mobile-related-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["entity-schema-name"] = "UsrOrder",
					["schema-type"] = "mobile"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<GetRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "schema-type=mobile should bind and stay inside the structured tool response, not surface as an MCP binding error");
		response.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail before the mobile add-on read");
		response.Error.Should().MatchRegex(
			$"(?is)({System.Text.RegularExpressions.Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should come from resolving the environment, not from rejecting the mobile schema-type");
	}

	[Test]
	[Description("Rejects a blank entity-schema-name in the structured response before any remote call.")]
	[AllureTag(ToolName)]
	[AllureName("get-related-page-addon MCP tool rejects a blank entity-schema-name")]
	[AllureDescription("Starts the real clio MCP server, calls get-related-page-addon with a blank entity-schema-name, and verifies the structured response reports the missing field without an MCP binding error.")]
	public async Task GetRelatedPageAddon_ShouldRejectBlankEntitySchemaName_WhenWhitespace() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"noop-{Guid.NewGuid():N}",
					["package-name"] = "Custom",
					["entity-schema-name"] = " "
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<GetRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "input validation should stay inside the structured tool response, not surface as an MCP binding error");
		response.Success.Should().BeFalse(
			because: "a blank entity-schema-name fails validation, so the structured response reports failure rather than reading the add-on");
		response.Error.Should().Contain("entity-schema-name",
			because: "the structured response should name the missing required field");
	}

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
