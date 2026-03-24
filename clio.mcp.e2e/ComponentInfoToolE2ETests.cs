using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the component-info MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("component-info")]
[NonParallelizable]
public sealed class ComponentInfoToolE2ETests {
	private const string ToolName = ComponentInfoTool.ToolName;

	[Test]
	[Description("Advertises component-info in the MCP tool list so callers can discover the component catalog.")]
	[AllureTag(ToolName)]
	[AllureName("component-info tool is advertised by the MCP server")]
	[AllureDescription("Verifies that component-info appears in the MCP server tool manifest.")]
	public async Task ComponentInfoTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "component-info must be discoverable through the MCP tool manifest");
	}

	[Test]
	[Description("Returns grouped summaries in list mode and full metadata in detail mode using the real MCP server process.")]
	[AllureTag(ToolName)]
	[AllureName("component-info returns grouped summaries and detail metadata")]
	[AllureDescription("Starts the real clio MCP server, lists components with a tab search, then requests full metadata for crt.TabContainer.")]
	public async Task ComponentInfoTool_Should_Return_List_And_Detail_Metadata() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ComponentInfoResponse listResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["search"] = "tab" });
		ComponentInfoResponse detailResponse = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["component-type"] = "crt.TabContainer" });

		// Assert
		listResponse.Success.Should().BeTrue(
			because: "list mode should succeed with the shipped component registry");
		listResponse.Mode.Should().Be("list",
			because: "search-only queries should keep component-info in list mode");
		listResponse.Count.Should().BeGreaterThan(0,
			because: "the shipped registry should contain tab-related component metadata");
		listResponse.Groups.Should().NotBeNullOrEmpty(
			because: "list mode should return grouped summaries");
		listResponse.Groups!.SelectMany(group => group.Items).Select(item => item.ComponentType)
			.Should().Contain("crt.TabContainer",
				because: "the tab search should surface crt.TabContainer from the shipped registry");
		detailResponse.Success.Should().BeTrue(
			because: "detail mode should succeed for curated component types");
		detailResponse.Mode.Should().Be("detail",
			because: "component-type lookups should return the detail contract");
		detailResponse.ComponentType.Should().Be("crt.TabContainer",
			because: "the detail response should echo the requested component type");
		detailResponse.Properties.Should().ContainKey("caption",
			because: "detail mode should expose curated property metadata");
	}

	[Test]
	[Description("Returns a readable not-found response when component-info receives an unknown component type.")]
	[AllureTag(ToolName)]
	[AllureName("component-info reports unknown component types")]
	[AllureDescription("Starts the real clio MCP server, requests an unknown component type, and verifies that the failure stays structured and readable.")]
	public async Task ComponentInfoTool_Should_Report_Unknown_Component_Types() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ComponentInfoResponse response = await CallComponentInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["component-type"] = "crt.DoesNotExist" });

		// Assert
		response.Success.Should().BeFalse(
			because: "unknown component lookups should return a structured failure envelope");
		response.Error.Should().Contain("crt.DoesNotExist",
			because: "the failure should identify the missing component type");
		response.Groups.Should().NotBeNullOrEmpty(
			because: "the fallback response should still expose available types for discovery");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static async Task<ComponentInfoResponse> CallComponentInfoAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "component-info should return structured responses instead of top-level MCP failures");
		return EntitySchemaStructuredResultParser.Extract<ComponentInfoResponse>(callResult);
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
