using Allure.NUnit;
using Allure.NUnit.Attributes;
using Allure.Net.Commons;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the execute-esq MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ExecuteEsqTool.ToolName)]
[NonParallelizable]
public sealed class ExecuteEsqToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Exposes execute-esq as a discoverable, non-destructive tool via the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq MCP tool is discoverable on the lazy surface")]
	public async Task ExecuteEsq_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ExecuteEsqTool.ToolName,
			because: $"the {ExecuteEsqTool.ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(entry => entry.Name == ExecuteEsqTool.ToolName,
				because: "the compact discovery index must carry exactly one entry for execute-esq")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "execute-esq is a read-only query tool and must not be flagged destructive in the discovery index");
	}

	[Test]
	[Description("Binds execute-esq arguments through the real MCP server and returns a structured failure for an unknown environment.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq MCP tool binds arguments")]
	public async Task ExecuteEsq_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-esq-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ExecuteEsqTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["query"] = new Dictionary<string, object?> {
						["rootSchemaName"] = "Contact",
						["operationType"] = 0,
						["allColumns"] = false,
						["columns"] = new Dictionary<string, object?> {
							["items"] = new Dictionary<string, object?> {
								["Id"] = new Dictionary<string, object?> {
									["expression"] = new Dictionary<string, object?> {
										["expressionType"] = 0,
										["columnPath"] = "Id"
									}
								}
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid execute-esq payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Rejects a malformed or missing DateTime parameter through the real stdio MCP server with a path-specific accepted-format diagnostic before environment resolution.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq explains the required DateTime parameter encoding")]
	[AllureDescription("Invokes execute-esq through the real stdio MCP server with a nested malformed or missing DateTime value and verifies the structured response names the exact query path and required JSON-encoded shape without contacting an environment.")]
	public async Task ExecuteEsq_ShouldRejectMalformedDateTimeParameter_WhenCalledOverStdio(bool includePlainValue) {
		// Arrange
		await using var arrangeContext = await AllureApi.Step(
			"Arrange a real stdio MCP session",
			() => Task.FromResult(Arrange(TimeSpan.FromMinutes(3))));
		string invalidEnvironmentName = $"missing-esq-date-env-{Guid.NewGuid():N}";

		// Act
		Dictionary<string, object?> parameter = new() {
			["dataValueType"] = 7
		};
		if (includePlainValue) {
			parameter["value"] = "2026-08-10T00:00:00Z";
		}
		CallToolResult callResult = await AllureApi.Step("Act by submitting a malformed DateTime parameter", async () =>
			await arrangeContext.Session.CallToolAsync(
			ExecuteEsqTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["query"] = new Dictionary<string, object?> {
						["rootSchemaName"] = "SysSchema",
						["filters"] = new Dictionary<string, object?> {
							["items"] = new Dictionary<string, object?> {
								["ModifiedAfter"] = new Dictionary<string, object?> {
									["rightExpression"] = new Dictionary<string, object?> {
										["expressionType"] = 2,
										["parameter"] = parameter
									}
								}
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token));
		ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(callResult);

		// Assert
		await AllureApi.Step("Assert validation uses the structured tool response", () => {
			callResult.IsError.Should().NotBeTrue(
				because: "query validation failures should use the structured execute-esq response contract");
			return Task.CompletedTask;
		});
		await AllureApi.Step("Assert the structured response reports failure", () => {
			response.Success.Should().BeFalse(
				because: "a plain or missing DateTime parameter is not a valid SelectQuery temporal value");
			return Task.CompletedTask;
		});
		await AllureApi.Step("Assert the exact query path is reported", () => {
			response.Error.Should().Contain("$.filters.items.ModifiedAfter.rightExpression.parameter.value",
				because: "the stdio response should identify the exact malformed query location");
			return Task.CompletedTask;
		});
		await AllureApi.Step("Assert the accepted temporal format is explained", () => {
			response.Error.Should().Contain("JSON-encoded strings",
				because: "the stdio caller should receive the accepted temporal value format");
			return Task.CompletedTask;
		});
		await AllureApi.Step("Assert the reusable example survives passthrough redaction", () => {
			response.Error.Should().Contain(
				"value text '2026-01-01T00:00:00.000Z' including the two single quote characters",
				because: "the complete reusable value example must survive MCP passthrough redaction");
			return Task.CompletedTask;
		});
		await AllureApi.Step("Assert validation precedes environment access", () => {
			response.Error.Should().NotContain(invalidEnvironmentName,
				because: "temporal validation should finish before environment resolution or network access");
			return Task.CompletedTask;
		});
	}

}
