using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Proves that the query-shape diagnostic supplies a working recovery call.</summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ExecuteEsqTool.ToolName)]
[NonParallelizable]
public sealed class ExecuteEsqRecoveryToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Replays the minimal example returned for a missing query through clio-run against real Creatio.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("Recover from a missing ESQ query using the error example")]
	[AllureDescription("Submit the reported schema-name/columns/row-count shape, then execute the returned example with only the environment placeholder replaced and verify a real Contact Id.")]
	public async Task Execute_ShouldReturnContactId_WhenRetryingDiagnosticExample() {
		// Arrange
		string? environmentName = TestConfiguration.Load().Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("A registered Creatio sandbox environment is required.");
		}
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult rejected = await AllureApi.Step("Send the reported request without query", async () =>
			await context.Session.CallToolAsync(ClioRunTool.ToolName, new Dictionary<string, object?> {
				["command"] = ExecuteEsqTool.ToolName,
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["schema-name"] = "Contact",
					["columns"] = new[] { "Id" },
					["row-count"] = 50
				}
			}, context.CancellationTokenSource.Token));
		ExecuteEsqResponse failure = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(rejected);

		// Assert
		AllureApi.Step("Verify a structured failure", () => failure.Success.Should().BeFalse(
			because: "the reported payload does not supply query"));
		AllureApi.Step("Verify the argument is named", () => failure.Error.Should().Contain("'query' argument",
			because: "callers must know where the SelectQuery belongs"));
		AllureApi.Step("Verify this is not a protocol error", () => rejected.IsError.Should().NotBeTrue(
			because: "query validation returns the structured recovery response"));

		// Act
		string error = failure.Error!;
		string exampleJson = error[(error.IndexOf('\n') + 1)..]
			.Replace("<environment-name>", environmentName, StringComparison.Ordinal);
		Dictionary<string, object?> example = JsonSerializer.Deserialize<Dictionary<string, object?>>(exampleJson)!;
		CallToolResult retried = await AllureApi.Step("Retry the example from the error", async () =>
			await context.Session.CallToolAsync(ClioRunTool.ToolName, example, context.CancellationTokenSource.Token));
		ExecuteEsqResponse success = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(retried);

		// Assert
		AllureApi.Step("Verify successful MCP execution", () => retried.IsError.Should().NotBeTrue(
			because: "the copied example must bind through the real MCP process"));
		AllureApi.Step("Verify successful DataService execution", () => success.Success.Should().BeTrue(
			because: "the returned example must work against Creatio: " + success.Error));
		AllureApi.Step("Verify the bounded row count", () => success.Count.Should().Be(1,
			because: "the example requests one Contact and the sandbox contains the Supervisor contact"));
		AllureApi.Step("Verify the real Contact identifier", () =>
			Guid.Parse(success.Rows!.Value[0].GetProperty("Id").GetString()!).Should().NotBe(Guid.Empty,
				because: "acceptance requires a real row, not just a successful transport response"));
	}
}
