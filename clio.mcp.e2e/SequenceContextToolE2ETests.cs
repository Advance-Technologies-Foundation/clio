using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Real-process discovery and validation for sequence context.</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(SequenceContextTool.ToolName)]
[NonParallelizable]
public sealed class SequenceContextToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Sequence context is reachable on the long-tail surface as a read-only tool.")]
	[AllureTag(SequenceContextTool.ToolName)]
	public async Task Discovery_IsReadOnly() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		// Act
		var index = await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);
		// Assert
		index.Should().ContainSingle(x => x.Name == SequenceContextTool.ToolName,
			because: "the new command must be discoverable through the real MCP server")
			.Which.Destructive.Should().NotBe(true, because: "context discovery never writes to Creatio");
	}

	[Test]
	[Description("A malformed sequence UUID is rejected by the external MCP argument contract before remote reads.")]
	[AllureTag(SequenceContextTool.ToolName)]
	public async Task InvalidId_IsRejected() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		// Act
		var result = await context.Session.CallToolAsync(SequenceContextTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
				["environment-name"] = "nonexistent-sequence-test", ["sequence-id"] = "not-a-guid"
			}}, context.CancellationTokenSource.Token);
		// Assert
		result.IsError.Should().BeTrue(because: "a malformed UUID cannot bind to a valid sequence inspection");
		string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text)).Should()
			.Contain("sequence-id", because: "the UUID contract must reject the request before environment resolution");
	}
}
