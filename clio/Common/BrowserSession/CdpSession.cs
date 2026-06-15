using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="ICdpSession" />
public sealed class CdpSession : ICdpSession {
	// ~4s: once the port file exists the DevTools /json endpoint answers almost immediately, so a short
	// budget here fails fast on a broken launch instead of hanging.
	private const int PageTargetPollAttempts = 16;
	private const int PollDelayMs = 250;

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ICdpConnection _connection;
	private int _commandId = 1;
	private bool _disposed;

	/// <summary>Initializes the session with the collaborators it needs to discover and drive a page target.</summary>
	/// <param name="httpClientFactory">Creates the HTTP client used for the local DevTools JSON endpoint.</param>
	/// <param name="connection">The WebSocket transport seam.</param>
	public CdpSession(IHttpClientFactory httpClientFactory, ICdpConnection connection) {
		_httpClientFactory = httpClientFactory;
		_connection = connection;
	}

	/// <inheritdoc />
	public async Task ConnectAsync(int devToolsPort, CancellationToken ct = default) {
		string pageWebSocketUrl = await FindPageTargetAsync(devToolsPort, ct).ConfigureAwait(false);
		await _connection.ConnectAsync(new Uri(pageWebSocketUrl), ct).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<JsonElement> SendAsync(string method, object @params, CancellationToken ct = default) {
		int id = _commandId++;
		string payload = JsonSerializer.Serialize(new { id, method, @params });
		await _connection.SendTextAsync(payload, ct).ConfigureAwait(false);

		while (true) {
			ct.ThrowIfCancellationRequested();
			string response = await _connection.ReceiveTextAsync(ct).ConfigureAwait(false);
			if (string.IsNullOrEmpty(response) || !_connection.IsOpen) {
				throw new InvalidOperationException($"WebSocket closed while waiting for CDP response id={id}.");
			}
			using JsonDocument doc = JsonDocument.Parse(response);
			JsonElement root = doc.RootElement;
			// Skip interleaved CDP events (frames without our id).
			if (!root.TryGetProperty("id", out JsonElement idElement)
				|| !idElement.TryGetInt32(out int responseId) || responseId != id) {
				continue;
			}
			// A CDP error response has the form {"id":N,"error":{"code":…,"message":"…"}}. Treat any error as
			// a hard failure — silently accepting it would report success while the page never changed.
			if (root.TryGetProperty("error", out JsonElement errorElem)) {
				string message = errorElem.ValueKind == JsonValueKind.Object
					&& errorElem.TryGetProperty("message", out JsonElement messageElem)
					? messageElem.GetString() ?? "unknown CDP error"
					: errorElem.ToString();
				throw new InvalidOperationException($"CDP command '{method}' (id={id}) returned an error: {message}");
			}
			// Clone so the value survives disposal of the JsonDocument.
			return root.TryGetProperty("result", out JsonElement resultElem) ? resultElem.Clone() : default;
		}
	}

	/// <inheritdoc />
	public async Task<JsonElement> EvaluateAsync(string expression, bool awaitPromise = true, CancellationToken ct = default) {
		JsonElement result = await SendAsync("Runtime.evaluate",
			new { expression, awaitPromise, returnByValue = true }, ct).ConfigureAwait(false);

		// CDP Runtime.evaluate result shape: { result: { type, value, … }, exceptionDetails? }.
		if (result.ValueKind == JsonValueKind.Object
			&& result.TryGetProperty("exceptionDetails", out JsonElement exceptionDetails)) {
			string text = exceptionDetails.TryGetProperty("text", out JsonElement textElem)
				? textElem.GetString() ?? "Runtime.evaluate exception"
				: "Runtime.evaluate exception";
			throw new InvalidOperationException($"Runtime.evaluate failed: {text}");
		}
		if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("result", out JsonElement inner)
			&& inner.TryGetProperty("value", out JsonElement value)) {
			return value.Clone();
		}
		return default;
	}

	// Resolves a CDP page target's WebSocket URL from the local DevTools JSON endpoint.
	// NOTE: http://127.0.0.1:{port}/json is the browser's own local control endpoint, NOT a Creatio
	// service, so it deliberately does NOT go through IApplicationClient (which is Creatio-only).
	private async Task<string> FindPageTargetAsync(int port, CancellationToken ct) {
		using HttpClient http = _httpClientFactory.CreateClient();
		string listUrl = $"http://127.0.0.1:{port}/json";
		for (int attempt = 0; attempt < PageTargetPollAttempts; attempt++) {
			ct.ThrowIfCancellationRequested();
			try {
				string body = await http.GetStringAsync(listUrl, ct).ConfigureAwait(false);
				using JsonDocument doc = JsonDocument.Parse(body);
				foreach (JsonElement target in doc.RootElement.EnumerateArray()) {
					if (target.TryGetProperty("type", out JsonElement type) && type.GetString() == "page"
						&& target.TryGetProperty("webSocketDebuggerUrl", out JsonElement ws)) {
						string url = ws.GetString();
						if (!string.IsNullOrEmpty(url)) {
							return url;
						}
					}
				}
			} catch (HttpRequestException) {
				// Endpoint not accepting connections yet — keep polling.
			} catch (JsonException) {
				// Partial/empty body during startup — keep polling.
			}
			await Task.Delay(PollDelayMs, ct).ConfigureAwait(false);
		}
		throw new InvalidOperationException(
			"Error: could not obtain a CDP page target from the launched browser.");
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		if (_disposed) {
			return;
		}
		_disposed = true;
		await _connection.DisposeAsync().ConfigureAwait(false);
	}
}
