using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

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
	[Description("Starts the real clio MCP server, reads the get-tool-contract compact index, and verifies that both link-from-repository MCP endpoints are discoverable and flagged destructive on the lazy tool surface.")]
	[AllureTag(EnvironmentToolName)]
	[AllureTag(EnvPkgPathToolName)]
	[AllureName("Link From Repository tools expose destructive metadata on the lazy surface")]
	[AllureDescription("Uses the get-tool-contract compact index of the real MCP server to verify that both link-from-repository tools expose the destructive flag required for client-side safety policies.")]
	public async Task LinkFromRepository_Tools_Should_Be_Advertised_As_Destructive() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();

		// Act
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrangeContext.Session.GetToolContractIndexAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertToolIsDiscoverableAsDestructive(index, EnvironmentToolName);
		AssertToolIsDiscoverableAsDestructive(index, EnvPkgPathToolName);
	}

	[AllureStep("Assert compact-index entry is marked as destructive")]
	[AllureDescription("Assert from the real get-tool-contract compact index that the requested link-from-repository tool exposes the destructive flag")]
	private static void AssertToolIsDiscoverableAsDestructive(IReadOnlyList<ToolContractIndexEntry> index, string toolName) {
		ToolContractIndexEntry entry = index.Should().ContainSingle(entry => entry.Name == toolName,
			because: $"the {toolName} MCP tool must be discoverable via the get-tool-contract compact index so clients that apply safety policies can find it")
			.Which;

		entry.Destructive.Should().BeTrue(
			because: "link-from-repository removes existing package directories before replacing them with symbolic links");
	}
}
