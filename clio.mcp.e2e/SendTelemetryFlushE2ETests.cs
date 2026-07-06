using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the background telemetry flusher: spooled telemetry events are
/// uploaded as OTLP/HTTP JSON to a stub collector and removed locally on success.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("send-telemetry-flush")]
[NonParallelizable]
public sealed class SendTelemetryFlushE2ETests
{
	private const string ToolName = SendTelemetryTool.ToolName;

	/// <summary>
	/// Environment variable understood by <c>TelemetryService</c> to redirect its local storage root.
	/// </summary>
	private const string TelemetryHomeEnvironmentVariable = "CLIO_TELEMETRY_HOME";

	/// <summary>
	/// Environment variables understood by <c>TelemetryFlushOptionsProvider</c>.
	/// </summary>
	private const string TelemetryEndpointEnvironmentVariable = "CLIO_TELEMETRY_ENDPOINT";
	private const string TelemetryIngestKeyEnvironmentVariable = "CLIO_TELEMETRY_INGEST_KEY";

	private static readonly TimeSpan FlushWaitTimeout = TimeSpan.FromSeconds(15);

	[Test]
	[Description("Starts the real clio MCP server with a configured telemetry endpoint, invokes send-telemetry with consent, and verifies the event is uploaded to the stub OTLP collector and removed from the local spool.")]
	[AllureTag(ToolName)]
	[AllureName("Send Telemetry flushes stored event to the collector")]
	[AllureDescription("Uses the real clio MCP server and a local HTTP stub collector to verify the background OTLP/HTTP upload after send-telemetry.")]
	public async Task SendTelemetry_Should_Flush_Event_To_Stub_Collector_When_Endpoint_Configured()
	{
		// Arrange
		string sessionId = Guid.NewGuid().ToString();
		string telemetryHome = Path.Combine(Path.GetTempPath(), "clio-telemetry-flush-e2e", Guid.NewGuid().ToString("N"));
		await using StubCollector collector = StubCollector.Start();
		using EnvironmentVariableScope homeScope = new(TelemetryHomeEnvironmentVariable, telemetryHome);
		using EnvironmentVariableScope endpointScope = new(TelemetryEndpointEnvironmentVariable, collector.LogsUrl);
		using EnvironmentVariableScope keyScope = new(TelemetryIngestKeyEnvironmentVariable, "e2e-ingest-key");
		await using RawMcpSession session = RawMcpSession.Start();

		try {
			// Act
			// send-telemetry is hidden from tools/list on the lazy tool surface, so the raw JSON-RPC
			// call is dispatched through the clio-run executor ({"command":"send-telemetry","args":{...}});
			// clio-run returns the target tool's result verbatim, so the flush behavior under test is unchanged.
			JsonDocument callResult = await session.SendRequestAsync("tools/call", new {
				name = ClioRunTool.ToolName,
				arguments = new {
					command = ToolName,
					args = new {
						session_id = sessionId,
						event_name = "session_started",
						coding_agent = "Codex",
						plugin_version = "0.1.0",
						telemetry_consent = "granted"
					}
				}
			});
			bool uploaded = await WaitUntilAsync(() => collector.Requests.Count > 0, FlushWaitTimeout);

			// Assert
			callResult.RootElement.TryGetProperty("error", out _).Should().BeFalse(
				because: "send-telemetry should store the event and respond normally");
			// A clio-run dispatch failure (for example an unknown tool) surfaces as isError=true inside
			// a normal JSON-RPC result, so the routed call outcome must be checked explicitly.
			(callResult.RootElement.GetProperty("result").TryGetProperty("isError", out JsonElement isError)
					&& isError.ValueKind == JsonValueKind.True)
				.Should().BeFalse(
					because: "the clio-run dispatch of send-telemetry should succeed so the event reaches the local spool");
			uploaded.Should().BeTrue(
				because: "the background flusher should upload the stored event shortly after the tool call");
			StubCollector.CapturedRequest request = collector.Requests[0];
			request.IngestKey.Should().Be("e2e-ingest-key",
				because: "the configured ingest key must be sent as the X-Ingest-Key header");
			using JsonDocument payload = JsonDocument.Parse(request.Body);
			JsonElement logRecords = payload.RootElement.GetProperty("resourceLogs")[0]
				.GetProperty("scopeLogs")[0].GetProperty("logRecords");
			logRecords.GetArrayLength().Should().Be(1,
				because: "exactly one stored event should be uploaded");
			JsonElement logRecord = logRecords[0];
			logRecord.GetProperty("timeUnixNano").ValueKind.Should().Be(JsonValueKind.String,
				because: "OTLP/HTTP JSON encodes the int64 timeUnixNano as a string");
			logRecord.GetProperty("eventName").GetString().Should().Be("session_started",
				because: "the event name rides the dedicated OTLP eventName field (single source) that populates the ClickHouse EventName column");
			request.Body.Should().NotContain("time_unix_nano",
				because: "a wire/schema regression to the snake_case storage shape would be silently rejected and dropped by a strict collector");
			request.Body.Should().Contain(sessionId,
				because: "the uploaded OTLP payload should carry the original session id attribute");
			bool drained = await WaitUntilAsync(() => EventFiles(telemetryHome).Length == 0, FlushWaitTimeout);
			drained.Should().BeTrue(
				because: "delivered events must be deleted from the local spool");
		} finally {
			if (Directory.Exists(telemetryHome)) {
				Directory.Delete(telemetryHome, recursive: true);
			}
		}
	}

