using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the compile-creatio MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("compile-configuration")]
[Parallelizable(ParallelScope.Self)]
public sealed class CompileCreatioToolE2ETests : McpContractFixtureBase
{
	private const string ToolName = CompileCreatioTool.CompileCreatioToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server, invokes compile-creatio with an invalid environment name, and verifies that the tool fails with readable diagnostics.")]
	[AllureName("Compile Creatio reports invalid environment failures")]
	[Description("Reports invalid environment failures for compile-creatio through the real MCP server.")]
	public async Task CompileCreatio_Should_Report_Invalid_Environment_Failure()
	{
		// Arrange
		await using var arrangeContext = Arrange();
		string invalidEnvironmentName = $"missing-compile-env-{Guid.NewGuid():N}";

		// Act
		CompileCreatioActResult actResult = await ActAsync(arrangeContext, invalidEnvironmentName);

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMentionsEnvironment(actResult, invalidEnvironmentName);
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server, compiles an invalid environment (which fails fast), then polls compile-status for the same environment and verifies the failed operation is queryable.")]
	[AllureName("Compile Creatio failure is queryable via compile-status")]
	[Description("A compile-creatio failure is tracked and queryable through compile-status via the real MCP server.")]
	public async Task CompileCreatio_Should_RecordFailedOperation_QueryableViaCompileStatus()
	{
		// Arrange
		await using var arrangeContext = Arrange();
		string invalidEnvironmentName = $"missing-compile-status-env-{Guid.NewGuid():N}";

		// Act
		await ActAsync(arrangeContext, invalidEnvironmentName);
		CompileStatusResponse status = await ActStatusAsync(arrangeContext, invalidEnvironmentName);

		// Assert
		status.Success.Should().BeTrue(
			because: "looking up a tracked operation's status is itself a successful lookup, regardless of the compile's own outcome");
		status.Status.Should().Be("failed",
			because: "the invalid-environment compile-creatio call must have finalized the tracked operation as failed, not left it running");
		status.EnvironmentName.Should().Be(invalidEnvironmentName,
			because: "the status response must identify which environment it describes");
	}

	[Test]
	[AllureTag(CompileStatusTool.CompileStatusToolName)]
	[AllureDescription("Starts the real clio MCP server and queries compile-status for an environment that never ran compile-creatio, verifying a not-found (not an error) result.")]
	[AllureName("Compile Status reports not-found for an untracked environment")]
	[Description("compile-status reports not-found, not an error, for an environment with no tracked compile-creatio operation.")]
	public async Task CompileStatus_Should_ReturnNotFound_ForNeverCompiledEnvironment()
	{
		// Arrange
		await using var arrangeContext = Arrange();
		string neverCompiledEnvironmentName = $"never-compiled-env-{Guid.NewGuid():N}";

		// Act
		CompileStatusResponse status = await ActStatusAsync(arrangeContext, neverCompiledEnvironmentName);

		// Assert
		status.Success.Should().BeTrue(because: "an empty history is a legitimate state, not a tool error");
		status.Status.Should().Be("not-found");
	}

	private static async Task<CompileStatusResponse> ActStatusAsync(
		ArrangeContext arrangeContext,
		string environmentName)
	{
		return await AllureApi.Step("Act by invoking compile-status through MCP", async () =>
		{
			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				CompileStatusTool.CompileStatusToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			return EntitySchemaStructuredResultParser.Extract<CompileStatusResponse>(callResult);
		});
	}

	private static async Task<CompileCreatioActResult> ActAsync(
		ArrangeContext arrangeContext,
		string environmentName)
	{
		return await AllureApi.Step("Act by invoking compile-creatio through MCP", async () =>
		{
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(ToolName,
				because: "the compile-creatio MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
			return new CompileCreatioActResult(callResult, execution);
		});
	}

	[AllureStep("Assert compile-creatio tool call failed")]
	private static void AssertToolCallFailed(CompileCreatioActResult actResult)
	{
		(actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0).Should().BeTrue(
			because: "invalid compile-creatio requests should fail instead of succeeding silently");
	}

	[AllureStep("Assert compile-creatio failure output contains Error message")]
	private static void AssertFailureIncludesErrorMessage(CompileCreatioActResult actResult)
	{
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed compile-creatio execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "failed compile-creatio execution should report error-level diagnostics");
	}

	[AllureStep("Assert compile-creatio failure mentions the invalid environment")]
	private static void AssertFailureMentionsEnvironment(CompileCreatioActResult actResult, string environmentName)
	{
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure should help a human understand that the requested environment is not registered");
	}

	private sealed record CompileCreatioActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
