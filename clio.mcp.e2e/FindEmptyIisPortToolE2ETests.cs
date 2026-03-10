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

[TestFixture]
[AllureNUnit]
[AllureFeature("find-empty-iis-port")]
public sealed class FindEmptyIisPortToolE2ETests
{
	private const string ToolName = FindEmptyIisPortTool.FindEmptyIisPortToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes find-empty-iis-port, and verifies the structured IIS port-discovery payload shape.")]
	[AllureTag(ToolName)]
	[AllureName("Find empty IIS port returns structured result")]
	[AllureDescription("Uses the real clio MCP server to invoke find-empty-iis-port and verifies the fixed range payload plus the first available port contract.")]
	public async Task FindEmptyIisPort_Should_Return_Structured_Result()
	{
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		ActResult actResult = await ActAsync(arrangeContext);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "IIS port discovery should report availability inside the payload instead of as an MCP transport error");
		new[] { "available", "unavailable" }.Should().Contain(actResult.Execution.Status,
			because: "the IIS port discovery contract should always expose one of the defined availability states");
		actResult.Execution.RangeStart.Should().Be(FindEmptyIisPortTool.RangeStart,
			because: "the MCP tool should scan the fixed lower bound requested for local IIS deployments");
		actResult.Execution.RangeEnd.Should().Be(FindEmptyIisPortTool.RangeEnd,
			because: "the MCP tool should scan the fixed upper bound requested for local IIS deployments");
		actResult.Execution.Summary.Should().NotBeNullOrWhiteSpace(
			because: "the payload should include a human-readable scan summary");
		actResult.Execution.IisBoundPortCount.Should().BeGreaterThanOrEqualTo(0,
			because: "the payload should report how many IIS-bound ports were seen in the fixed discovery range");
		actResult.Execution.ActiveTcpPortCount.Should().BeGreaterThanOrEqualTo(0,
			because: "the payload should report how many active TCP reservations were seen in the fixed discovery range");

		if (actResult.Execution.Status == "available")
		{
			actResult.Execution.FirstAvailablePort.Should().NotBeNull(
				because: "an available result should provide the first free IIS deployment port");
			actResult.Execution.FirstAvailablePort.Should().BeInRange(FindEmptyIisPortTool.RangeStart, FindEmptyIisPortTool.RangeEnd,
				because: "the recommended IIS port should stay inside the fixed discovery range");
		}
		else
		{
			actResult.Execution.FirstAvailablePort.Should().BeNull(
				because: "an unavailable result should not claim that a free IIS deployment port exists");
		}
	}

	[AllureStep("Arrange find-empty-iis-port MCP session")]
	private static async Task<ArrangeContext> ArrangeAsync()
	{
		McpE2ESettings settings = TestConfiguration.Load();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	[AllureStep("Act by invoking find-empty-iis-port through MCP")]
	private static async Task<ActResult> ActAsync(ArrangeContext arrangeContext)
	{
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the find-empty-iis-port MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?>(),
			arrangeContext.CancellationTokenSource.Token);

		FindAvailableIisPortEnvelope execution = FindAvailableIisPortResultParser.Extract(callResult);
		return new ActResult(callResult, execution);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable
	{
		public async ValueTask DisposeAsync()
		{
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record ActResult(
		CallToolResult CallResult,
		FindAvailableIisPortEnvelope Execution);
}
