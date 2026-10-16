using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// Proves the get/update-entity-connections round trip on an explicitly selected disposable sandbox: reorder the
/// connections of an object, read the new order back through the server merge, refuse a non-lookup column with no
/// change, and restore the original list.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("McpE2E.Manual")]
[Category("LocalOnly")]
[Explicit("Writes the EntityConnections add-on of Custom for Account and restores it; requires an exclusive sandbox with the UseConnectionsComponent feature on.")]
[NonParallelizable]
// [AllureNUnit] is intentionally omitted, as in the related-page sandbox fixture: its lifecycle hooks can deadlock
// fixtures with many sequential awaits.
[AllureFeature(UpdateEntityConnectionsTool.ToolName)]
public sealed class EntityConnectionsSandboxE2ETests {
	private const string EntitySchemaName = "Account";
	private const string PackageName = "Custom";

	[Test]
	[Description("Reorders an object's connections, reads the order back, refuses a non-lookup column without a change, and restores the original list.")]
	[AllureTag(GetEntityConnectionsTool.ToolName)]
	[AllureTag(UpdateEntityConnectionsTool.ToolName)]
	[AllureName("Connections round trip through the EntityConnections add-on")]
	[AllureDescription("Uses the real MCP server against a disposable sandbox: read, swap the first two connections, verify the server merge, refuse a text column, restore and verify the original order.")]
	public async Task EntityConnections_ShouldReorderAndRestore_WhenSandboxIsConfigured() {
		// Arrange
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions("The Connections round trip requires a disposable local sandbox.");
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E__AllowDestructiveMcpTests=true for an exclusive disposable sandbox.");
		}
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(5));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, deadline.Token);
		Dictionary<string, object?> target = new() {
			["environment-name"] = settings.Sandbox.EnvironmentName,
			["entity-schema-name"] = EntitySchemaName,
			["package-name"] = PackageName
		};
		EntityConnectionsResponse original = await CallAsync(session, GetEntityConnectionsTool.ToolName, target, deadline.Token);
		original.Success.Should().BeTrue(because: $"the current connections must be captured before a write: {original.Error}");
		original.Warnings.Should().BeNullOrEmpty(
			because: "the round trip needs a list read with the add-on applied (UseConnectionsComponent on)");
		List<string> originalNames = original.Connections!.Select(connection => connection.ColumnName!).ToList();
		if (originalNames.Count < 2) {
			Assert.Ignore($"{EntitySchemaName} needs at least two connections on the sandbox to prove a reorder.");
		}
		List<string> swapped = [originalNames[1], originalNames[0], .. originalNames.Skip(2)];

		try {
			// Act / Assert
			await AllureApi.Step("Swap the first two connections and read the order back", async () => {
				EntityConnectionsResponse written = await CallAsync(session, UpdateEntityConnectionsTool.ToolName,
					new Dictionary<string, object?>(target) { ["connections"] = swapped }, deadline.Token);
				written.Success.Should().BeTrue(because: $"the reorder must be saved: {written.Error}");
				written.Changed.Should().BeTrue(because: "a different order is a write");
				EntityConnectionsResponse readBack = await CallAsync(session, GetEntityConnectionsTool.ToolName, target, deadline.Token);
				readBack.Connections!.Select(connection => connection.ColumnName).Should().Equal(swapped,
					because: "the server merge of the saved add-on must give the requested order");
				readBack.PackageLayer!.Items.Should().NotBeEmpty(because: "a reorder is stored as add-on items");
			});
			await AllureApi.Step("Repeat the same list without a write", async () => {
				EntityConnectionsResponse repeated = await CallAsync(session, UpdateEntityConnectionsTool.ToolName,
					new Dictionary<string, object?>(target) { ["connections"] = swapped }, deadline.Token);
				repeated.Success.Should().BeTrue(because: "a no-op update succeeds");
				repeated.Changed.Should().BeFalse(because: "an unchanged list writes nothing");
			});
			await AllureApi.Step("Refuse a non-lookup column without a change", async () => {
				EntityConnectionsResponse refused = await CallAsync(session, UpdateEntityConnectionsTool.ToolName,
					new Dictionary<string, object?>(target) { ["connections"] = new List<string>(swapped) { "Name" } },
					deadline.Token);
				refused.Success.Should().BeFalse(because: "only lookup columns can be connections");
				refused.Error.Should().Contain("'Name'", because: "the refused column is named");
				EntityConnectionsResponse unchanged = await CallAsync(session, GetEntityConnectionsTool.ToolName, target, deadline.Token);
				unchanged.Connections!.Select(connection => connection.ColumnName).Should().Equal(swapped,
					because: "a refused update must not write anything");
			});
		} finally {
			// A separate deadline lets the restoration run even when the assertion deadline expired.
			using CancellationTokenSource restoreDeadline = new(TimeSpan.FromMinutes(3));
			// The restore brings back the original ORDER only: like the Object designer's diff, it may leave items in
			// the Custom layer of the add-on instead of returning the layer to its original content. By design.
			await AllureApi.Step("Restore and verify the original order", async () => {
				EntityConnectionsResponse restored = await CallAsync(session, UpdateEntityConnectionsTool.ToolName,
					new Dictionary<string, object?>(target) { ["connections"] = originalNames }, restoreDeadline.Token);
				restored.Success.Should().BeTrue(because: $"the original list must be restored: {restored.Error}");
				restored.Connections!.Select(connection => connection.ColumnName).Should().Equal(originalNames,
					because: "the restored list must read back exactly as it was");
			});
		}
	}

	private static async Task<EntityConnectionsResponse> CallAsync(
		McpServerSession session, string toolName, Dictionary<string, object?> args, CancellationToken token) {
		var result = await session.CallToolAsync(toolName, new Dictionary<string, object?> { ["args"] = args }, token);
		result.IsError.Should().NotBeTrue(because: "the call must return the structured command result");
		return EntitySchemaStructuredResultParser.Extract<EntityConnectionsResponse>(result);
	}
}
