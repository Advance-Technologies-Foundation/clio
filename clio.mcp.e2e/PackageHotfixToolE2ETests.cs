using System.Text.RegularExpressions;
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
/// End-to-end tests for the unlock-for-hotfix and finish-hotfix MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("pkg-hotfix")]
public sealed class PackageHotfixToolE2ETests {

	private const string UnlockToolName = PackageHotfixTool.UnlockForHotfixToolName;
	private const string FinishToolName = PackageHotfixTool.FinishHotfixToolName;

	[Test]
	[AllureTag(UnlockToolName)]
	[AllureDescription("Starts the real clio MCP server and verifies that unlock-for-hotfix is advertised in the tool list.")]
	[AllureName("unlock-for-hotfix tool is advertised by the MCP server")]
	[Description("Verifies that unlock-for-hotfix appears in the MCP tool advertisement from the real clio mcp-server process.")]
	public async Task UnlockForHotfix_Should_Be_Advertised_By_McpServer() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		await using PackageHotfixArrangeContext arrangeContext = await ArrangeAsync(settings);

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertToolIsAdvertised(tools, UnlockToolName);
	}

	[Test]
	[AllureTag(FinishToolName)]
	[AllureDescription("Starts the real clio MCP server and verifies that finish-hotfix is advertised in the tool list.")]
	[AllureName("finish-hotfix tool is advertised by the MCP server")]
	[Description("Verifies that finish-hotfix appears in the MCP tool advertisement from the real clio mcp-server process.")]
	public async Task FinishHotfix_Should_Be_Advertised_By_McpServer() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		await using PackageHotfixArrangeContext arrangeContext = await ArrangeAsync(settings);

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertToolIsAdvertised(tools, FinishToolName);
	}

	[Test]
	[AllureTag(UnlockToolName)]
	[AllureDescription("Invokes unlock-for-hotfix with an invalid environment name and verifies that the MCP result reports a failure with human-readable diagnostics.")]
	[AllureName("unlock-for-hotfix reports invalid environment name failures")]
	[Description("Reports invalid environment failures for unlock-for-hotfix through the real MCP server.")]
	public async Task UnlockForHotfix_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		await using PackageHotfixArrangeContext arrangeContext = await ArrangeAsync(settings);
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
		McpE2ESettings settings = TestConfiguration.Load();
		await using PackageHotfixArrangeContext arrangeContext = await ArrangeAsync(settings);
		string invalidEnvironmentName = $"missing-hotfix-env-{Guid.NewGuid():N}";

		// Act
		CommandExecutionActResult actResult = await ActAsync(arrangeContext, FinishToolName, "SomePackage", invalidEnvironmentName);

		// Assert
		AssertCommandToolFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMentionsEnvironment(actResult, invalidEnvironmentName);
	}

	[AllureStep("Arrange MCP server session")]
	private static async Task<PackageHotfixArrangeContext> ArrangeAsync(McpE2ESettings settings) {
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new PackageHotfixArrangeContext(session, cancellationTokenSource);
	}

	[AllureStep("Act by invoking hotfix tool through MCP")]
	private static async Task<CommandExecutionActResult> ActAsync(
		PackageHotfixArrangeContext arrangeContext,
		string toolName,
		string packageName,
		string environmentName) {
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
	}

	[AllureStep("Assert tool is advertised by MCP server")]
	private static void AssertToolIsAdvertised(IList<McpClientTool> tools, string toolName) {
		tools.Select(t => t.Name).Should().Contain(toolName,
			because: $"the {toolName} MCP tool must be advertised by the clio mcp-server process");
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
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure log should identify that the requested environment is not registered");
	}

	private sealed record PackageHotfixArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record CommandExecutionActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
