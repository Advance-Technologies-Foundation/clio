using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// SPIKE-ONLY diagnostic that decomposes the wall-clock cost of ONE <c>clio mcp-server</c> process
/// lifecycle on the CI agent. It exists to settle a single question: the suite pays a strikingly
/// CONSTANT ~11.5 s per lifecycle (sigma 0.03-0.11 s across four TeamCity builds on different agents,
/// versus 0.6 s on a developer machine), and two competing explanations fit that observation equally
/// well from the outside:
/// <list type="number">
/// <item>a fixed timer — the 10 s shutdown drain in <c>McpServerCommand.Execute</c>'s finally block
/// (<c>ComponentRegistryClient.DrainAsync</c> / <c>ITelemetryFlushScheduler.DrainAsync</c>) or the
/// harness's own 10 s <c>StdioClientTransportOptions.ShutdownTimeout</c>, tripped by a stalled
/// network call on an egress-restricted agent;</item>
/// <item>deterministic CPU work — runtime boot plus the MCP host's reflection-driven schema
/// generation for ~165 tool methods, which on one fixed agent repeats with very low variance.</item>
/// </list>
/// The two are separated by measuring the phases independently: a no-MCP-host baseline process, the
/// spawn-to-<c>initialize</c> window, the <c>tools/list</c> round trip, and the graceful
/// stdin-EOF-to-exit window. A large shutdown window proves the timer; a large spawn window with a
/// small shutdown window proves the CPU work.
/// <para>
/// Reports through <see cref="TestContext.Progress"/> so every number lands in the TeamCity build log
/// and never asserts on a duration — a slow agent must not turn this into a red build.
/// </para>
/// </summary>
[TestFixture]
[Category("E2E")]
[Category("McpE2E.Diagnostic")]
[NonParallelizable]
public sealed class McpLifecycleTimingDiagnosticsE2ETests {

	private const int LifecycleCycles = 5;
	private const int BaselineCycles = 3;
	private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan EgressProbeTimeout = TimeSpan.FromSeconds(5);

	[Test]
	[Description("Reports the per-phase wall-clock breakdown of a clio mcp-server lifecycle on the current agent so the fixed ~11.5 s per-lifecycle cost can be attributed to either the shutdown drain (a timer) or host startup (CPU work).")]
	public async Task McpServerLifecycle_Should_Report_PhaseBreakdown_For_Diagnosis() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ClioProcessDescriptor serverProcess = ClioExecutableResolver.Resolve(settings);
		ClioProcessDescriptor baselineProcess = ClioExecutableResolver.Resolve(settings, "help");
		Report("=== MCP LIFECYCLE TIMING DIAGNOSTICS ===");
		ReportEnvironment(settings);

		// Act
		await ProbeEgressAsync("academy.creatio.com", 443);
		await ProbeEgressAsync("caadt-telemetry.creatio.com", 443);
		List<double> baselineSeconds = [];
		for (int cycle = 1; cycle <= BaselineCycles; cycle++) {
			double elapsed = await MeasureBaselineProcessAsync(baselineProcess, settings);
			baselineSeconds.Add(elapsed);
			Report($"[baseline] cycle {cycle}/{BaselineCycles} no-mcp-host process total={elapsed:F2}s");
		}
		List<LifecycleSample> samples = [];
		for (int cycle = 1; cycle <= LifecycleCycles; cycle++) {
			LifecycleSample sample = await MeasureLifecycleAsync(serverProcess, settings, killInsteadOfGracefulExit: false);
			samples.Add(sample);
			Report($"[lifecycle] cycle {cycle}/{LifecycleCycles} {sample}");
		}
		LifecycleSample killed = await MeasureLifecycleAsync(serverProcess, settings, killInsteadOfGracefulExit: true);
		Report($"[lifecycle] kill-instead-of-EOF (isolates startup from shutdown) {killed}");

