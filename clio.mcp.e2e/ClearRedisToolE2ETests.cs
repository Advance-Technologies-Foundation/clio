using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using System.Text.RegularExpressions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the clear-redis MCP tool.
/// </summary>
/// <remarks>
/// clear-redis is a thin remote command: clio POSTs the Creatio <c>ClearRedisDb</c> route and Creatio
/// flushes its own Redis — clio never connects to Redis itself. The clio-side contract (the tool
/// forwards the right options, and the command POSTs the <c>ClearRedisDb</c> route) is covered by unit
/// tests (<c>ClearRedisToolTests</c>, <c>RedisCommandTests</c>), so no live Redis is needed for it.
/// These e2e tests cover the env-free failure paths (unknown environment name, unreachable URL), which
/// fail before any live Redis is touched and run everywhere via <see cref="ArrangeWithoutRedisAsync"/>.
/// The former seed-then-verify happy path was removed (ENG-91829): its only unique value over the unit
/// tests was observing Creatio's own Redis flush — a backend behaviour that requires a runner-reachable
/// sandbox Redis the CI agents cannot reach.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature("clear-redis-db")]
public sealed class ClearRedisToolE2ETests {
	private const string EnvironmentToolName = ClearRedisTool.ClearRedisByEnvironmentName;
	private const string CredentialsToolName = ClearRedisTool.ClearRedisByCredentialsToolName;

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[AllureTag(EnvironmentToolName)]
	[AllureDescription("Invokes clear-redis with a non-existent environment name and verifies that the MCP result reports a failure with human-readable diagnostics.")]
	[AllureName("Clear Redis Tool reports invalid environment name failures")]
	public async Task ClearRedis_Should_Report_Failure_When_Environment_Name_Is_Invalid() {
		// Arrange — env-free: the unknown-environment lookup fails before any Redis connect.
		await using ClearRedisArrangeContext arrangeContext = await ArrangeWithoutRedisAsync();
		string invalidEnvironmentName = $"{arrangeContext.SandboxContext.EnvironmentName}-missing";

		// Act
		ClearRedisActResult actResult = await ActWithEnvironmentNameAsync(arrangeContext, invalidEnvironmentName);

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureMessageMentionsInvalidEnvironment(actResult, invalidEnvironmentName);
		AssertFailureIncludesErrorMessage(actResult);
	}

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[AllureTag(CredentialsToolName)]
	[AllureDescription("Invokes clear-redis-by-credentials with an invalid URL and verifies that the MCP result reports a failure with human-readable diagnostics.")]
	[AllureName("Clear Redis Tool reports invalid URL failures")]
	public async Task ClearRedisByCredentials_Should_Report_Failure_When_Url_Is_Invalid() {
		// Arrange — env-free: the deliberately unreachable port fails the TCP connect, no live Redis needed.
		await using ClearRedisArrangeContext arrangeContext = await ArrangeWithoutRedisAsync();
		UriBuilder invalidUrl = new(arrangeContext.SandboxContext.Uri) {
			Port = 49999
		};

		// Act
		ClearRedisActResult actResult = await ActWithCredentialsAsync(
			arrangeContext,
			arrangeContext.SandboxContext.Password,
			invalidUrl.Uri.ToString(),
			isNetCore: true);

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertFailureMessageMentionsInvalidUrl(actResult);
	}

	// Lightweight arrange for the invalid-input tests: uses synthetic connection details and starts the
	// MCP server, but does NOT require sandbox configuration or connect to a real Redis. Both negative
	// cases fail before any live Redis is touched (unknown-environment lookup; deliberately unreachable
	// URL), so they are env-free (McpE2E.NoEnvironment).
	// No AllowDestructiveMcpTests gate: rejecting an invalid request mutates nothing.
	private async Task<ClearRedisArrangeContext> ArrangeWithoutRedisAsync() {
		return await AllureApi.Step("Arrange clear-redis invalid-input state (no sandbox Redis)", async () => {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			const string environmentName = "clear-redis-synthetic-env";
			SandboxEnvironmentContext sandboxContext = new(
				environmentName,
				Uri: "http://127.0.0.1:49998",
				Login: "Supervisor",
				Password: "Supervisor",
				IsNetCore: true,
				EnvironmentPath: string.Empty,
				ConnectionStringsPath: string.Empty,
				RedisConnectionString: string.Empty,
				DatabaseConnectionString: string.Empty);
			CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
			McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
			return new ClearRedisArrangeContext(sandboxContext, session, cancellationTokenSource);
		});
	}

