using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="IAuthenticatedBrowserLauncher" />
public sealed class AuthenticatedBrowserLauncher : IAuthenticatedBrowserLauncher {
	private const string ProfilePrefix = "clio-auth-profile-";
	// ~20s: a cold Chromium start can take a while to write DevToolsActivePort.
	private const int PortFilePollAttempts = 80;
	private const int PollDelayMs = 250;

	private readonly IChromiumLocator _chromiumLocator;
	private readonly IProcessExecutor _processExecutor;
	private readonly IFileSystem _fileSystem;
	private readonly ICdpSession _cdpSession;
	private readonly ILogger _logger;

	/// <summary>Initializes the launcher with the collaborators needed to start a browser and drive CDP.</summary>
	/// <param name="chromiumLocator">Locates the browser executable.</param>
	/// <param name="processExecutor">Launches the browser process.</param>
	/// <param name="fileSystem">Reads the storageState and the <c>DevToolsActivePort</c> handshake file.</param>
	/// <param name="cdpSession">The shared CDP session used to inject cookies and navigate.</param>
	/// <param name="logger">Diagnostics sink (cookie NAMES only — never values).</param>
	public AuthenticatedBrowserLauncher(IChromiumLocator chromiumLocator, IProcessExecutor processExecutor,
		IFileSystem fileSystem, ICdpSession cdpSession, ILogger logger) {
		_chromiumLocator = chromiumLocator;
		_processExecutor = processExecutor;
		_fileSystem = fileSystem;
		_cdpSession = cdpSession;
		_logger = logger;
	}

	/// <inheritdoc />
	public async Task LaunchAsync(EnvironmentSettings env, string storageStatePath, CancellationToken ct = default) {
		await LaunchAndKeepOpenAsync(env, storageStatePath, ct).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<LaunchResult> LaunchAndKeepOpenAsync(EnvironmentSettings env, string storageStatePath,
		CancellationToken ct = default) {
		ArgumentNullException.ThrowIfNull(env);

		// Throws ChromiumNotFoundException, which the command turns into the canonical AC-04 error —
		// it must never silently fall back to an unauthenticated launch.
		string browserPath = _chromiumLocator.Locate();
		IReadOnlyList<BrowserCookie> cookies = StorageStateJson.ParseCookies(_fileSystem.ReadAllText(storageStatePath));

		// Best-effort: remove profiles left behind by earlier runs before creating a new one, so these
		// temp directories do not accumulate unbounded.
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

		// If the CDP inject-and-navigate phase hangs, guard with a per-launch timeout linked to the caller's
		// token so Ctrl-C is still honoured and the command always terminates.
		using var cdpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cdpCts.CancelAfter(TimeSpan.FromSeconds(30));

		int? browserPid = launch.ProcessId;
		int port;
		try {
			port = await ReadDevToolsPortAsync(userDataDir, cdpCts.Token).ConfigureAwait(false);
			await InjectCookiesAndNavigateAsync(port, cookies, env.Uri, shellUrl, cdpCts.Token).ConfigureAwait(false);
			// Close our CDP session (the browser window stays open for the user / for a driver to re-attach).
			await _cdpSession.DisposeAsync().ConfigureAwait(false);
		} catch {
			// On any CDP-phase failure the browser window is already open but cookies were never injected,
			// so the user would land on the login form — the exact outcome this command promises to avoid.
			// Kill the orphaned process before re-throwing so no stale window is left behind.
			if (browserPid.HasValue) {
				try {
					// Direct use of System.Diagnostics.Process is intentional: we are terminating a
					// specific browser process that IProcessExecutor launched for us.
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
		return new LaunchResult(port);
	}

	// The authenticated entry point is the Shell, NOT the bare site root: navigating to {Uri}/ can
	// render the login form even with a valid session cookie, whereas {Uri}/0/Shell/ (NetFramework)
	// or {Uri}/Shell/ (NetCore) honours the injected .ASPXAUTH cookie and lands on the workspace.
	internal static string BuildShellUrl(EnvironmentSettings env) {
		string baseUri = env.Uri.TrimEnd('/');
		return baseUri + (env.IsNetCore ? "/Shell/" : "/0/Shell/");
	}

	// Best-effort removal of profile directories from earlier --authenticated runs. Failures are swallowed
	// per directory so cleanup never blocks the launch; such a profile is simply retried on a future run.
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

	// Drives the CDP session: connect to the page target, enable Network, set every harvested cookie
	// (HttpOnly included — the whole point, since document.cookie cannot), then navigate to the shell URL.
	private async Task InjectCookiesAndNavigateAsync(int devToolsPort, IReadOnlyList<BrowserCookie> cookies,
		string cookieFallbackUrl, string navigateUrl, CancellationToken ct) {
		await _cdpSession.ConnectAsync(devToolsPort, ct).ConfigureAwait(false);
		await _cdpSession.SendAsync("Network.enable", new { }, ct).ConfigureAwait(false);
		foreach (BrowserCookie cookie in cookies) {
			await _cdpSession.SendAsync("Network.setCookie", BuildSetCookieParams(cookie, cookieFallbackUrl), ct)
				.ConfigureAwait(false);
		}
		await _cdpSession.SendAsync("Page.enable", new { }, ct).ConfigureAwait(false);
		await _cdpSession.SendAsync("Page.navigate", new { url = navigateUrl }, ct).ConfigureAwait(false);
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
}