		// Assert
		ReportVerdict(baselineSeconds, samples, killed);
		samples.Should().HaveCount(LifecycleCycles,
			because: "every measured lifecycle must produce a sample for the phase breakdown to be interpretable");
	}

	[Test]
	[Description("Splits the harness-side session cost into McpServerSession.StartAsync and DisposeAsync, and re-measures dispose with a shortened StdioClientTransportOptions.ShutdownTimeout, to prove whether the ~10 s per lifecycle is that timeout rather than any work the clio server performs.")]
	public async Task HarnessSession_Should_Attribute_Cost_To_Start_Or_Dispose() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		Report("=== HARNESS SESSION ATTRIBUTION ===");

		// Act
		List<double> startSeconds = [];
		List<double> disposeSeconds = [];
		for (int cycle = 1; cycle <= BaselineCycles; cycle++) {
			using CancellationTokenSource startupCts = new(HandshakeTimeout);
			Stopwatch start = Stopwatch.StartNew();
			McpServerSession session = await McpServerSession.StartAsync(settings, startupCts.Token);
			start.Stop();
			await session.ListToolsAsync(startupCts.Token);
			Stopwatch dispose = Stopwatch.StartNew();
			await session.DisposeAsync();
			dispose.Stop();
			startSeconds.Add(start.Elapsed.TotalSeconds);
			disposeSeconds.Add(dispose.Elapsed.TotalSeconds);
			Report($"[harness] cycle {cycle}/{BaselineCycles} StartAsync={start.Elapsed.TotalSeconds:F2}s "
				+ $"DisposeAsync={dispose.Elapsed.TotalSeconds:F2}s");
		}
		List<double> shortenedDisposeSeconds = [];
		foreach (int shutdownSeconds in new[] { 1, 2 }) {
			double elapsed = await MeasureSdkDisposeAsync(settings, TimeSpan.FromSeconds(shutdownSeconds));
			shortenedDisposeSeconds.Add(elapsed);
			Report($"[harness] ShutdownTimeout={shutdownSeconds}s -> DisposeAsync={elapsed:F2}s");
		}

		// Assert
		double disposeMedian = Median([.. disposeSeconds]);
		Report($"[median] StartAsync={Median([.. startSeconds]):F2}s DisposeAsync={disposeMedian:F2}s");
		Report(disposeMedian >= 5
			? "[verdict] DISPOSE-DOMINATED -> the per-lifecycle cost is the client transport shutdown wait, "
				+ "not clio server work. Compare the shortened-timeout rows to confirm it scales with ShutdownTimeout."
			: "[verdict] Dispose is cheap -> look elsewhere for the per-lifecycle cost.");
		disposeSeconds.Should().HaveCount(BaselineCycles,
			because: "each cycle must contribute a dispose measurement for the attribution to be interpretable");
	}

	/// <summary>
	/// Builds a client over the same stdio transport the harness uses but with an explicit
	/// <see cref="StdioClientTransportOptions.ShutdownTimeout"/>, and returns how long disposing it takes.
	/// A dispose window that tracks the configured timeout proves the wait IS the timeout.
	/// </summary>
	private static async Task<double> MeasureSdkDisposeAsync(McpE2ESettings settings, TimeSpan shutdownTimeout) {
		ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(settings);
		StdioClientTransport transport = new(new StdioClientTransportOptions {
			Command = descriptor.Command,
			Arguments = [.. descriptor.Arguments],
			WorkingDirectory = descriptor.WorkingDirectory,
			EnvironmentVariables = settings.ProcessEnvironmentVariables,
			Name = "clio-mcp-e2e-diagnostics",
			ShutdownTimeout = shutdownTimeout
		}, NullLoggerFactory.Instance);
		using CancellationTokenSource cts = new(HandshakeTimeout);
		McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
		await client.ListToolsAsync(cancellationToken: cts.Token);
		Stopwatch dispose = Stopwatch.StartNew();
		await client.DisposeAsync();
		dispose.Stop();
		return dispose.Elapsed.TotalSeconds;
	}

	private static async Task<double> MeasureBaselineProcessAsync(
		ClioProcessDescriptor descriptor,
		McpE2ESettings settings) {
		using Process process = CreateProcess(descriptor, settings);
		Stopwatch stopwatch = Stopwatch.StartNew();
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		await process.WaitForExitAsync().WaitAsync(ExitTimeout);
		stopwatch.Stop();
		return stopwatch.Elapsed.TotalSeconds;
	}

	private static async Task<LifecycleSample> MeasureLifecycleAsync(
		ClioProcessDescriptor descriptor,
		McpE2ESettings settings,
		bool killInsteadOfGracefulExit) {
		using Process process = CreateProcess(descriptor, settings);
		Stopwatch stopwatch = Stopwatch.StartNew();
		process.Start();
		// stderr is drained on a background reader: a full pipe buffer would otherwise block the
		// child and be misread as startup cost.
		Task<string> standardError = process.StandardError.ReadToEndAsync();
		await WriteLineAsync(process, """
			{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"clio.mcp.e2e.diagnostics","version":"1.0.0"}}}
			""");
		await ReadResponseAsync(process, expectedId: 1);
		double initializeSeconds = stopwatch.Elapsed.TotalSeconds;
		await WriteLineAsync(process, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
		await WriteLineAsync(process, """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
		string toolsResponse = await ReadResponseAsync(process, expectedId: 2);
		double listSeconds = stopwatch.Elapsed.TotalSeconds - initializeSeconds;
		int advertisedTools = CountAdvertisedTools(toolsResponse);

		Stopwatch shutdown = Stopwatch.StartNew();
		if (killInsteadOfGracefulExit) {
			process.Kill(entireProcessTree: true);
		} else {
			process.StandardInput.Close();
		}
		await process.WaitForExitAsync().WaitAsync(ExitTimeout);
		shutdown.Stop();
		string errorText = await standardError.WaitAsync(TimeSpan.FromSeconds(10));
		return new LifecycleSample(
			initializeSeconds,
			listSeconds,
			shutdown.Elapsed.TotalSeconds,
			advertisedTools,
			FirstLine(errorText));
	}

	private static Process CreateProcess(ClioProcessDescriptor descriptor, McpE2ESettings settings) {
		ProcessStartInfo startInfo = new() {
			FileName = descriptor.Command,
			WorkingDirectory = descriptor.WorkingDirectory,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			StandardOutputEncoding = Encoding.UTF8,
			StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
		};
		foreach (string argument in descriptor.Arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		foreach (KeyValuePair<string, string?> variable in settings.ProcessEnvironmentVariables) {
			startInfo.Environment[variable.Key] = variable.Value;
		}
		return new Process { StartInfo = startInfo };
	}

	private static async Task WriteLineAsync(Process process, string payload) {
		await process.StandardInput.WriteAsync(payload.Trim());
		await process.StandardInput.WriteAsync('\n');
		await process.StandardInput.FlushAsync();
	}

	/// <summary>
	/// Reads newline-delimited JSON-RPC from the child until the response carrying
	/// <paramref name="expectedId"/> arrives, skipping any interleaved notification.
	/// </summary>
	private static async Task<string> ReadResponseAsync(Process process, int expectedId) {
		using CancellationTokenSource timeout = new(HandshakeTimeout);
		while (true) {
			string? line = await process.StandardOutput.ReadLineAsync(timeout.Token);
			if (line is null) {
				throw new InvalidOperationException(
					$"The clio MCP server closed stdout before answering request id={expectedId}.");
			}
			if (string.IsNullOrWhiteSpace(line)) {
				continue;
			}
			try {
				using JsonDocument document = JsonDocument.Parse(line);
				if (document.RootElement.TryGetProperty("id", out JsonElement id)
					&& id.ValueKind == JsonValueKind.Number
					&& id.GetInt32() == expectedId) {
					return line;
				}
			} catch (JsonException) {
				// Non-JSON diagnostics on stdout are not part of the protocol stream; ignore them.
			}
		}
	}

	private static int CountAdvertisedTools(string toolsResponse) {
		try {
			using JsonDocument document = JsonDocument.Parse(toolsResponse);
			return document.RootElement.TryGetProperty("result", out JsonElement result)
				&& result.TryGetProperty("tools", out JsonElement tools)
					? tools.GetArrayLength()
					: -1;
		} catch (JsonException) {
			return -1;
		}
	}

	private static async Task ProbeEgressAsync(string host, int port) {
		Stopwatch stopwatch = Stopwatch.StartNew();
		try {
			using TcpClient client = new();
			using CancellationTokenSource timeout = new(EgressProbeTimeout);
			await client.ConnectAsync(host, port, timeout.Token);
			stopwatch.Stop();
			Report($"[egress] {host}:{port} reachable in {stopwatch.Elapsed.TotalSeconds:F2}s");
		} catch (Exception exception) {
			stopwatch.Stop();
			Report($"[egress] {host}:{port} UNREACHABLE after {stopwatch.Elapsed.TotalSeconds:F2}s "
				+ $"({exception.GetType().Name}: {exception.Message})");
		}
	}

	private static void ReportEnvironment(McpE2ESettings settings) {
		Report($"[env] clio process path = {settings.ClioProcessPath}");
		Report($"[env] suppress curated knowledge bootstrap = {settings.SuppressCuratedKnowledgeBootstrap}");
		foreach (string name in new[] {
			"CLIO_HOME", "CLIO_NO_UPDATE_CHECK", "CLIO_TELEMETRY_ENDPOINT",
			"CLIO_COMPONENT_REGISTRY_CDN_BASE_URL", "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY"
		}) {
			settings.ProcessEnvironmentVariables.TryGetValue(name, out string? forwarded);
			string effective = forwarded ?? Environment.GetEnvironmentVariable(name) ?? "<unset>";
			Report($"[env] {name} = {effective}");
		}
	}

	private static void ReportVerdict(
		IReadOnlyCollection<double> baselineSeconds,
		IReadOnlyCollection<LifecycleSample> samples,
		LifecycleSample killed) {
		double baseline = Median([.. baselineSeconds]);
		double initialize = Median([.. samples.Select(sample => sample.InitializeSeconds)]);
		double list = Median([.. samples.Select(sample => sample.ListSeconds)]);
		double shutdown = Median([.. samples.Select(sample => sample.ShutdownSeconds)]);
		double total = Median([.. samples.Select(sample => sample.TotalSeconds)]);
		Report("=== MEDIANS ===");
		Report($"[median] no-mcp-host baseline process   = {baseline:F2}s");
		Report($"[median] spawn -> initialize response   = {initialize:F2}s  (of which ~{baseline:F2}s is runtime boot; "
			+ $"the remaining ~{Math.Max(0, initialize - baseline):F2}s is MCP host build + schema generation)");
		Report($"[median] tools/list round trip          = {list:F2}s");
		Report($"[median] stdin EOF -> process exit      = {shutdown:F2}s");
		Report($"[median] FULL LIFECYCLE                 = {total:F2}s");
		Report($"[kill]   same cycle, killed not EOF     = {killed.TotalSeconds:F2}s "
			+ $"(shutdown window {killed.ShutdownSeconds:F2}s)");
		string verdict = shutdown >= 5
			? "SHUTDOWN-DOMINATED -> the fixed cost is the 10s drain in McpServerCommand.Execute's finally "
				+ "block (and/or the harness 10s StdioClientTransportOptions.ShutdownTimeout). Fix the drain."
			: initialize - baseline >= 5
				? "STARTUP-DOMINATED -> the fixed cost is MCP host registration / schema generation, not a timer. "
					+ "Fix by sharing sessions and making host build cheaper."
				: baseline >= 5
					? "RUNTIME-BOOT-DOMINATED -> the cost is process/runtime start on this agent (JIT, disk, AV), "
						+ "independent of clio's MCP host. Fix by spawning far fewer processes."
					: "NO SINGLE DOMINANT PHASE on this agent — compare against the TeamCity per-test numbers.";
		Report($"[verdict] {verdict}");
	}

	private static double Median(double[] values) {
		if (values.Length == 0) {
			return 0;
		}
		Array.Sort(values);
		int middle = values.Length / 2;
		return values.Length % 2 == 1
			? values[middle]
			: (values[middle - 1] + values[middle]) / 2;
	}

	private static string FirstLine(string text) {
		if (string.IsNullOrWhiteSpace(text)) {
			return string.Empty;
		}
		string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return lines.Length == 0 ? string.Empty : lines[0];
	}

	private static void Report(string message) {
		TestContext.Progress.WriteLine(message);
		TestContext.Out.WriteLine(message);
	}

	private sealed record LifecycleSample(
		double InitializeSeconds,
		double ListSeconds,
		double ShutdownSeconds,
		int AdvertisedTools,
		string FirstStandardErrorLine) {

		public double TotalSeconds => InitializeSeconds + ListSeconds + ShutdownSeconds;

		public override string ToString() =>
			$"initialize={InitializeSeconds:F2}s list={ListSeconds:F2}s shutdown={ShutdownSeconds:F2}s "
			+ $"total={TotalSeconds:F2}s tools={AdvertisedTools}"
			+ (string.IsNullOrWhiteSpace(FirstStandardErrorLine) ? string.Empty : $" stderr=\"{FirstStandardErrorLine}\"");
	}
}
