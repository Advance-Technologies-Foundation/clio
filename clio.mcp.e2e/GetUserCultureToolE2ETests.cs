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
/// End-to-end tests for the get-user-culture MCP tool. NOT part of CI — run manually against a
/// real clio mcp-server process.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(GetUserCultureTool.ToolName)]
[NonParallelizable]
public sealed class GetUserCultureToolE2ETests {
	[Test]
	[Description("Advertises get-user-culture as a read-only, non-destructive MCP tool through the real MCP server.")]
	[AllureTag(GetUserCultureTool.ToolName)]
	[AllureName("get-user-culture MCP tool is advertised")]
	public async Task GetUserCulture_Should_Be_Advertised() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		McpClientTool tool = tools.Single(tool => tool.Name == GetUserCultureTool.ToolName);
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue(
			because: "get-user-culture only reads the profile culture and must be advertised as read-only");
		tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse(
			because: "get-user-culture must not mutate Creatio state");
	}

	[Test]
	[Description("Binds get-user-culture arguments through the real MCP server and returns a structured failure signal for an unknown environment.")]
	[AllureTag(GetUserCultureTool.ToolName)]
	[AllureName("get-user-culture MCP tool binds arguments")]
	public async Task GetUserCulture_Should_Bind_Arguments_And_Report_Failure_For_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-culture-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			GetUserCultureTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetUserCultureResponse response = EntitySchemaStructuredResultParser.Extract<GetUserCultureResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid get-user-culture payloads should bind and return a structured tool response, not a protocol error");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment cannot yield a profile culture");
		response.Reason.Should().NotBeNullOrWhiteSpace(
			because: "the failure signal must carry a machine-readable reason so the agent can ask the user");
		response.Culture.Should().BeNull(
			because: "a failure signal must never surface a fallback culture as if it were resolved");
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
