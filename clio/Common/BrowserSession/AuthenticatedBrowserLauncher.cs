using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="IAuthenticatedBrowserLauncher" />
public sealed class AuthenticatedBrowserLauncher : IAuthenticatedBrowserLauncher {
	private const int PollAttempts = 80;
	private const int PollDelayMs = 250;

	private readonly IChromiumLocator _chromiumLocator;
	private readonly IProcessExecutor _processExecutor;
	private readonly IFileSystem _fileSystem;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ILogger _logger;

	/// <summary>Initializes the launcher with the collaborators needed to start a browser and drive CDP.</summary>
	/// <param name="chromiumLocator">Locates the browser executable.</param>
	/// <param name="processExecutor">Launches the browser process.</param>
	/// <param name="fileSystem">Reads the storageState and the <c>DevToolsActivePort</c> handshake file.</param>
	/// <param name="httpClientFactory">Creates the HTTP client used for the local DevTools JSON endpoint.</param>
	/// <param name="logger">Diagnostics sink (cookie NAMES only — never values).</param>
	public AuthenticatedBrowserLauncher(IChromiumLocator chromiumLocator, IProcessExecutor processExecutor,
		IFileSystem fileSystem, IHttpClientFactory httpClientFactory, ILogger logger) {
		_chromiumLocator = chromiumLocator;
		_processExecutor = processExecutor;
		_fileSystem = fileSystem;
		_httpClientFactory = httpClientFactory;
		_logger = logger;
	}

	/// <inheritdoc />
	public async Task LaunchAsync(EnvironmentSettings env, string storageStatePath, CancellationToken ct = default) {
		ArgumentNullException.ThrowIfNull(env);

		// Throws ChromiumNotFoundException, which the command turns into the canonical AC-04 error —
		// it must never silently fall back to an unauthenticated launch.
		string browserPath = _chromiumLocator.Locate();
		IReadOnlyList<BrowserCookie> cookies = StorageStateJson.ParseCookies(_fileSystem.ReadAllText(storageStatePath));

		// Isolated profile so this debugging session never collides with the user's everyday browser
		// profile; also where Chromium writes the DevToolsActivePort handshake file we read below.
		string userDataDir = Path.Combine(Path.GetTempPath(), "clio-auth-profile-" + Guid.NewGuid().ToString("N"));

		// --remote-debugging-port=0 lets Chromium pick a free loopback port (avoids collisions) and
		// write it to <user-data-dir>/DevToolsActivePort; binding defaults to 127.0.0.1 (the endpoint
		// is unauthenticated, so it must stay loopback-only). Start on about:blank, inject, then navigate.
		string arguments =
			"--remote-debugging-port=0 " +
			$"--user-data-dir=\"{userDataDir}\" " +
			"--no-first-run --no-default-browser-check --new-window about:blank";
		ProcessLaunchResult launch = await _processExecutor
			.FireAndForgetAsync(new ProcessExecutionOptions(browserPath, arguments)).ConfigureAwait(false);
		if (!launch.Started) {
			throw new InvalidOperationException(
				$"Error: failed to launch the browser at '{browserPath}'. {launch.ErrorMessage}".TrimEnd());
		}

		int port = await ReadDevToolsPortAsync(userDataDir, ct).ConfigureAwait(false);
		string pageWebSocketUrl = await FindPageTargetAsync(port, ct).ConfigureAwait(false);
		await InjectCookiesAndNavigateAsync(pageWebSocketUrl, cookies, env.Uri, ct).ConfigureAwait(false);

		// Cookie NAMES are safe to log; VALUES are bearer secrets and must never be logged.
		_logger.WriteInfo($"Opened an authenticated browser session at {env.Uri} " +
			$"(injected {cookies.Count} session cookie(s)).");
	}

	// Reads the port Chromium chose from the DevToolsActivePort handshake file (line 1). Polls because
	// the file appears a moment after process start.
	private async Task<int> ReadDevToolsPortAsync(string userDataDir, CancellationToken ct) {
		string portFile = Path.Combine(userDataDir, "DevToolsActivePort");
		for (int attempt = 0; attempt < PollAttempts; attempt++) {
			ct.ThrowIfCancellationRequested();
			if (_fileSystem.ExistsFile(portFile)) {
				string firstLine = _fileSystem.ReadAllText(portFile)
					.Split('\n', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } lines
					? lines[0].Trim()
					: string.Empty;
				if (int.TryParse(firstLine, out int port) && port > 0) {
					return port;
				}
			}
			await Task.Delay(PollDelayMs, ct).ConfigureAwait(false);
		}
		throw new InvalidOperationException(
			"Error: timed out waiting for the browser's remote-debugging endpoint (DevToolsActivePort).");
	}

