using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the unlock-for-hotfix and finish-hotfix MCP tools.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("pkg-hotfix")]
[Parallelizable(ParallelScope.Self)]
public sealed class PackageHotfixToolE2ETests : McpContractFixtureBase {

	private const string UnlockToolName = PackageHotfixTool.UnlockForHotfixToolName;
	private const string FinishToolName = PackageHotfixTool.FinishHotfixToolName;

	[Test]
	[AllureTag(UnlockToolName)]
	[AllureDescription("Starts the real clio MCP server and verifies that unlock-for-hotfix is discoverable via the get-tool-contract compact index.")]
	[AllureName("unlock-for-hotfix tool is discoverable on the lazy surface")]
	[Description("Verifies that unlock-for-hotfix is discoverable via the get-tool-contract compact index of the real clio mcp-server process.")]
	public async Task UnlockForHotfix_Should_Be_Advertised_By_McpServer() {
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertToolIsDiscoverable(toolNames, UnlockToolName);
	}

	[Test]
	[AllureTag(FinishToolName)]
	[AllureDescription("Starts the real clio MCP server and verifies that finish-hotfix is discoverable via the get-tool-contract compact index.")]
	[AllureName("finish-hotfix tool is discoverable on the lazy surface")]
	[Description("Verifies that finish-hotfix is discoverable via the get-tool-contract compact index of the real clio mcp-server process.")]
	public async Task FinishHotfix_Should_Be_Advertised_By_McpServer() {
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertToolIsDiscoverable(toolNames, FinishToolName);
	}

	[Test]
	[AllureTag(UnlockToolName)]
	[AllureDescription("Invokes unlock-for-hotfix with an invalid environment name and verifies that the MCP result reports a failure with human-readable diagnostics.")]
	[AllureName("unlock-for-hotfix reports invalid environment name failures")]
	[Description("Reports invalid environment failures for unlock-for-hotfix through the real MCP server.")]
	public async Task UnlockForHotfix_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using var arrangeContext = Arrange();
		string invalidEnvironmentName = $"missing-hotfix-env-{Guid.NewGuid():N}";

		// Act
		CommandExecutionActResult actResult = await ActAsync(arrangeContext, UnlockToolName, "SomePackage", invalidEnvironmentName);

		// Assert
		AssertCommandToolFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMentionsEnvironment(actResult, invalidEnvironmentName);
	}

	[Test]
	[AllureTag(FinishToolName)]
	[AllureDescription("Invokes finish-hotfix with an invalid environment name and verifies that the MCP result reports a failure with human-readable diagnostics.")]
	[AllureName("finish-hotfix reports invalid environment name failures")]
	[Description("Reports invalid environment failures for finish-hotfix through the real MCP server.")]
	public async Task FinishHotfix_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using var arrangeContext = Arrange();
		string invalidEnvironmentName = $"missing-hotfix-env-{Guid.NewGuid():N}";

		// Act
		CommandExecutionActResult actResult = await ActAsync(arrangeContext, FinishToolName, "SomePackage", invalidEnvironmentName);

		// Assert
		AssertCommandToolFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMentionsEnvironment(actResult, invalidEnvironmentName);
	}

	private static async Task<CommandExecutionActResult> ActAsync(
		ArrangeContext arrangeContext,
		string toolName,
		string packageName,
		string environmentName) {
		return await AllureApi.Step("Act by invoking hotfix tool through MCP", async () => {
			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				toolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["package-name"] = packageName,
						["environment-name"] = environmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
			return new CommandExecutionActResult(callResult, execution);
		});
	}

	[AllureStep("Assert tool is discoverable on the lazy surface")]
	private static void AssertToolIsDiscoverable(IReadOnlyCollection<string> toolNames, string toolName) {
		toolNames.Should().Contain(toolName,
			because: $"the {toolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[AllureStep("Assert command-oriented MCP tool failed")]
	private static void AssertCommandToolFailed(CommandExecutionActResult actResult) {
		(actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0).Should().BeTrue(
			because: "hotfix tool should fail when the requested environment is not registered");
	}

	[AllureStep("Assert failure output contains Error message")]
	private static void AssertFailureIncludesErrorMessage(CommandExecutionActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed MCP command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == Clio.Common.LogDecoratorType.Error,
			because: "failed hotfix execution should report its diagnostics as error-level log output");
	}

	[AllureStep("Assert failure diagnostics mention the invalid environment")]
	private static void AssertFailureMentionsEnvironment(CommandExecutionActResult actResult, string environmentName) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed hotfix execution should provide diagnostics that explain the failure");
		// The hotfix tools are hidden long-tail tools routed through the clio-run executor, so an
		// invocation-layer failure may also surface as the wrapped "Error: tool '<name>' failed:" text.
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}|environment.*not.*found|not found|error occurred invoking|tool '[^']+' failed)",
			because: "the failure log should identify that the requested environment is not registered");
	}

	private sealed record CommandExecutionActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
