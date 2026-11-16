using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Results;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Runs a freshly built process on a stand that will not interpret it until it is compiled.
/// </summary>
/// <remarks>
/// A stand on a locally built core stamps every new process with <c>CreatedInVersion</c> 1.0.0.0, which the flow
/// engine does not interpret, so the first run is refused with <c>ProcessSchemaIsNotPublishedException</c> although
/// create-business-process says no compile is needed. A released core interprets the process and the first run
/// simply succeeds; nothing is compiled there. A compile reloads the runtime for every user of the stand, so under
/// TeamCity or GitHub Actions - one shared stand behind the whole suite - the test is ignored instead
/// (docs/knowledge/Tests/no-mcp-e2e-fixture-may-restart-or-recompile-the-shared-instance.md). The compile is a
/// process-name one on a .NET Framework host, which reloads by itself; a .NET host would also need a restart, which
/// this helper never does.
/// </remarks>
internal static class ProcessPublishE2ESupport {

	private const string NotPublishedCode = "ProcessSchemaIsNotPublishedException";
	private const int RunAttemptsAfterCompile = 12;
	private static readonly TimeSpan RunRetryDelay = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan CompilePollDelay = TimeSpan.FromSeconds(10);

	// The in-progress notice names the operation as "(operation-id '<id>')"; System.Text.Json writes the apostrophes
	// as ', so both spellings are accepted.
	private static readonly Regex OperationId = new(@"operation-id (?:'|\\u0027)([0-9a-fA-F-]+)(?:'|\\u0027)",
		RegexOptions.CultureInvariant);

	/// <summary>
	/// Runs the process; when the stand refuses it as not published, compiles it by name, waits for the compile,
	/// and runs it again while the application reloads.
	/// </summary>
	/// <param name="context">The open session.</param>
	/// <param name="processName">The process code to compile.</param>
	/// <param name="run">One run-process call.</param>
	/// <returns>The first answer that is not a not-published refusal, or the last answer after the retries.</returns>
	internal static async Task<RunProcessEnvelope> RunCompilingWhenRequiredAsync(ProcessDesignerArrangeContext context,
		string processName, Func<Task<RunProcessEnvelope>> run) {
		RunProcessEnvelope answer = await run();
		if (answer.Status != RunProcessTool.NotStartedStatus
				|| answer.Error?.Contains(NotPublishedCode, StringComparison.Ordinal) != true) {
			return answer;
		}
		if (TeamCityRunGuard.IsRunningUnderTeamCityOrGitHubActions()) {
			Assert.Ignore($"'{context.EnvironmentName}' does not interpret new processes (a locally built core), so "
				+ "running one needs a compile, which reloads the shared stand: not done in an automated run.");
		}
		(bool succeeded, string compileText) = await CompileAndWaitAsync(context, processName);
		if (!succeeded) {
			Assert.Fail($"The compile '{processName}' needs before its first run on this stand failed: {compileText}");
		}
		// The application reloads after the compile. A run in that window answers not-started, or is refused before
		// any launch (no status: reading the process timed out); neither ran anything, so both are retried.
		for (int attempt = 0; attempt < RunAttemptsAfterCompile; attempt++) {
			answer = await run();
			if (answer.Status is not null && answer.Status != RunProcessTool.NotStartedStatus) {
				return answer;
			}
			await Task.Delay(RunRetryDelay, context.CancellationTokenSource.Token);
		}
		return answer;
	}

	private static async Task<(bool Succeeded, string Text)> CompileAndWaitAsync(ProcessDesignerArrangeContext context,
		string processName) {
		CallToolResult compiled = await ProcessDesignerE2EArrange.CallToolAsync(context,
			CompileCreatioTool.CompileCreatioToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			});
		string answer = compiled.Content.OfType<TextContentBlock>().First().Text;
		Match inProgress = OperationId.Match(answer);
		if (!inProgress.Success) {
			return (answer.Contains("\"exit-code\":0", StringComparison.Ordinal), answer);
		}
		while (true) {
			CallToolResult status = await context.Session.CallToolAsync(CompileStatusTool.CompileStatusToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = context.EnvironmentName,
						["operation-id"] = inProgress.Groups[1].Value
					}
				},
				context.CancellationTokenSource.Token);
			string text = status.Content.OfType<TextContentBlock>().First().Text;
			using JsonDocument payload = JsonDocument.Parse(text);
			string? state = payload.RootElement.GetProperty("status").GetString();
			if (state != "running") {
				return (state == "succeeded", text);
			}
			await Task.Delay(CompilePollDelay, context.CancellationTokenSource.Token);
		}
	}
}
