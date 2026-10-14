using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Finds the DevTools endpoint of a Chromium started with <c>--remote-debugging-port=0</c>: the port it wrote to
/// <c>DevToolsActivePort</c>, then the WebSocket URL of its page target. Shared by the visible authenticated
/// launcher and the headless page-smoke launcher.
/// </summary>
internal static class DevToolsEndpoint {

	/// <summary>Reads the port Chromium chose from line 1 of <c>&lt;user-data-dir&gt;/DevToolsActivePort</c>.</summary>
	/// <remarks>Polls, because the file appears a moment after the process starts.</remarks>
	internal static async Task<int> ReadPortAsync(IFileSystem fileSystem, string userDataDir, int attempts, int delayMs,
		CancellationToken ct) {
		string portFile = Path.Combine(userDataDir, "DevToolsActivePort");
		for (int attempt = 0; attempt < attempts; attempt++) {
			ct.ThrowIfCancellationRequested();
			if (fileSystem.ExistsFile(portFile) && TryReadPort(fileSystem, portFile, out int port)) {
				return port;
			}
			await Task.Delay(delayMs, ct).ConfigureAwait(false);
		}
		throw new InvalidOperationException(
			"Error: timed out waiting for the browser's remote-debugging endpoint (DevToolsActivePort).");
	}

	// On Windows Chromium writes the file non-atomically under an exclusive share mode, so a read that lands while it
	// writes fails with a sharing violation (IOException), or with access denied while the file is delete-pending: both
	// are "not yet", like a missing or half-written file.
	private static bool TryReadPort(IFileSystem fileSystem, string portFile, out int port) {
		port = 0;
		string content;
		try {
			content = fileSystem.ReadAllText(portFile);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			return false;
		}
		string firstLine = content.Split('\n', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } lines
			? lines[0].Trim()
			: string.Empty;
		return int.TryParse(firstLine, out port) && port > 0;
	}

	/// <summary>Resolves the WebSocket URL of a page target from the local DevTools JSON endpoint.</summary>
	/// <remarks>
	/// <c>http://127.0.0.1:{port}/json</c> is the browser's own local control endpoint, NOT a Creatio service, so it
	/// deliberately does not go through <c>IApplicationClient</c>, which is Creatio-only.
	/// </remarks>
	internal static async Task<string> FindPageTargetAsync(IHttpClientFactory httpClientFactory, int port, int attempts,
		int delayMs, CancellationToken ct) {
		using HttpClient http = httpClientFactory.CreateClient();
		string listUrl = $"http://127.0.0.1:{port}/json";
		for (int attempt = 0; attempt < attempts; attempt++) {
			ct.ThrowIfCancellationRequested();
			try {
				string body = await http.GetStringAsync(listUrl, ct).ConfigureAwait(false);
				string url = FindPageWebSocketUrl(body);
				if (!string.IsNullOrEmpty(url)) {
					return url;
				}
			} catch (HttpRequestException) {
				// Endpoint not accepting connections yet - keep polling.
			} catch (JsonException) {
				// Partial or empty body during startup - keep polling.
			}
			await Task.Delay(delayMs, ct).ConfigureAwait(false);
		}
		throw new InvalidOperationException(
			"Error: could not obtain a CDP page target from the launched browser.");
	}

	/// <summary>The <c>webSocketDebuggerUrl</c> of the first <c>page</c> target in a <c>/json</c> listing, or null.</summary>
	internal static string FindPageWebSocketUrl(string listing) {
		using JsonDocument doc = JsonDocument.Parse(listing);
		if (doc.RootElement.ValueKind != JsonValueKind.Array) {
			return null;
		}
		foreach (JsonElement target in doc.RootElement.EnumerateArray()) {
			if (target.TryGetProperty("type", out JsonElement type) && type.GetString() == "page"
				&& target.TryGetProperty("webSocketDebuggerUrl", out JsonElement ws)) {
				string url = ws.GetString();
				if (!string.IsNullOrEmpty(url)) {
					return url;
				}
			}
		}
		return null;
	}
}
