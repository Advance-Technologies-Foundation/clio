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

/// <summary>Real-process discovery, validation and explicitly opted-in lab writes.</summary>
[TestFixture, AllureNUnit, NonParallelizable]
[AllureFeature(DataServiceBatchTool.ToolName)]
public sealed class DataServiceBatchToolE2ETests : McpContractFixtureBase {
	[Test, Category("McpE2E.NoEnvironment")]
	[Description("Native batching is discoverable as destructive and rejects malformed UUIDs before environment access.")]
	public async Task Discovery_ShouldExposeDestructiveTool_AndRejectMalformedUuid() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		// Act
		var index = await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);
		var result = await context.Session.CallToolAsync(DataServiceBatchTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> { ["environment-name"] = "unused", ["operations"] = new[] {
				new Dictionary<string, object?> { ["operation"] = "delete", ["schema-name"] = "Contact", ["record-id"] = "invalid" }
			}} }, context.CancellationTokenSource.Token);
		// Assert
		index.Should().ContainSingle(item => item.Name == DataServiceBatchTool.ToolName, because: "batch writes must be discoverable")
			.Which.Destructive.Should().BeTrue(because: "the native request can change records");
		result.IsError.Should().BeTrue(because: "malformed targeting must fail before a write");
		string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text)).Should().Contain("invalid-parameter-type: argument 'operations'",
			because: "UUID binding must reject the operation before environment resolution");
	}

	[Test, Category("McpE2E.Manual")]
	[Description("An explicit disposable Contact receives two updates around an intentional invalid-column failure in one native batch.")]
	public async Task Execute_ShouldPreserveMixedOutcomes_WhenLabContactIsExplicit() {
		// Arrange
		var settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		if (!Guid.TryParse(Environment.GetEnvironmentVariable("CLIO_BATCH_TEST_CONTACT_ID"), out Guid contactId)) {
			Assert.Ignore("Set CLIO_BATCH_TEST_CONTACT_ID to an owned disposable Contact. This test changes its Name.");
			return;
		}
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
		var operations = new[] { "Name", "UsrMissingBatchTestColumn", "Name" }.Select((column, index) =>
			new Dictionary<string, object?> {
				["operation"] = "update", ["schema-name"] = "Contact", ["record-id"] = contactId,
				["values"] = new Dictionary<string, object?> { [column] = new Dictionary<string, object?> {
					["data-value-type"] = 1, ["value"] = $"Batch MCP test {index}"
				}}
			}).ToArray();
		// Act
		var call = await Session.CallToolAsync(DataServiceBatchTool.ToolName, new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> { ["environment-name"] = settings.Sandbox.EnvironmentName, ["operations"] = operations }
		}, timeout.Token);
		var result = EntitySchemaStructuredResultParser.Extract<DataServiceBatchResult>(call);
		// Assert
		call.IsError.Should().NotBeTrue(because: "partial native failures use structured outcomes");
		result.Items.Select(item => item.State).Should().Equal(new[] { "completed", "failed", "completed" },
			because: "native continuation must preserve both successful neighbors");
		result.UnknownCount.Should().Be(0, because: "the native service must correlate all three lab results");
		result.Items[1].Diagnostic.SideEffect.Should().Be("unknown", because: "failure must not claim that no side effects occurred");
		result.Items[1].Diagnostic.Entity.Should().Be("Contact", because: "the failed operation needs explicit entity context");
		result.Items[1].Error.Should().Contain("UsrMissingBatchTestColumn", because: "the actual platform validation message must identify the rejected column");
		result.Items.Where(item => item.State == "completed").Should().OnlyContain(item => item.RowsAffected == 1,
			because: "both updates must target the selected existing lab contact");
	}
}
