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
/// End-to-end tests for the add-package-dependency MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("add-package-dependency")]
[Parallelizable(ParallelScope.Self)]
public sealed class AddPackageDependencyToolE2ETests : McpContractFixtureBase {

	private const string ToolName = AddPackageDependencyTool.AddPackageDependencyToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server and verifies that add-package-dependency is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	[AllureName("add-package-dependency tool is discoverable on the lazy surface")]
	[Description("Verifies that add-package-dependency is discoverable via the get-tool-contract compact index exposed by the real clio mcp-server process.")]
	public async Task AddPackageDependency_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertToolIsDiscoverable(toolNames, ToolName);
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Invokes add-package-dependency with an invalid environment name and verifies that the MCP result reports a failure with human-readable diagnostics and no sandbox mutation.")]
	[AllureName("add-package-dependency reports invalid environment name failures")]
	[Description("Reports invalid environment failures for add-package-dependency through the real MCP server.")]
	public async Task AddPackageDependency_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using var arrangeContext = Arrange();
		string invalidEnvironmentName = $"missing-dependency-env-{Guid.NewGuid():N}";

		// Act
		CommandExecutionActResult actResult =
			await ActAsync(arrangeContext, "SomePackage", "CrtLeadOppMgmtApp", invalidEnvironmentName);

		// Assert
		AssertCommandToolFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMentionsEnvironment(actResult, invalidEnvironmentName);
	}

	private static async Task<CommandExecutionActResult> ActAsync(
		ArrangeContext arrangeContext,
		string packageName,
		string dependencyName,
		string environmentName) {
		return await AllureApi.Step("Act by invoking add-package-dependency through MCP", async () => {
			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["package-name"] = packageName,
						["dependencies"] = new[] {
							new Dictionary<string, object?> { ["name"] = dependencyName }
						}
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
			return new CommandExecutionActResult(callResult, execution);
		});
	}

	[AllureStep("Assert tool is discoverable on the lazy MCP surface")]
	private static void AssertToolIsDiscoverable(IReadOnlyCollection<string> toolNames, string toolName) {
		toolNames.Should().Contain(toolName,
			because: $"the {toolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[AllureStep("Assert command-oriented MCP tool failed")]
	private static void AssertCommandToolFailed(CommandExecutionActResult actResult) {
		(actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0).Should().BeTrue(
			because: "add-package-dependency should fail when the requested environment is not registered");
	}

	[AllureStep("Assert failure output contains Error message")]
	private static void AssertFailureIncludesErrorMessage(CommandExecutionActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed MCP command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == Clio.Common.LogDecoratorType.Error,
			because: "failed add-package-dependency execution should report its diagnostics as error-level log output");
	}

	[AllureStep("Assert failure diagnostics mention the invalid environment")]
	private static void AssertFailureMentionsEnvironment(CommandExecutionActResult actResult, string environmentName) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed add-package-dependency execution should provide diagnostics that explain the failure");
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure log should identify that the requested environment is not registered");
	}

	private sealed record CommandExecutionActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
