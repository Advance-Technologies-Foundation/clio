using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("find-empty-iis-port")]
public sealed class FindEmptyIisPortToolE2ETests : McpContractFixtureBase
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
		await using var arrangeContext = Arrange();

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

	private static async Task<ActResult> ActAsync(McpContractFixtureBase.ArrangeContext arrangeContext)
	{
		return await AllureApi.Step("Act by invoking find-empty-iis-port through MCP", async () =>
		{
			IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(ToolName,
				because: "the find-empty-iis-port MCP tool must be discoverable via the get-tool-contract compact index on the lazy surface before the end-to-end call can be executed");

			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?>(),
				arrangeContext.CancellationTokenSource.Token);

			FindAvailableIisPortEnvelope execution = FindAvailableIisPortResultParser.Extract(callResult);
			return new ActResult(callResult, execution);
		});
	}

	private sealed record ActResult(
		CallToolResult CallResult,
		FindAvailableIisPortEnvelope Execution);
}
