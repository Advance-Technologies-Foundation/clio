using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the execute-esq MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ExecuteEsqTool.ToolName)]
[NonParallelizable]
public sealed class ExecuteEsqToolE2ETests {
	[Test]
	[Description("Advertises execute-esq as a read-only MCP tool through the real MCP server.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq MCP tool is advertised")]
	public async Task ExecuteEsq_Should_Be_Advertised() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		McpClientTool tool = tools.Single(tool => tool.Name == ExecuteEsqTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue(
			because: "execute-esq must be advertised as a read-only query tool");
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse(
			because: "execute-esq must not mutate Creatio state");
	}

	[Test]
	[Description("Binds execute-esq arguments through the real MCP server and returns a structured failure for an unknown environment.")]
	[AllureTag(ExecuteEsqTool.ToolName)]
	[AllureName("execute-esq MCP tool binds arguments")]
	public async Task ExecuteEsq_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