	private static async Task<ClearRedisActResult> ActWithEnvironmentNameAsync(
		ClearRedisArrangeContext arrangeContext,
		string environmentName) {
		return await AllureApi.Step("Act by invoking clear-redis with invalid environment name", async () => {
			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				EnvironmentToolName,
				new Dictionary<string, object?> {
					["environmentName"] = environmentName
				},
				arrangeContext.CancellationTokenSource.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
			return new ClearRedisActResult(callResult, execution);
		});
	}

	private static async Task<ClearRedisActResult> ActWithCredentialsAsync(
		ClearRedisArrangeContext arrangeContext,
		string password,
		string? url = null,
		bool? isNetCore = null) {
		return await AllureApi.Step("Act by invoking clear-redis with explicit credentials", async () => {
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(CredentialsToolName,
				because: "the credentials clear-redis tool must be discoverable via the get-tool-contract compact index for credentials-path end-to-end coverage");

			Dictionary<string, object?> arguments = new() {
				["url"] = url ?? arrangeContext.SandboxContext.Uri,
				["userName"] = arrangeContext.SandboxContext.Login,
				["password"] = password
			};
			if (isNetCore.HasValue) {
				arguments["isNetCore"] = isNetCore.Value;
			}

			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				CredentialsToolName,
				arguments,
				arrangeContext.CancellationTokenSource.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
			return new ClearRedisActResult(callResult, execution);
		});
	}

	[AllureStep("Assert failed clear-redis request reported failure")]
	[AllureDescription("Assert that the clear-redis MCP tool reports failure instead of succeeding silently when the request is invalid")]
	private static void AssertToolCallFailed(ClearRedisActResult actResult) {
		bool failed = actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0;
		failed.Should().BeTrue(
			because: "clear-redis should fail when the requested input cannot be executed successfully");
	}

	[AllureStep("Assert failure diagnostics are human-readable")]
	[AllureDescription("Assert that the failure output explains the failed clear-redis invocation, ideally by naming the invalid environment or at minimum by identifying the failing MCP tool call")]
	private static void AssertFailureMessageMentionsInvalidEnvironment(ClearRedisActResult actResult, string invalidEnvironmentName) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? [])
			.Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed clear-redis execution should provide diagnostics that explain why the environment lookup failed");
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|clear-redis-by-environment|error occurred invoking)",
			because: "the failure log should help a human understand that the request failed, even when the MCP server surfaces a top-level tool invocation error instead of the underlying environment lookup message");
	}

	[AllureStep("Assert failure output contains error message type")]
	[AllureDescription("Assert that failed clear-redis execution emits at least one Error log message when execution output is available")]
	private static void AssertFailureIncludesErrorMessage(ClearRedisActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed MCP command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Error,
			because: "failed clear-redis execution should report its diagnostics as error-level log output");
	}

	[AllureStep("Assert failure diagnostics mention invalid URL")]
	[AllureDescription("Assert that the failure output identifies the failed credentials-based clear-redis invocation or the connection problem caused by the invalid URL")]
	private static void AssertFailureMessageMentionsInvalidUrl(ClearRedisActResult actResult) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? [])
			.Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed credentials-based clear-redis execution should provide diagnostics that explain why the URL could not be reached");
		combinedOutput.Should().MatchRegex(
			"(?is)(clear-redis-by-credentials|error occurred invoking|connection|refused|could not be reached|actively refused)",
			because: "the failure log should help a human understand that the credentials-based request failed because the target URL was invalid");
	}

	private sealed record ClearRedisArrangeContext(
		SandboxEnvironmentContext SandboxContext,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record ClearRedisActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
