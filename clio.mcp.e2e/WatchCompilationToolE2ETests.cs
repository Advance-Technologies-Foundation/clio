using System.Text.RegularExpressions;
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
/// End-to-end tests for the watch-compilation MCP tool. The tool is feature-toggled
/// (<c>watch-compilation</c>) and off by default; <see cref="WatchCompilationE2EGate"/> skips these
/// fixtures rather than failing them when the feature is disabled on the configured stand.
/// </summary>
[TestFixture]
[Category(WatchCompilationE2EGate.CategoryName)]
[AllureNUnit]
[AllureFeature("watch-compilation")]
[NonParallelizable]
public sealed class WatchCompilationToolE2ETests {
	private const string ToolName = WatchCompilationTool.WatchCompilationToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies watch-compilation is discoverable via the get-tool-contract compact index (requires the feature toggle to be enabled).")]
	[AllureTag(ToolName)]
	[AllureName("watch-compilation is discoverable on the lazy surface of the clio MCP server")]
	public async Task WatchCompilation_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		// ArrangeAsync already Assert.Ignores when the watch-compilation feature is disabled, so this
		// test only runs against a server that registered the gated tool. On the lazy surface even an
		// ENABLED gated tool is never resident in tools/list - discoverability is asserted through the
		// union of tools/list and the get-tool-contract compact index.
		await using ArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "the watch-compilation tool must be discoverable via the get-tool-contract compact index when the watch-compilation feature is enabled");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes watch-compilation with an invalid environment name, and verifies a readable structured failure is returned.")]
	[AllureTag(ToolName)]
	[AllureName("watch-compilation reports invalid environment failures")]
	public async Task WatchCompilation_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string invalidEnvironmentName = $"missing-watch-compilation-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext, invalidEnvironmentName, giveUpAfterSeconds: null);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "invalid environment failures should be returned as normal command execution envelopes");
		execution.ExitCode.Should().Be(1,
			because: $"watch-compilation routes through the shared BaseTool resolver catch, which returns FromResolverError (ExitCode=1) for expected environment-resolution failures, not the unexpected-exception code -1. Actual execution: {DescribeExecution(execution)}");
		string combinedOutput = string.Join(
			Environment.NewLine,
			(execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should help a human understand that the requested environment is not registered");
	}

	[Test]
	[Description("Starts the real clio MCP server and invokes watch-compilation against a reachable, currently-idle sandbox environment, verifying it settles quickly with exit code 0.")]
	[AllureTag(ToolName)]
	[AllureName("watch-compilation succeeds quickly when the environment is already idle")]
	public async Task WatchCompilation_Should_SucceedQuickly_WhenEnvironmentIsIdle() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		WatchCompilationE2EGate.SkipIfFeatureDisabled(settings);
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run the watch-compilation idle-environment E2E.");
		}
		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"watch-compilation MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		ArrangeContext arrangeContext = new(session, cancellationTokenSource);

		// Act
		// A short give-up-after keeps this bounded even if the sandbox happens to be mid-compile from
		// an unrelated concurrent test run; the fast-idle path is what this test actually verifies.
		CallToolResult callResult = await CallToolAsync(arrangeContext, environmentName!, giveUpAfterSeconds: 30);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"a valid watch-compilation request against a reachable environment should return a normal MCP tool result. Actual execution: {DescribeExecution(execution)}");
		execution.ExitCode.Should().BeOneOf([0, 2],
			because: $"an idle sandbox should settle immediately (0); a busy sandbox should at worst give up after the short deadline (2), never fail or error. Actual execution: {DescribeExecution(execution)}");
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		WatchCompilationE2EGate.SkipIfFeatureDisabled(settings);
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private static async Task<CallToolResult> CallToolAsync(
		ArrangeContext arrangeContext, string environmentName, int? giveUpAfterSeconds) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = environmentName
		};
		if (giveUpAfterSeconds is not null) {
			args["give-up-after-seconds"] = giveUpAfterSeconds.Value;
		}
		return await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
	}

	private static string DescribeExecution(CommandExecutionEnvelope execution) {
		string messages = execution.Output is null
			? "<no messages>"
			: string.Join(" | ", execution.Output.Select(message => $"{message.MessageType}: {message.Value}"));
		return $"ExitCode={execution.ExitCode}; Messages={messages}";
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
