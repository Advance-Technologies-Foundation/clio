using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Checks native enrollment discovery and binding through the external MCP process.</summary>
[TestFixture, Category("McpE2E.NoEnvironment"), AllureNUnit, NonParallelizable]
[AllureFeature(SequenceEnrollmentTool.ToolName)]
public sealed class SequenceEnrollmentToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Enrollment is discoverable as a destructive tool and rejects malformed IDs before resolving an environment.")]
	public async Task DiscoveryAndInvalidId_AreExplicit() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		// Act
		var index = await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);
		var call = await context.Session.CallToolAsync(SequenceEnrollmentTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
				["environment-name"] = "unused-contract-test", ["sequence-id"] = "not-a-guid",
				["contact-ids"] = new[] { Guid.NewGuid().ToString() }
			}}, context.CancellationTokenSource.Token);
		// Assert
		index.Should().ContainSingle(entry => entry.Name == SequenceEnrollmentTool.ToolName && entry.Destructive == true,
			because: "native enrollment can create participants and activities");
		call.IsError.Should().BeTrue(because: "malformed target IDs cannot authorize enrollment");
		string.Join(" ", call.Content.OfType<TextContentBlock>().Select(block => block.Text)).Should().Contain("sequence-id",
			because: "binding must reject the ID before any missing-environment failure can mask it");
	}
}
