using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-103025: execute-esq answers SQL text with the query shape and a ready-to-resend suggested-query, and never
/// executes it. The hint is built before any environment is resolved.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ExecuteEsqTool.ToolName)]
[NonParallelizable]
public sealed class ExecuteEsqQueryHintsE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq turns a simple SELECT into a suggested-query")]
	[AllureDescription("Sends SQL text as query through the real MCP server and verifies the structured failure carries the equivalent SelectQuery and nothing was executed.")]
	[Description("execute-esq with a single-table SELECT returns suggested-query for Contact/Name with the equality filter and row cap.")]
	public async Task ExecuteEsq_Should_Suggest_Query_For_Simple_Sql() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ExecuteEsqResponse response = await CallAsync(arrange, new Dictionary<string, object?> {
			["query"] = "SELECT Name FROM Contact WHERE Name = 'Supervisor' LIMIT 5"
		});

		// Assert
		response.Success.Should().BeFalse(because: "SQL text is never executed");
		response.Error.Should().Contain("suggested-query", because: "the failure points at the resend-ready query");
		response.SuggestedQuery.Should().NotBeNull(because: "a single-table SELECT with an equality filter is translatable");
		JsonElement query = response.SuggestedQuery!.Value;
		query.GetProperty("rootSchemaName").GetString().Should().Be("Contact", because: "FROM Contact names the root schema");
		query.GetProperty("rowCount").GetInt32().Should().Be(5, because: "LIMIT 5 is carried over");
		query.GetProperty("columns").GetProperty("items").TryGetProperty("Name", out _).Should().BeTrue(
			because: "the selected column is translated");
		query.GetRawText().Should().Contain("Supervisor", because: "the equality filter value is carried over");
	}

	[Test]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq gives the shape only for SQL it cannot translate safely")]
	[AllureDescription("Sends SQL with a join and verifies the response explains the shape and offers no suggested-query.")]
	[Description("execute-esq with a JOIN returns a failure without suggested-query.")]
	public async Task ExecuteEsq_Should_Not_Suggest_Query_For_Join() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ExecuteEsqResponse response = await CallAsync(arrange, new Dictionary<string, object?> {
			["sql"] = "SELECT c.Name FROM Contact c JOIN Account a ON a.Id = c.AccountId"
		});

		// Assert
		response.Success.Should().BeFalse(because: "SQL text is never executed");
		response.SuggestedQuery.Should().BeNull(because: "a join has no single obvious SelectQuery, and a guess would read different data");
		response.Error.Should().Contain("'query' argument", because: "the shape is still explained");
	}

	[Test]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq suggests no query for a number literal")]
	[AllureDescription("Review ENG-103025: 'IsActive = 1' cannot tell a Boolean column from an Integer one, so no suggested-query is returned.")]
	[Description("execute-esq with a numeric equality literal returns the shape and no suggested-query.")]
	public async Task ExecuteEsq_Should_Not_Suggest_Query_For_A_Number_Literal() {
		// Arrange
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ExecuteEsqResponse response = await CallAsync(arrange, new Dictionary<string, object?> {
			["query"] = "SELECT Name FROM Contact WHERE IsActive = 1"
		});

		// Assert
		response.Success.Should().BeFalse(because: "SQL text is never executed");
		response.SuggestedQuery.Should().BeNull(because: "a filter typed from the literal would often be wrong");
		response.Error.Should().Contain("The filter on 'IsActive'", because: "the caller learns which filter blocked the suggestion");
	}

	private static async Task<ExecuteEsqResponse> CallAsync(ArrangeContext arrange, Dictionary<string, object?> args) {
		args["environment-name"] = $"missing-esq-hint-env-{Guid.NewGuid():N}";
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ExecuteEsqTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrange.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "a shape hint is a structured failure, not a protocol error");
		return EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);
	}
}
