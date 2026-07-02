using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for the link-from-repository MCP tools.
/// </summary>
[TestFixture]
[AllureFeature("link-from-repository")]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class LinkFromRepositoryContractToolE2ETests : McpContractFixtureBase {
	private const string EnvironmentToolName = LinkFromRepositoryTool.LinkFromRepositoryByEnvironmentToolName;
	private const string EnvPkgPathToolName = LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePathToolName;

	[Test]
	[Description("Starts the real clio MCP server, lists tools, and verifies that both link-from-repository MCP endpoints are advertised as destructive.")]
	[AllureTag(EnvironmentToolName)]
	[AllureTag(EnvPkgPathToolName)]
	[AllureName("Link From Repository tools advertise destructive metadata")]
	[AllureDescription("Uses the real MCP discovery response to verify that both link-from-repository tools expose the destructive hint required for client-side safety policies.")]
	public async Task LinkFromRepository_Tools_Should_Be_Advertised_As_Destructive() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertToolIsAdvertisedAsDestructive(tools, EnvironmentToolName);
		AssertToolIsAdvertisedAsDestructive(tools, EnvPkgPathToolName);
	}

	[AllureStep("Assert discovered MCP tool is marked as destructive")]
	[AllureDescription("Assert from the real MCP discovery payload that the requested link-from-repository tool exposes the destructive hint")]
	private static void AssertToolIsAdvertisedAsDestructive(IList<McpClientTool> tools, string toolName) {
		McpClientTool tool = tools.Single(tool => tool.Name == toolName);

		tool.ProtocolTool.Annotations.Should().NotBeNull(
			because: "the MCP server should expose tool annotations for clients that apply safety policies");
		tool.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "link-from-repository removes existing package directories before replacing them with symbolic links");
	}
}
