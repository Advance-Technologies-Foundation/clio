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
/// ENG-93885: raw JSON-RPC stdio regression guard for the one condition the SDK-based harness cannot
/// reproduce — the real CAADT 1.4.0 <c>mcp_client.py</c> wire sequence, which sends <c>initialize</c>
/// (clientInfo <c>{"name":"mcp_client","version":"1.0"}</c>), deliberately NEVER sends
/// <c>notifications/initialized</c>, and then immediately issues <c>tools/call</c>. The fix in
/// <see cref="ToolContractGetTool"/> rests on the load-bearing SDK assumption that
/// <c>Server.ClientInfo</c> is captured at initialize-REQUEST time, so <c>tools/call</c> works and the
/// legacy full "tools" shape is served even without the lifecycle notification. If a future
/// ModelContextProtocol bump moved ClientInfo capture onto <c>notifications/initialized</c> (or enforced
/// a strict lifecycle), this test fails while every handshake-completing test stays green — exactly the
/// silent re-break mode that shipped the original field incident.
/// </summary>
/// <remarks>
/// This test speaks newline-delimited JSON-RPC directly over the spawned <c>clio mcp-server</c> process's
/// stdio (same raw-process pattern as <see cref="McpServerShutdownE2ETests"/>), bypassing the C# SDK
/// client whose <c>McpClient.CreateAsync</c> always completes the handshake with
/// <c>notifications/initialized</c> and offers no supported way to suppress it.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ToolContractGetTool.ToolName)]
[NonParallelizable]
public sealed class ToolContractGetToolLegacyClientRawStdioE2ETests {
	private const string McpServerVerb = "mcp-server";
	private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMinutes(3);

