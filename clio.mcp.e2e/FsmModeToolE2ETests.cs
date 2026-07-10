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

/// <summary>
/// End-to-end tests for FSM mode MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("fsm")]
[NonParallelizable]
public sealed class FsmModeToolE2ETests : McpContractFixtureBase
{
	private const string GetToolName = FsmModeTool.GetFsmModeToolName;

	[Category("McpE2E.Sandbox")]
	[Test]
	[AllureTag(GetToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes get-fsm-mode for the configured sandbox environment, and verifies that the structured FSM payload is returned from the live Creatio instance.")]
	[AllureName("Get FSM mode returns live sandbox status")]
	[Description("Returns the current FSM mode for the configured sandbox environment through the real MCP server and live Creatio response.")]
	public async Task GetFsmMode_Should_Return_Live_Sandbox_Status()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using var arrangeContext = Arrange();

		// Act
		CallToolResult callResult = await ActGetAsync(arrangeContext, settings.Sandbox.EnvironmentName!);
		FsmModeStatusEnvelope status = FsmModeStatusResultParser.Extract(callResult);

		// Assert
		AssertStatusToolSucceeded(callResult);
		AssertStructuredStatusReturned(status, settings.Sandbox.EnvironmentName!);
		AssertStatusShapeMatchesMode(status);
	}

	private static async Task<CallToolResult> ActGetAsync(ArrangeContext arrangeContext, string environmentName)
	{
		return await AllureApi.Step("Act by invoking get-fsm-mode through MCP", async () =>
		{
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(GetToolName,
				because: "the get-fsm-mode MCP tool must be discoverable via the get-tool-contract compact index on the lazy surface before the end-to-end call can be executed");

			return await arrangeContext.Session.CallToolAsync(
				GetToolName,
				new Dictionary<string, object?> { ["environmentName"] = environmentName },
				arrangeContext.CancellationTokenSource.Token);
		});
	}

	[AllureStep("Assert get-fsm-mode call succeeded")]
	private static void AssertStatusToolSucceeded(CallToolResult callResult)
	{
		callResult.IsError.Should().NotBeTrue(
			because: "get-fsm-mode should return a normal MCP result for a registered sandbox environment");
	}

	[AllureStep("Assert structured sandbox FSM status is returned")]
	private static void AssertStructuredStatusReturned(FsmModeStatusEnvelope status, string environmentName)
	{
		status.EnvironmentName.Should().Be(environmentName,
			because: "the live MCP result should preserve the requested sandbox environment name");
		status.Mode.Should().MatchRegex("^(on|off)$",
			because: "the live MCP result should report FSM mode as either on or off");
	}

	[AllureStep("Assert live FSM status shape matches the reported mode")]
	private static void AssertStatusShapeMatchesMode(FsmModeStatusEnvelope status)
	{
		if (string.Equals(status.Mode, "on", StringComparison.OrdinalIgnoreCase))
		{
			status.UseStaticFileContent.Should().BeFalse(
				because: "FSM on should correspond to useStaticFileContent=false in the live payload");
			status.StaticFileContent.Should().BeNull(
				because: "FSM on should correspond to staticFileContent=null in the live payload");
			return;
		}

		status.UseStaticFileContent.Should().BeTrue(
			because: "FSM off should correspond to useStaticFileContent=true in the live payload");
		status.StaticFileContent.Should().NotBeNull(
			because: "FSM off should correspond to populated staticFileContent in the live payload");
	}
}
