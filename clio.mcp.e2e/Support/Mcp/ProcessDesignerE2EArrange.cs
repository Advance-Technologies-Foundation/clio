using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// The arrange half every process-designer E2E fixture repeats: resolve a fresh clio, require a reachable
/// sandbox environment, open one MCP session bounded by a token, and call a tool through it.
/// </summary>
/// <remarks>
/// Lifted out of the per-element fixtures because copying it copied more than code: the Formula fixture
/// inherited the Approval fixture's skip message ("to run the Approval MCP E2E tests") and its comment about
/// "four call sites" on a fixture with three, so a developer who hit the skip was sent to the wrong feature.
/// The pieces most worth owning once are the ones a reader cannot see are shared: the three-minute token and
/// the rule that an unreachable environment IGNORES rather than fails.
/// <para>The subject and the minimum package version are parameters rather than constants, because they are the
/// only two things the message must say and the only two that differ per fixture.</para>
/// </remarks>
internal static class ProcessDesignerE2EArrange {

	/// <summary>
	/// Opens a session against the configured sandbox, or ignores the test when none is configured or reachable.
	/// </summary>
	/// <param name="subject">What the skip message should call these tests, e.g. "Formula".</param>
	/// <param name="minimumPackageVersion">The CrtProcessBuilder the environment must carry, named in the skip.</param>
	internal static async Task<ProcessDesignerArrangeContext> StartAsync(string subject,
			string minimumPackageVersion) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore($"Configure McpE2E:Sandbox:EnvironmentName (with a CrtProcessBuilder "
				+ $"{minimumPackageVersion} or later) to run the {subject} MCP E2E tests.");
		}
		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName)) {
			Assert.Ignore($"{subject} MCP E2E requires a reachable configured sandbox environment. "
				+ $"'{environmentName}' was not reachable.");
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ProcessDesignerArrangeContext(session, cancellationTokenSource, environmentName);
	}

	/// <summary>Calls a tool, first asserting it is discoverable — a missing tool is a different failure.</summary>
	internal static async Task<CallToolResult> CallToolAsync(ProcessDesignerArrangeContext context,
			string toolName, Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the tool must be discoverable before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	/// <summary>Reads a process back by code through <c>describe-business-process</c>.</summary>
	internal static async Task<CallToolResult> DescribeAsync(ProcessDesignerArrangeContext context,
			string processCode) =>
		await context.Session.CallToolAsync(
			Clio.Command.McpServer.Tools.ProcessDesigner.DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processCode
				}
			},
			context.CancellationTokenSource.Token);

}

/// <summary>One MCP session plus the environment it is bound to, disposed together.</summary>
internal sealed record ProcessDesignerArrangeContext(
	McpServerSession Session,
	CancellationTokenSource CancellationTokenSource,
	string EnvironmentName) : IAsyncDisposable {

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		await Session.DisposeAsync();
		CancellationTokenSource.Dispose();
	}
}
