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

	[Test]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq refuses a SelectQuery with columns as an array and its suggestion returns the column")]
	[AllureDescription("QA ENG-103025: {rootSchemaName:Contact, columns:[Name]} used to run and return Id-only rows. It must be refused unexecuted, and the suggested query must return Name.")]
	[Description("QA ENG-103025: a SelectQuery with columns as an array is refused instead of returning Id-only rows, and the returned suggested-query reads the Name column on the live stand.")]
	public async Task ExecuteEsq_Should_Refuse_Array_Columns_And_Suggest_A_Query_That_Returns_Them() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			$"execute-esq shape-refusal MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ExecuteEsqResponse refused = await CallAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["query"] = new Dictionary<string, object?> { ["rootSchemaName"] = "Contact", ["columns"] = new[] { "Name" } }
		});
		refused.SuggestedQuery.Should().NotBeNull(because: "a single schema with a plain column list has one safe reading");
		object suggested = JsonSerializer.Deserialize<object>(refused.SuggestedQuery!.Value.GetRawText())!;
		ExecuteEsqResponse executed = await CallAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["query"] = suggested
		});

		// Assert
		refused.Success.Should().BeFalse(because: "running it would drop the columns and return Id-only rows");
		refused.Rows.Should().BeNull(because: "nothing was executed");
		refused.Error.Should().Contain("'columns' must be an object", because: "the refusal names the wrong part");
		executed.Success.Should().BeTrue(because: $"the suggested query must be valid as returned: {executed.Error}");
		executed.Count.Should().BeGreaterThan(0, because: "the stand has contacts");
		executed.Rows!.Value[0].TryGetProperty("Name", out _).Should().BeTrue(
			because: "the requested column is in the rows, not silently dropped");
	}

	[Test]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq refuses write SQL as read-only")]
	[AllureDescription("QA ENG-103025: UPDATE text is refused with a read-only message, not a JSON parse error.")]
	[Description("QA ENG-103025: UPDATE SQL text gets the read-only refusal and nothing is executed.")]
	public async Task ExecuteEsq_Should_Refuse_Write_Sql_As_Read_Only() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			$"execute-esq read-only MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ExecuteEsqResponse response = await CallAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["query"] = "UPDATE Contact SET Name = 'x' WHERE Name = 'nobody-clio-e2e'"
		});

		// Assert
		response.Success.Should().BeFalse(because: "execute-esq never writes");
		response.Error.Should().Contain("execute-esq is read-only", because: "the caller learns why")
			.And.NotContain("not valid JSON", because: "the statement is SQL, not malformed JSON");
	}

	[Test]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq runs a query carrying an unknown member, with a warning")]
	[AllureDescription("Review ENG-103025: a member clio does not know as a SelectQuery member no longer refuses a working query; it runs on the live stand and the response names the member in warnings.")]
	[Description("Review ENG-103025: a well-formed SelectQuery with an unknown extra member runs on the stand and returns a warning naming it.")]
	public async Task ExecuteEsq_Should_Run_With_A_Warning_When_The_Query_Has_An_Unknown_Member() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			$"execute-esq unknown-member MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
		await using var arrange = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ExecuteEsqResponse response = await CallAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["query"] = new Dictionary<string, object?> {
				["rootSchemaName"] = "Contact",
				["operationType"] = 0,
				["allColumns"] = false,
				["rowCount"] = 1,
				["columns"] = new Dictionary<string, object?> {
					["items"] = new Dictionary<string, object?> {
						["Name"] = new Dictionary<string, object?> {
							["expression"] = new Dictionary<string, object?> { ["expressionType"] = 0, ["columnPath"] = "Name" }
						}
					}
				},
				["clioE2eUnknownMember"] = true
			}
		});

		// Assert
		response.Success.Should().BeTrue(because: $"an unknown member must not turn a working query into a failure: {response.Error}");
		response.Warnings.Should().ContainSingle().Which.Should().Contain("'clioE2eUnknownMember'",
			because: "the caller learns the member may have been ignored");
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
