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
	private const string ProfilePrefix = "clio-auth-profile-";
	// ~20s: a cold Chromium start can take a while to write DevToolsActivePort.
	private const int PortFilePollAttempts = 80;
	// ~4s: once the port file exists the DevTools /json endpoint answers almost immediately, so a
	// short budget here fails fast on a broken launch instead of hanging for another 20s.
	private const int PageTargetPollAttempts = 16;
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

		// Best-effort: remove profiles left behind by earlier runs before creating a new one, so these
		// temp directories do not accumulate unbounded. Profiles still locked by a browser that is open
		// from a previous run are skipped (deletion throws) and cleaned up on a later run.
		CleanupStaleProfiles();

		// Isolated profile so this debugging session never collides with the user's everyday browser
		// profile; also where Chromium writes the DevToolsActivePort handshake file we read below.
		string userDataDir = Path.Combine(Path.GetTempPath(), ProfilePrefix + Guid.NewGuid().ToString("N"));

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

		string shellUrl = BuildShellUrl(env);

		// If the CDP inject-and-navigate phase hangs (WebSocket never returns the expected id), the
		// outer ct might be CancellationToken.None and ThrowIfCancellationRequested() would be a no-op,
		// leaving the loop running indefinitely. Guard with a per-launch timeout linked to the caller's
		// token so Ctrl-C is still honoured and the command always terminates.
		using var cdpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cdpCts.CancelAfter(TimeSpan.FromSeconds(15));

		int? browserPid = launch.ProcessId;
		try {
			int port = await ReadDevToolsPortAsync(userDataDir, cdpCts.Token).ConfigureAwait(false);
			string pageWebSocketUrl = await FindPageTargetAsync(port, cdpCts.Token).ConfigureAwait(false);
			await InjectCookiesAndNavigateAsync(pageWebSocketUrl, cookies, env.Uri, shellUrl, cdpCts.Token)
				.ConfigureAwait(false);
		} catch {
			// On any CDP-phase failure the browser window is already open but cookies were never injected,
			// so the user would land on the login form — the exact outcome this command promises to avoid.
			// Kill the orphaned process before re-throwing so no stale window is left behind.
			if (browserPid.HasValue) {
				try {
					// Direct use of System.Diagnostics.Process is intentional: we are terminating a
					// specific browser process that IProcessExecutor launched for us. IProcessExecutor
					// handles launches and captures; it does not (and should not) own cleanup/kill.
#pragma warning disable CLIO004
					System.Diagnostics.Process.GetProcessById(browserPid.Value).Kill(entireProcessTree: true);
#pragma warning restore CLIO004
				} catch (ArgumentException) {
					// Process already exited before we could kill it.
				} catch (InvalidOperationException) {
					// Process not accessible (e.g. cross-user on Windows) — best effort.
				}
			}
			throw;
		}

		// Cookie NAMES are safe to log; VALUES are bearer secrets and must never be logged.
		_logger.WriteInfo($"Opened an authenticated browser session at {shellUrl} " +
			$"(injected {cookies.Count} session cookie(s)).");
	}

	// The authenticated entry point is the Shell, NOT the bare site root: navigating to {Uri}/ can
	// render the login form even with a valid session cookie, whereas {Uri}/0/Shell/ (NetFramework)
	// or {Uri}/Shell/ (NetCore) honours the injected .ASPXAUTH cookie and lands on the workspace.
	// Live-verified 2026-06-10. Mirrors EnvironmentSettings.SimpleloginUri's IsNetCore split, minus the
	// ?simplelogin=true form (which would force the manual login form we are bypassing).
	internal static string BuildShellUrl(EnvironmentSettings env) {
		string baseUri = env.Uri.TrimEnd('/');
		return baseUri + (env.IsNetCore ? "/Shell/" : "/0/Shell/");
	}

	// Best-effort removal of profile directories from earlier --authenticated runs. Failures (a profile
	// still locked by an open browser on Windows, or a permissions issue) are swallowed per directory so
	// cleanup never blocks the launch; such a profile is simply retried on a future run.
	private void CleanupStaleProfiles() {
		try {
			foreach (string dir in _fileSystem.GetDirectories(
				Path.GetTempPath(), ProfilePrefix + "*", SearchOption.TopDirectoryOnly)) {
				try {
					_fileSystem.DeleteDirectory(dir, recursive: true);
				} catch (IOException) {
					// In use by a browser still open from a previous run — leave it for next time.
				} catch (UnauthorizedAccessException) {
					// Locked / insufficient permissions — leave it.
				}
			}
		} catch (IOException) {
			// Temp directory not enumerable — skip cleanup entirely.
		} catch (UnauthorizedAccessException) {
			// No access to the temp directory — skip cleanup entirely.
		}
	}

	// Reads the port Chromium chose from the DevToolsActivePort handshake file (line 1). Polls because
	// the file appears a moment after process start.
	private async Task<int> ReadDevToolsPortAsync(string userDataDir, CancellationToken ct) {
		string portFile = Path.Combine(userDataDir, "DevToolsActivePort");
		for (int attempt = 0; attempt < PortFilePollAttempts; attempt++) {
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

	// Drives the page-level CDP session: enable Network, set every harvested cookie (HttpOnly included —
	// the whole point, since document.cookie cannot), then navigate to the Creatio URI.
	private static async Task InjectCookiesAndNavigateAsync(string pageWebSocketUrl,
		IReadOnlyList<BrowserCookie> cookies, string cookieFallbackUrl, string navigateUrl, CancellationToken ct) {
		// ClientWebSocket is a framework I/O transport (like Process/HttpClient), not a DI-managed
		// behavior service, so it is constructed locally per connection.
		using var ws = new ClientWebSocket();
		await ws.ConnectAsync(new Uri(pageWebSocketUrl), ct).ConfigureAwait(false);

		int id = 1;
		await CdpSendAsync(ws, id++, "Network.enable", new { }, ct).ConfigureAwait(false);
		foreach (BrowserCookie cookie in cookies) {
			await CdpSendAsync(ws, id++, "Network.setCookie", BuildSetCookieParams(cookie, cookieFallbackUrl), ct)
				.ConfigureAwait(false);
		}
		await CdpSendAsync(ws, id++, "Page.enable", new { }, ct).ConfigureAwait(false);
		await CdpSendAsync(ws, id, "Page.navigate", new { url = navigateUrl }, ct).ConfigureAwait(false);

		if (ws.State == WebSocketState.Open) {
			await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct).ConfigureAwait(false);
		}
	}

	internal static Dictionary<string, object> BuildSetCookieParams(BrowserCookie cookie, string fallbackUrl) {
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
			if (string.IsNullOrEmpty(response) || ws.State != WebSocketState.Open) {
				throw new InvalidOperationException($"WebSocket closed while waiting for CDP response id={id}.");
			}
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
			await stream.WriteAsync(buffer.AsMemory(0, result.Count), ct).ConfigureAwait(false);
		} while (!result.EndOfMessage);
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}
}
