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
/// End-to-end tests for the ADAC product telemetry measurement MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("send-measurements")]
[NonParallelizable]
public sealed class SendMeasurementsToolE2ETests
{
	private const string ToolName = SendMeasurementsTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes send-measurements with first-use consent, and verifies that an OpenTelemetry-shaped event is written locally.")]
	[AllureTag(ToolName)]
	[AllureName("Send Measurements stores OTel event file")]
	[AllureDescription("Uses the real clio MCP server to invoke send-measurements and verifies the local telemetry event file.")]
	public async Task SendMeasurements_Should_Store_Otel_Event_File()
	{
		// Arrange
		string sessionId = Guid.NewGuid().ToString();
		string telemetryHome = DefaultTelemetryHome();
		string consentPath = Path.Combine(telemetryHome, "consent.json");
		string installationIdPath = Path.Combine(telemetryHome, "installation-id.txt");
		string? consentBackup = File.Exists(consentPath) ? File.ReadAllText(consentPath) : null;
		string? installationIdBackup = File.Exists(installationIdPath) ? File.ReadAllText(installationIdPath) : null;
		await using RawMcpSession session = RawMcpSession.Start();

		try {
			// Act
			JsonDocument tools = await session.SendRequestAsync("tools/list", new { });
			JsonDocument callResult = await session.SendRequestAsync("tools/call", new {
				name = ToolName,
				arguments = new {
					args = new {
						session_id = sessionId,
						event_name = "session_started",
						coding_agent = "Codex",
						skill_version = "0.1.0",
						plugin_version = "0.1.0",
						telemetry_consent = "granted"
					}
				}
			});

			// Assert
			tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
				.Select(tool => tool.GetProperty("name").GetString())
				.Should().Contain(ToolName,
					because: "the real MCP server should advertise the send-measurements tool");
			callResult.RootElement.TryGetProperty("error", out _).Should().BeFalse(
				because: "send-measurements should return a normal MCP response when the event is persisted locally");
			string? eventFile = FindEventFile(telemetryHome, sessionId);
			eventFile.Should().NotBeNull(
				because: "one ADAC product event should be persisted locally");
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(eventFile!));
			document.RootElement.GetProperty("severity_text").GetString().Should().Be("INFO",
				because: "product telemetry is represented as an OpenTelemetry info log");
			document.RootElement.GetProperty("body").GetProperty("string_value").GetString().Should().Be("session_started",
				because: "the OTel body should carry the event name");
		} finally {
			DeleteEventFile(telemetryHome, sessionId);
			DeleteSessionState(telemetryHome, sessionId);
			RestoreFile(consentPath, consentBackup);
			RestoreFile(installationIdPath, installationIdBackup);
		}
	}

	private static string DefaultTelemetryHome() =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".creatio-ai-app-development-toolkit", "telemetry");

	private static string? FindEventFile(string telemetryHome, string sessionId)
	{
		string eventsDirectory = Path.Combine(telemetryHome, "events");
		if (!Directory.Exists(eventsDirectory)) {
			return null;
		}
		return Directory.GetFiles(eventsDirectory, "*.json")
			.SingleOrDefault(path => File.ReadAllText(path).Contains(sessionId, StringComparison.Ordinal));
	}

	private static void DeleteEventFile(string telemetryHome, string sessionId)
	{
		string? eventFile = FindEventFile(telemetryHome, sessionId);
		if (eventFile is not null) {
			File.Delete(eventFile);
		}
	}

	private static void DeleteSessionState(string telemetryHome, string sessionId)
	{
		string sessionState = Path.Combine(telemetryHome, "sessions", $"{sessionId}.json");
		if (File.Exists(sessionState)) {
			File.Delete(sessionState);
		}
	}

	private static void RestoreFile(string path, string? content)
	{
		if (content is null) {
			if (File.Exists(path)) {
				File.Delete(path);
			}
			return;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
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
