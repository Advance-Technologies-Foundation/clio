using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the compile-creatio MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("compile-configuration")]
public sealed class CompileCreatioToolE2ETests
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
		McpE2ESettings settings = TestConfiguration.Load();
		await using CompileCreatioArrangeContext arrangeContext = await ArrangeAsync(settings);
		string invalidEnvironmentName = $"missing-compile-env-{Guid.NewGuid():N}";

		// Act
		CompileCreatioActResult actResult = await ActAsync(arrangeContext, invalidEnvironmentName);

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMentionsEnvironment(actResult, invalidEnvironmentName);
	}

	[AllureStep("Arrange compile-creatio MCP session")]
	private static async Task<CompileCreatioArrangeContext> ArrangeAsync(McpE2ESettings settings)
	{
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new CompileCreatioArrangeContext(session, cancellationTokenSource);
	}

	[AllureStep("Act by invoking compile-creatio through MCP")]
	private static async Task<CompileCreatioActResult> ActAsync(
		CompileCreatioArrangeContext arrangeContext,
		string environmentName)
	{
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the compile-creatio MCP tool must be advertised before the end-to-end call can be executed");

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

	private sealed record CompileCreatioArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable
	{
		public async ValueTask DisposeAsync()
		{
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record CompileCreatioActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
