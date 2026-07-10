using System.Diagnostics;
using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the product telemetry MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("send-telemetry")]
[NonParallelizable]
public sealed class SendTelemetryToolE2ETests
{
	private const string ToolName = SendTelemetryTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes send-telemetry with first-use consent, and verifies that an OpenTelemetry-shaped event is written locally.")]
	[AllureTag(ToolName)]
	[AllureName("Send Telemetry stores OTel event file")]
	[AllureDescription("Uses the real clio MCP server to invoke send-telemetry and verifies the local telemetry event file.")]
	public async Task SendTelemetry_Should_Store_Otel_Event_File()
	{
		// Arrange
		string sessionId = Guid.NewGuid().ToString();
		// Redirect telemetry to a test-owned temp directory so the test never reads or mutates
		// the developer's real consent/installation state. The clio MCP subprocess inherits this
		// environment variable and uses it as its telemetry storage root.
		string telemetryHome = Path.Combine(Path.GetTempPath(), "clio-telemetry-e2e", Guid.NewGuid().ToString("N"));
		string? previousTelemetryHome = Environment.GetEnvironmentVariable(TelemetryHomeEnvironmentVariable);
		string? previousTelemetryEnabled = Environment.GetEnvironmentVariable(TelemetryEnabledEnvironmentVariable);
		Environment.SetEnvironmentVariable(TelemetryHomeEnvironmentVariable, telemetryHome);
		// This test exercises only local event storage. With a production default endpoint now shipped,
		// disable the background flusher so a granted-consent event is never uploaded to the real
		// collector from a test run.
		Environment.SetEnvironmentVariable(TelemetryEnabledEnvironmentVariable, "false");
		await using RawMcpSession session = RawMcpSession.Start();

		try {
			// Act
			// send-telemetry is hidden from tools/list on the lazy tool surface: it is discoverable only
			// through the get-tool-contract compact index and callable only through the clio-run executor
			// ({"command":"send-telemetry","args":{...}}), which returns the target tool's result verbatim.
			JsonDocument discovery = await session.SendRequestAsync("tools/call", new {
				name = ToolContractGetTool.ToolName,
				arguments = new { }
			});
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

			// Assert
			// The compact index rides the raw CallToolResult (text and/or structured content), so the
			// discoverability gate searches the serialized result for the tool name.
			discovery.RootElement.GetProperty("result").ToString().Should().Contain(ToolName,
				because: "the real MCP server should list send-telemetry in the get-tool-contract compact index on the lazy tool surface");
			callResult.RootElement.TryGetProperty("error", out _).Should().BeFalse(
				because: "send-telemetry should return a normal MCP response when the event is persisted locally");
			// A clio-run dispatch failure (for example an unknown tool) surfaces as isError=true inside
			// a normal JSON-RPC result, so the routed call outcome must be checked explicitly.
			(callResult.RootElement.GetProperty("result").TryGetProperty("isError", out JsonElement isError)
					&& isError.ValueKind == JsonValueKind.True)
				.Should().BeFalse(
					because: "the clio-run dispatch of send-telemetry should succeed so the event is persisted locally");
			string? eventFile = FindEventFile(telemetryHome, sessionId);
			eventFile.Should().NotBeNull(
				because: "one product event should be persisted locally");
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(eventFile!));
			document.RootElement.GetProperty("severity_text").GetString().Should().Be("INFO",
				because: "product telemetry is represented as an OpenTelemetry info log");
			document.RootElement.GetProperty("event_name").GetString().Should().Be("session_started",
				because: "the event name is stored once, in the dedicated OTel event_name field");
		} finally {
			Environment.SetEnvironmentVariable(TelemetryHomeEnvironmentVariable, previousTelemetryHome);
			Environment.SetEnvironmentVariable(TelemetryEnabledEnvironmentVariable, previousTelemetryEnabled);
			if (Directory.Exists(telemetryHome)) {
				Directory.Delete(telemetryHome, recursive: true);
			}
		}
	}

	[Test]
	[Description("Starts the real clio MCP server, grants consent and stores an event, then withdraws consent and verifies the local outbox is purged.")]
	[AllureTag("withdraw-telemetry-consent")]
	[AllureName("Withdraw Telemetry Consent purges the local outbox")]
	[AllureDescription("Uses the real clio MCP server to grant consent, store an event, then call withdraw-telemetry-consent and verify the spooled event is deleted.")]
	public async Task WithdrawTelemetryConsent_Should_Purge_Spooled_Events()
	{
		// Arrange
		string sessionId = Guid.NewGuid().ToString();
		string telemetryHome = Path.Combine(Path.GetTempPath(), "clio-telemetry-e2e", Guid.NewGuid().ToString("N"));
		string? previousTelemetryHome = Environment.GetEnvironmentVariable(TelemetryHomeEnvironmentVariable);
		string? previousTelemetryEnabled = Environment.GetEnvironmentVariable(TelemetryEnabledEnvironmentVariable);
		Environment.SetEnvironmentVariable(TelemetryHomeEnvironmentVariable, telemetryHome);
		// Keep uploads disabled so the granted-consent event is never sent to the real collector from a test run.
		Environment.SetEnvironmentVariable(TelemetryEnabledEnvironmentVariable, "false");
		await using RawMcpSession session = RawMcpSession.Start();

		try {
			// Act — grant consent and store one event, then withdraw consent. Both telemetry tools are
			// hidden from tools/list on the lazy tool surface, so the raw JSON-RPC calls are dispatched
			// through the clio-run executor; withdraw-telemetry-consent takes no arguments, so no args
			// object is forwarded alongside the command name.
			await session.SendRequestAsync("tools/call", new {
				name = ClioRunTool.ToolName,
				arguments = new {
					command = SendTelemetryTool.ToolName,
					args = new {
						session_id = sessionId,
						event_name = "session_started",
						coding_agent = "Codex",
						plugin_version = "0.1.0",
						telemetry_consent = "granted"
					}
				}
			});
			FindEventFile(telemetryHome, sessionId).Should().NotBeNull(
				because: "the granted-consent event should be spooled before withdrawal");

			JsonDocument discovery = await session.SendRequestAsync("tools/call", new {
				name = ToolContractGetTool.ToolName,
				arguments = new { }
			});
			JsonDocument withdrawResult = await session.SendRequestAsync("tools/call", new {
				name = ClioRunTool.ToolName,
				arguments = new {
					command = WithdrawTelemetryConsentTool.ToolName
				}
			});

			// Assert
			// The compact index rides the raw CallToolResult (text and/or structured content), so the
			// discoverability gate searches the serialized result for the tool name.
			discovery.RootElement.GetProperty("result").ToString().Should().Contain(WithdrawTelemetryConsentTool.ToolName,
				because: "the real MCP server should list withdraw-telemetry-consent in the get-tool-contract compact index on the lazy tool surface");
			withdrawResult.RootElement.TryGetProperty("error", out _).Should().BeFalse(
				because: "withdraw-telemetry-consent should return a normal MCP response");
			// A clio-run dispatch failure (for example an unknown tool) surfaces as isError=true inside
			// a normal JSON-RPC result, so the routed call outcome must be checked explicitly.
			(withdrawResult.RootElement.GetProperty("result").TryGetProperty("isError", out JsonElement isError)
					&& isError.ValueKind == JsonValueKind.True)
				.Should().BeFalse(
					because: "the clio-run dispatch of withdraw-telemetry-consent should succeed so the outbox purge runs");
			FindEventFile(telemetryHome, sessionId).Should().BeNull(
				because: "withdrawal must purge the not-yet-uploaded local outbox");
			File.Exists(Path.Combine(telemetryHome, "consent.json")).Should().BeTrue(
				because: "the denied decision is persisted locally so later sessions stay opted out");
		} finally {
			Environment.SetEnvironmentVariable(TelemetryHomeEnvironmentVariable, previousTelemetryHome);
			Environment.SetEnvironmentVariable(TelemetryEnabledEnvironmentVariable, previousTelemetryEnabled);
			if (Directory.Exists(telemetryHome)) {
				Directory.Delete(telemetryHome, recursive: true);
			}
		}
	}

	/// <summary>
	/// Environment variable understood by <c>TelemetryService</c> to redirect its local storage root.
	/// </summary>
	private const string TelemetryHomeEnvironmentVariable = "CLIO_TELEMETRY_HOME";

	/// <summary>
	/// Environment variable understood by <c>TelemetryFlushOptionsProvider</c> to disable uploads.
	/// </summary>
	private const string TelemetryEnabledEnvironmentVariable = "CLIO_TELEMETRY_ENABLED";

	private static string? FindEventFile(string telemetryHome, string sessionId)
	{
		string eventsDirectory = Path.Combine(telemetryHome, "events");
		if (!Directory.Exists(eventsDirectory)) {
			return null;
		}
		return Directory.GetFiles(eventsDirectory, "*.json")
			.SingleOrDefault(path => File.ReadAllText(path).Contains(sessionId, StringComparison.Ordinal));
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
					name = "clio.mcp.e2e.raw",
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