	[Test]
	[Description("Pre-seeds a spooled event and granted consent, then verifies that starting the clio MCP server alone uploads the backlog to the stub collector without any tool calls.")]
	[AllureTag(ToolName)]
	[AllureName("MCP server start flushes spool backlog")]
	[AllureDescription("Verifies the on-start flush trigger: a pre-seeded spool drains to the stub collector with no send-telemetry calls.")]
	public async Task McpServerStart_Should_Flush_Spool_Backlog_Without_Tool_Calls()
	{
		// Arrange
		string sessionId = Guid.NewGuid().ToString();
		string telemetryHome = Path.Combine(Path.GetTempPath(), "clio-telemetry-flush-e2e", Guid.NewGuid().ToString("N"));
		SeedConsent(telemetryHome);
		SeedEvent(telemetryHome, sessionId);
		await using StubCollector collector = StubCollector.Start();
		using EnvironmentVariableScope homeScope = new(TelemetryHomeEnvironmentVariable, telemetryHome);
		using EnvironmentVariableScope endpointScope = new(TelemetryEndpointEnvironmentVariable, collector.LogsUrl);
		using EnvironmentVariableScope keyScope = new(TelemetryIngestKeyEnvironmentVariable, null);
		await using RawMcpSession session = RawMcpSession.Start();

		try {
			// Act
			bool uploaded = await WaitUntilAsync(() => collector.Requests.Count > 0, FlushWaitTimeout);

			// Assert
			uploaded.Should().BeTrue(
				because: "starting the MCP server should drain the telemetry spool left over from previous sessions");
			collector.Requests[0].Body.Should().Contain(sessionId,
				because: "the pre-seeded spooled event should reach the collector");
			bool drained = await WaitUntilAsync(() => EventFiles(telemetryHome).Length == 0, FlushWaitTimeout);
			drained.Should().BeTrue(
				because: "the uploaded backlog must be removed from the local spool");
		} finally {
			if (Directory.Exists(telemetryHome)) {
				Directory.Delete(telemetryHome, recursive: true);
			}
		}
	}

	private static string[] EventFiles(string telemetryHome)
	{
		string eventsDirectory = Path.Combine(telemetryHome, "events");
		return Directory.Exists(eventsDirectory)
			? Directory.GetFiles(eventsDirectory, "*.json")
			: [];
	}

	private static void SeedConsent(string telemetryHome)
	{
		Directory.CreateDirectory(telemetryHome);
		File.WriteAllText(Path.Combine(telemetryHome, "consent.json"),
			"""{ "telemetry_consent": "granted", "updated_at": "2026-01-01T00:00:00+00:00" }""");
	}

