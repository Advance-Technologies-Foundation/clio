using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for the <c>find-app</c> MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class FindAppContractToolE2ETests : McpContractFixtureBase {
	private const string FindAppToolName = FindAppTool.FindAppToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies that find-app is advertised so callers can discover the fast app-discovery tool.")]
	[AllureTag(FindAppToolName)]
	[AllureName("find-app is advertised by the MCP server")]
	[AllureDescription("Starts the real clio MCP server and verifies that find-app appears in the advertised tool manifest.")]
	public async Task FindApp_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(FindAppToolName,
			because: "find-app must be advertised so MCP callers can discover the fast app-discovery tool");
	}
}