	// Resolves a CDP page target's WebSocket URL from the local DevTools JSON endpoint.
	// NOTE: http://127.0.0.1:{port}/json is the browser's own local control endpoint, NOT a Creatio
	// service, so it deliberately does NOT go through IApplicationClient (which is Creatio-only).
	private async Task<string> FindPageTargetAsync(int port, CancellationToken ct) {
		using HttpClient http = _httpClientFactory.CreateClient();
		string listUrl = $"http://127.0.0.1:{port}/json";
		for (int attempt = 0; attempt < PollAttempts; attempt++) {
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

	// Drives the page-level CDP session: enable Network, set every harvested cookie (HttpOnly included —
	// the whole point, since document.cookie cannot), then navigate to the Creatio URI.
	private static async Task InjectCookiesAndNavigateAsync(string pageWebSocketUrl,
		IReadOnlyList<BrowserCookie> cookies, string navigateUrl, CancellationToken ct) {
		// ClientWebSocket is a framework I/O transport (like Process/HttpClient), not a DI-managed
		// behavior service, so it is constructed locally per connection.
		using var ws = new ClientWebSocket();
		await ws.ConnectAsync(new Uri(pageWebSocketUrl), ct).ConfigureAwait(false);

		int id = 1;
		await CdpSendAsync(ws, id++, "Network.enable", new { }, ct).ConfigureAwait(false);
		foreach (BrowserCookie cookie in cookies) {
			await CdpSendAsync(ws, id++, "Network.setCookie", BuildSetCookieParams(cookie, navigateUrl), ct)
				.ConfigureAwait(false);
		}
		await CdpSendAsync(ws, id++, "Page.enable", new { }, ct).ConfigureAwait(false);
		await CdpSendAsync(ws, id++, "Page.navigate", new { url = navigateUrl }, ct).ConfigureAwait(false);

		if (ws.State == WebSocketState.Open) {
			await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct).ConfigureAwait(false);
		}
	}

	private static Dictionary<string, object> BuildSetCookieParams(BrowserCookie cookie, string fallbackUrl) {
		var param = new Dictionary<string, object> {
			["name"] = cookie.Name,
			["value"] = cookie.Value,
			["path"] = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
			["httpOnly"] = cookie.HttpOnly,
			["secure"] = cookie.Secure,
			["sameSite"] = NormalizeSameSite(cookie.SameSite)
		};
		if (!string.IsNullOrEmpty(cookie.Domain)) {
			param["domain"] = cookie.Domain;
		} else {
			param["url"] = fallbackUrl;
		}
		if (cookie.Expires > 0) {
			param["expires"] = cookie.Expires;
		}
		return param;
	}

	// CDP accepts only Strict | Lax | None for sameSite; default to Lax for anything else.
	private static string NormalizeSameSite(string sameSite) => sameSite?.ToLowerInvariant() switch {
		"strict" => "Strict",
		"none" => "None",
		_ => "Lax"
	};

	// Sends one CDP command and drains frames until the response with the matching id arrives
	// (interleaved CDP events without an id are skipped).
	private static async Task CdpSendAsync(ClientWebSocket ws, int id, string method, object @params,
		CancellationToken ct) {
		byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, method, @params }));
		await ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);

		while (true) {
			ct.ThrowIfCancellationRequested();
			string response = await ReceiveTextAsync(ws, ct).ConfigureAwait(false);
			using JsonDocument doc = JsonDocument.Parse(response);
			if (doc.RootElement.TryGetProperty("id", out JsonElement idElement)
				&& idElement.TryGetInt32(out int responseId) && responseId == id) {
				return;
			}
		}
	}

	private static async Task<string> ReceiveTextAsync(ClientWebSocket ws, CancellationToken ct) {
		byte[] buffer = new byte[8192];
		using var stream = new MemoryStream();
		WebSocketReceiveResult result;
		do {
			result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
			stream.Write(buffer, 0, result.Count);
		} while (!result.EndOfMessage);
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}
}
