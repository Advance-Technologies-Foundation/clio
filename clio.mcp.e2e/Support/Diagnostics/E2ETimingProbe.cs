using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Clio.Mcp.E2E.Support.Diagnostics;

/// <summary>
/// Counts the two fixed costs the suite pays outside test logic — MCP server process lifecycles and
/// out-of-process clio CLI invocations — and reports them once at the end of the run.
/// </summary>
/// <remarks>
/// The per-test durations TeamCity records cover only test bodies, so the arrange cost of the suite
/// (a server process per fixture, a <c>ping-app</c> per test) is invisible in the build statistics and
/// can only be estimated. These counters make it a measured number, which is what any decision about
/// restructuring the fixtures has to be based on.
/// </remarks>
internal static class E2ETimingProbe {

	private static int _sessionStarts;
	private static long _sessionStartMilliseconds;
	private static int _sessionDisposals;
	private static long _sessionDisposeMilliseconds;
	private static readonly ConcurrentDictionary<string, (int Count, long Milliseconds)> CliInvocations = new();

	/// <summary>Records one completed MCP server process start.</summary>
	/// <param name="elapsed">Wall time the start took.</param>
	public static void RecordSessionStart(TimeSpan elapsed) {
		Interlocked.Increment(ref _sessionStarts);
		Interlocked.Add(ref _sessionStartMilliseconds, (long)elapsed.TotalMilliseconds);
	}

	/// <summary>Records one completed MCP server process disposal.</summary>
	/// <param name="elapsed">Wall time the disposal took.</param>
	public static void RecordSessionDispose(TimeSpan elapsed) {
		Interlocked.Increment(ref _sessionDisposals);
		Interlocked.Add(ref _sessionDisposeMilliseconds, (long)elapsed.TotalMilliseconds);
	}

	/// <summary>Records one out-of-process clio CLI invocation.</summary>
	/// <param name="verb">The first CLI argument, used as the grouping key.</param>
	/// <param name="elapsed">Wall time the invocation took.</param>
	public static void RecordCliInvocation(string verb, TimeSpan elapsed) {
		CliInvocations.AddOrUpdate(
			verb,
			_ => (1, (long)elapsed.TotalMilliseconds),
			(_, existing) => (existing.Count + 1, existing.Milliseconds + (long)elapsed.TotalMilliseconds));
	}

	/// <summary>
	/// Records one MCP tool call made through the harness.
	/// </summary>
	/// <remarks>
	/// A handful of tool names cost a real Creatio compile — create-app, create-app-section, every
	/// schema mutation that publishes — and those calls, not the harness, are what the run's remaining
	/// time is made of. Counting them by name is the only way to answer "how many compiles does one run
	/// pay for" without reading every fixture.
	/// </remarks>
	/// <param name="toolName">Name of the tool invoked.</param>
	/// <param name="elapsed">Wall time the call took.</param>
	public static void RecordToolCall(string toolName, TimeSpan elapsed) {
		ToolCalls.AddOrUpdate(
			toolName,
			_ => (1, (long)elapsed.TotalMilliseconds),
			(_, existing) => (existing.Count + 1, existing.Milliseconds + (long)elapsed.TotalMilliseconds));
	}

	private static readonly ConcurrentDictionary<string, (int Count, long Milliseconds)> ToolCalls = new();

	/// <summary>
	/// Renders the collected counters as a human-readable block plus TeamCity build statistics, so the
	/// numbers are both visible in the build log and comparable across builds.
	/// </summary>
	/// <returns>The report text.</returns>
	public static string BuildReport() {
		StringBuilder report = new();
		report.AppendLine("[e2e-timing] fixed arrange cost of this run");
		report.AppendLine(Line("mcp server starts", _sessionStarts, _sessionStartMilliseconds));
		report.AppendLine(Line("mcp server disposals", _sessionDisposals, _sessionDisposeMilliseconds));
		long cliTotal = 0;
		int cliCount = 0;
		foreach ((string verb, (int Count, long Milliseconds) value) in CliInvocations.OrderByDescending(entry => entry.Value.Milliseconds)) {
			report.AppendLine(Line($"cli {verb}", value.Count, value.Milliseconds));
			cliTotal += value.Milliseconds;
			cliCount += value.Count;
		}
		report.AppendLine(Line("cli total", cliCount, cliTotal));
		long overall = _sessionStartMilliseconds + _sessionDisposeMilliseconds + cliTotal;
		report.AppendLine(Line("fixed cost total", _sessionStarts + _sessionDisposals + cliCount, overall));
		report.AppendLine(Statistic("e2eMcpSessionStarts", _sessionStarts));
		report.AppendLine(Statistic("e2eMcpSessionStartMs", _sessionStartMilliseconds));
		report.AppendLine(Statistic("e2eMcpSessionDisposeMs", _sessionDisposeMilliseconds));
		report.AppendLine(Statistic("e2eCliInvocations", cliCount));
		report.AppendLine(Statistic("e2eCliMs", cliTotal));
		report.AppendLine(Statistic("e2eFixedCostMs", overall));
		report.AppendLine("[e2e-timing] MCP tool calls, most expensive first");
		long toolTotal = 0;
		int toolCount = 0;
		foreach ((string toolName, (int Count, long Milliseconds) value) in ToolCalls.OrderByDescending(entry => entry.Value.Milliseconds)) {
			report.AppendLine(Line($"tool {toolName}", value.Count, value.Milliseconds));
			toolTotal += value.Milliseconds;
			toolCount += value.Count;
		}
		report.AppendLine(Line("tool calls total", toolCount, toolTotal));
		report.AppendLine(Statistic("e2eToolCalls", toolCount));
		report.AppendLine(Statistic("e2eToolMs", toolTotal));
		return report.ToString();
	}

	/// <summary>
	/// Writes the report to the test host's REAL standard output, bypassing the <see cref="Console"/>
	/// writer NUnit substitutes during a run — otherwise the text is swallowed and never reaches the
	/// build log, which is the only place the TeamCity service messages are read from.
	/// </summary>
	public static void WriteReportToProcessStandardOutput() {
		string report = Environment.NewLine + BuildReport();
		using (Stream standardOutput = Console.OpenStandardOutput()) {
			byte[] payload = Encoding.UTF8.GetBytes(report);
			standardOutput.Write(payload, 0, payload.Length);
			standardOutput.Flush();
		}
		// stdout of the test host is not always relayed by `dotnet test`; the file next to the test
		// assembly is the channel that always survives and can be published as a build artifact.
		try {
			File.WriteAllText(
				Path.Combine(AppContext.BaseDirectory, "e2e-timing.txt"),
				report);
		} catch (IOException) {
		} catch (UnauthorizedAccessException) {
		}
	}

	private static string Line(string label, long count, long milliseconds) =>
		string.Format(
			CultureInfo.InvariantCulture,
			"[e2e-timing]   {0,-24} count={1,5}  total={2,8:F1}s  mean={3,6:F0}ms",
			label,
			count,
			milliseconds / 1000d,
			count == 0 ? 0 : milliseconds / (double)count);

	private static string Statistic(string key, long value) =>
		string.Format(CultureInfo.InvariantCulture, "##teamcity[buildStatisticValue key='{0}' value='{1}']", key, value);
}