	private static void SeedEvent(string telemetryHome, string sessionId)
	{
		string eventsDirectory = Path.Combine(telemetryHome, "events");
		Directory.CreateDirectory(eventsDirectory);
		string eventId = Guid.NewGuid().ToString("N");
		string fileName = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}_{eventId}.json";
		File.WriteAllText(Path.Combine(eventsDirectory, fileName),
			$$"""
			{
				"time_unix_nano": 1767225600000000000,
				"severity_text": "INFO",
				"event_name": "session_started",
				"attributes": [
					{ "key": "session_id", "value": { "string_value": "{{sessionId}}" } },
					{ "key": "event_id", "value": { "string_value": "{{eventId}}" } }
				]
			}
			""");
	}

	private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout) {
			if (condition()) {
				return true;
			}
			await Task.Delay(200);
		}
		return condition();
	}

	private sealed class EnvironmentVariableScope : IDisposable
	{
		private readonly string _name;
		private readonly string? _previous;

		public EnvironmentVariableScope(string name, string? value)
		{
			_name = name;
			_previous = Environment.GetEnvironmentVariable(name);
			Environment.SetEnvironmentVariable(name, value);
		}

		public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
	}

	/// <summary>
	/// Minimal local OTLP collector stub: accepts POST /v1/logs, records bodies and the
	/// ingest-key header, and always responds 200.
	/// </summary>
	private sealed class StubCollector : IAsyncDisposable
	{
		private readonly HttpListener _listener;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task _loop;
		private readonly List<CapturedRequest> _requests = [];
		private readonly object _sync = new();

		private StubCollector(HttpListener listener, string logsUrl)
		{
			_listener = listener;
			LogsUrl = logsUrl;
			_loop = Task.Run(LoopAsync);
		}

		public string LogsUrl { get; }

		public IReadOnlyList<CapturedRequest> Requests
		{
			get {
				lock (_sync) {
					return _requests.ToArray();
				}
			}
		}

		public static StubCollector Start()
		{
			for (int attempt = 0; attempt < 5; attempt++) {
				int port = Random.Shared.Next(20000, 60000);
				HttpListener listener = new();
				listener.Prefixes.Add($"http://127.0.0.1:{port}/");
				try {
					listener.Start();
					return new StubCollector(listener, $"http://127.0.0.1:{port}/v1/logs");
				} catch (HttpListenerException) {
					// Port collision — try another one.
				}
			}
			throw new InvalidOperationException("Unable to start the stub telemetry collector.");
		}

		private async Task LoopAsync()
		{
			while (!_cts.IsCancellationRequested) {
				HttpListenerContext context;
				try {
					context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
				} catch (Exception) {
					return;
				}
				using StreamReader reader = new(context.Request.InputStream, context.Request.ContentEncoding);
				string body = await reader.ReadToEndAsync();
				string? ingestKey = context.Request.Headers["X-Ingest-Key"];
				lock (_sync) {
					_requests.Add(new CapturedRequest(body, ingestKey));
				}
				context.Response.StatusCode = 200;
				context.Response.Close();
			}
		}

		public async ValueTask DisposeAsync()
		{
			await _cts.CancelAsync();
			try {
				_listener.Stop();
				_listener.Close();
			} catch {
				// Test cleanup should not hide assertion failures.
			}
			try {
				await _loop;
			} catch {
				// Loop exit after Stop is expected.
			}
		}

		public sealed record CapturedRequest(string Body, string? IngestKey);
	}

	private sealed class RawMcpSession : IAsyncDisposable
	{
		private readonly Process _process;
		private int _nextId = 1;

		private RawMcpSession(Process process)
		{
			_process = process;
		}

		public static RawMcpSession Start()
		{
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(settings);
			ProcessStartInfo startInfo = new() {
				FileName = descriptor.Command,
				UseShellExecute = false,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = false,
				CreateNoWindow = true,
				WorkingDirectory = descriptor.WorkingDirectory
			};
			foreach (string argument in descriptor.Arguments) {
				startInfo.ArgumentList.Add(argument);
			}
			foreach (var variable in settings.ProcessEnvironmentVariables) {
				if (variable.Value is not null) {
					startInfo.Environment[variable.Key] = variable.Value;
				}
			}
			Process process = Process.Start(startInfo)
				?? throw new InvalidOperationException("Unable to start clio MCP server.");
			RawMcpSession session = new(process);
			session.SendRequestAsync("initialize", new {
				protocolVersion = "2024-11-05",
				capabilities = new { },
				clientInfo = new {
					name = "clio.mcp.e2e.flush",
					version = "1.0.0"
				}
			}).GetAwaiter().GetResult();
			return session;
		}

		public async Task<JsonDocument> SendRequestAsync(string method, object parameters)
		{
			int id = _nextId++;
			await WriteMessageAsync(new {
				jsonrpc = "2.0",
				id,
				method,
				@params = parameters
			});
			while (true) {
				JsonDocument response = await ReadMessageAsync();
				if (response.RootElement.TryGetProperty("id", out JsonElement responseId)
					&& responseId.GetInt32() == id) {
					return response;
				}
				response.Dispose();
			}
		}

		private async Task WriteMessageAsync(object message)
		{
			await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
			await _process.StandardInput.FlushAsync();
		}

		private async Task<JsonDocument> ReadMessageAsync()
		{
			string? line;
			do {
				line = await _process.StandardOutput.ReadLineAsync();
				line.Should().NotBeNull(because: "the MCP server should keep stdout open while sending a response");
			} while (string.IsNullOrWhiteSpace(line));
			return JsonDocument.Parse(line);
		}

		public async ValueTask DisposeAsync()
		{
			try {
				if (!_process.HasExited) {
					_process.Kill(entireProcessTree: true);
				}
				await _process.WaitForExitAsync();
			} catch {
				// Test cleanup should not hide assertion failures.
			}
			_process.Dispose();
		}
	}
}
