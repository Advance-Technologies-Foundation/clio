using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>ENG-103025: the suggested-query that execute-esq returns for SQL runs against a live stand as is.</summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ExecuteEsqTool.ToolName)]
[NonParallelizable]
public sealed class ExecuteEsqSuggestedQuerySandboxE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq suggested-query runs unchanged on a live stand")]
	[AllureDescription("Sends SQL text, takes the returned suggested-query and resends it as query, then verifies the real Supervisor Contact row comes back.")]
	[Description("The SelectQuery returned for 'SELECT Id, Name FROM Contact WHERE Name = 'Supervisor' LIMIT 1' executes and returns the Supervisor Contact.")]
	public async Task ExecuteEsq_SuggestedQuery_Should_Execute_Against_Stand() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			$"execute-esq suggested-query MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ExecuteEsqResponse hint = await CallAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["query"] = "SELECT Id, Name FROM Contact WHERE Name = 'Supervisor' LIMIT 1"
		});
		hint.SuggestedQuery.Should().NotBeNull(because: "the SQL is a single-table SELECT with an equality filter");
		object suggested = JsonSerializer.Deserialize<object>(hint.SuggestedQuery!.Value.GetRawText())!;
		ExecuteEsqResponse executed = await CallAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["query"] = suggested
		});

		// Assert
		executed.Success.Should().BeTrue(because: $"the suggested query must be valid as returned: {executed.Error}");
		executed.Count.Should().Be(1, because: "one Supervisor contact exists and LIMIT 1 was carried over");
		executed.Rows!.Value[0].GetProperty("Name").GetString().Should().Be("Supervisor",
			because: "the equality filter was carried over");
	}

	private static async Task<ExecuteEsqResponse> CallAsync(ArrangeContext arrange, Dictionary<string, object?> args) {
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ExecuteEsqTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrange.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "execute-esq reports failures as structured results");
		return EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);
	}
}