	[Test]
	[Description("Mirrors the exact CAADT 1.4.0 mcp_client.py wire sequence: initialize with clientInfo name=mcp_client/version=1.0, NO notifications/initialized, then a no-args tools/call get-tool-contract - the response must carry the full non-empty tools array and no compact index, proving ClientInfo is captured at initialize-request time and the strict lifecycle is not enforced.")]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("raw stdio legacy client without notifications/initialized gets the full tools shape")]
	public async Task RawStdioLegacyClient_Should_GetFullToolsShape_WithoutInitializedNotification() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(settings, McpServerVerb);
		ProcessStartInfo startInfo = new() {
			FileName = descriptor.Command,
			WorkingDirectory = descriptor.WorkingDirectory,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};
		foreach (string argument in descriptor.Arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		using CancellationTokenSource timeoutSource = new(ResponseTimeout);

		// Act
		using Process process = new() { StartInfo = startInfo };
		process.Start().Should().BeTrue(
			because: "the clio mcp-server process must launch before the raw legacy wire sequence can be exercised");
		try {
			// Drain stderr in the background so a chatty server can never fill the pipe and deadlock the test.
			Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

			// Wire message 1 - the exact initialize the CAADT 1.4.0 client sends (mcp_client.py line 181 at
			// that tag): clientInfo name=mcp_client, version=1.0.
			await SendAsync(process,
				"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"mcp_client","version":"1.0"}}}""",
				timeoutSource.Token);
			JsonElement initializeResponse = await ReadResponseAsync(process, expectedId: 1, timeoutSource.Token);
			initializeResponse.TryGetProperty("result", out JsonElement initializeResult).Should().BeTrue(
				because: "initialize must succeed for the legacy client identity before tools/call is even attempted");
			initializeResult.TryGetProperty("serverInfo", out _).Should().BeTrue(
				because: "a successful initialize result carries the serverInfo the real client logs");

			// Deliberately NO notifications/initialized here - that is the discriminating real-client
			// condition this test exists to pin. Go straight to the no-args discovery call.
			string callRequest = """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"""
				+ JsonSerializer.Serialize(ToolContractGetTool.ToolName)
				+ ""","arguments":{}}}""";
			await SendAsync(process, callRequest, timeoutSource.Token);
			JsonElement callResponse = await ReadResponseAsync(process, expectedId: 2, timeoutSource.Token);

			// Assert
			callResponse.TryGetProperty("error", out JsonElement rpcError).Should().BeFalse(
				because: $"tools/call must not be rejected at the JSON-RPC level without notifications/initialized, but got: {(rpcError.ValueKind == JsonValueKind.Undefined ? "" : rpcError.GetRawText())}");
			callResponse.TryGetProperty("result", out JsonElement callResult).Should().BeTrue(
				because: "tools/call must return a result envelope for the legacy client");
			if (callResult.TryGetProperty("isError", out JsonElement isError)) {
				isError.ValueKind.Should().NotBe(JsonValueKind.True,
					because: "get-tool-contract must return a normal tool result, not a tool-level error, for the legacy client");
			}
			JsonElement payload = ExtractToolPayload(callResult);
			payload.TryGetProperty("success", out JsonElement success).Should().BeTrue(
				because: "the get-tool-contract envelope always carries the success flag");
			success.ValueKind.Should().Be(JsonValueKind.True,
				because: "a no-args get-tool-contract call must succeed for the legacy stdio client identity");
			payload.TryGetProperty("tools", out JsonElement tools).Should().BeTrue(
				because: "the legacy CAADT 1.4.0 client hard-crashes on the compact index and must receive the full tools array instead");
			tools.ValueKind.Should().Be(JsonValueKind.Array,
				because: "the legacy shape is a JSON array of full tool contracts");
			tools.GetArrayLength().Should().BeGreaterThan(0,
				because: "the legacy full tools array must actually enumerate the tool universe, not be empty");
			bool hasIndex = payload.TryGetProperty("index", out JsonElement index)
				&& index.ValueKind != JsonValueKind.Null;
			hasIndex.Should().BeFalse(
				because: "the legacy response must not carry the compact discovery index the shipped client crashes on");
			_ = standardErrorTask;
		} finally {
			// Guarantee the spawned child is gone even if an assertion above throws first, so a failed
			// run never leaks a clio mcp-server process onto the runner.
			TryKill(process);
		}
	}

	private static async Task SendAsync(Process process, string jsonRpcLine, CancellationToken cancellationToken) {
		await process.StandardInput.WriteLineAsync(jsonRpcLine.AsMemory(), cancellationToken);
		await process.StandardInput.FlushAsync(cancellationToken);
	}

	/// <summary>
	/// Reads newline-delimited JSON from the server's stdout until the response with
	/// <paramref name="expectedId"/> arrives, skipping any non-JSON noise and unrelated
	/// notifications/log messages the transport may interleave.
	/// </summary>
	private static async Task<JsonElement> ReadResponseAsync(Process process, int expectedId, CancellationToken cancellationToken) {
		while (true) {
			cancellationToken.ThrowIfCancellationRequested();
			string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
			line.Should().NotBeNull(
				because: $"the mcp-server process must not close stdout before answering request id={expectedId} (exit code: {(process.HasExited ? process.ExitCode.ToString() : "still running")})");
			if (string.IsNullOrWhiteSpace(line)) {
				continue;
			}
			JsonElement message;
			try {
				using JsonDocument document = JsonDocument.Parse(line!);
				message = document.RootElement.Clone();
			} catch (JsonException) {
				continue;
			}
			if (message.TryGetProperty("id", out JsonElement id)
				&& id.ValueKind == JsonValueKind.Number
				&& id.GetInt32() == expectedId) {
				return message;
			}
		}
	}

	/// <summary>
	/// Extracts the get-tool-contract JSON envelope from a raw tools/call result: prefers
	/// <c>structuredContent</c> when present, else parses the first <c>content[].text</c> payload —
	/// mirroring what the real Python client reads off the wire.
	/// </summary>
	private static JsonElement ExtractToolPayload(JsonElement callResult) {
		if (callResult.TryGetProperty("structuredContent", out JsonElement structuredContent)
			&& structuredContent.ValueKind == JsonValueKind.Object) {
			return structuredContent;
		}
		callResult.TryGetProperty("content", out JsonElement content).Should().BeTrue(
			because: "a tools/call result without structuredContent must still carry the content array");
		foreach (JsonElement item in content.EnumerateArray()) {
			if (item.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String) {
				using JsonDocument document = JsonDocument.Parse(text.GetString()!);
				return document.RootElement.Clone();
			}
		}
		throw new InvalidOperationException("tools/call result carried neither structuredContent nor a text content payload.");
	}

	private static void TryKill(Process process) {
		try {
			if (!process.HasExited) {
				process.Kill(entireProcessTree: true);
			}
		} catch (InvalidOperationException) {
			// The process already exited between the HasExited check and Kill - nothing to clean up.
		}
	}
}
