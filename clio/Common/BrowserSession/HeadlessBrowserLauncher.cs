using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>Starts a headless Chromium with a throw-away profile and a loopback DevTools endpoint.</summary>
public interface IHeadlessBrowserLauncher {

	/// <summary>Launches the browser and waits for its page target.</summary>
	/// <param name="ct">Cancels the launch; a browser already started is killed and its profile deleted.</param>
	/// <returns>The running browser; disposing it kills the process tree and deletes the profile.</returns>
	/// <exception cref="ChromiumNotFoundException">No Chromium-based browser could be located.</exception>
	Task<IHeadlessBrowser> LaunchAsync(CancellationToken ct);
}

/// <summary>A running headless browser. Disposal never throws.</summary>
public interface IHeadlessBrowser : IAsyncDisposable {

	/// <summary>The WebSocket URL of the browser's page target.</summary>
	Uri PageWebSocketUrl { get; }
}

/// <inheritdoc cref="IHeadlessBrowserLauncher" />
public sealed class HeadlessBrowserLauncher(
	IChromiumLocator chromiumLocator,
	IProcessExecutor processExecutor,
	IFileSystem fileSystem,
	IHttpClientFactory httpClientFactory) : IHeadlessBrowserLauncher {

	internal const string ProfilePrefix = "clio-headless-profile-";
	// ~15 s: a cold Chromium start can take a while to write DevToolsActivePort.
	private const int PortFilePollAttempts = 60;
	private const int PageTargetPollAttempts = 16;
	private const int PollDelayMs = 250;
	// A profile this old belongs to no running check (every check ends well within it), so it is a leftover.
	private static readonly TimeSpan StaleProfileAge = TimeSpan.FromMinutes(10);

	/// <inheritdoc />
	public async Task<IHeadlessBrowser> LaunchAsync(CancellationToken ct) {
		string browserPath = chromiumLocator.Locate();
		DeleteStaleProfiles();
		string userDataDir = Path.Combine(Path.GetTempPath(), ProfilePrefix + Guid.NewGuid().ToString("N"));
		fileSystem.CreateDirectory(userDataDir);
		ProcessLaunchResult launch;
		try {
			launch = await processExecutor.FireAndForgetAsync(new ProcessExecutionOptions(browserPath, string.Empty) {
				ArgumentList = BuildArguments(userDataDir, RunsAsRootOnLinux())
			}).ConfigureAwait(false);
		}
		catch {
			DeleteProfile(fileSystem, userDataDir);
			throw;
		}
		// A handle around the process just started, not a service: it is excluded from the DI auto-scan.
#pragma warning disable CLIO001
		HeadlessBrowser browser = new(launch.ProcessId, userDataDir, fileSystem);
#pragma warning restore CLIO001
		if (!launch.Started) {
			await browser.DisposeAsync().ConfigureAwait(false);
			throw new InvalidOperationException(
				$"Error: failed to launch the browser at '{browserPath}'. {launch.ErrorMessage}".TrimEnd());
		}
		try {
			int port = await DevToolsEndpoint.ReadPortAsync(fileSystem, userDataDir, PortFilePollAttempts, PollDelayMs, ct)
				.ConfigureAwait(false);
			string pageUrl = await DevToolsEndpoint.FindPageTargetAsync(httpClientFactory, port, PageTargetPollAttempts,
				PollDelayMs, ct).ConfigureAwait(false);
			browser.PageWebSocketUrl = new Uri(pageUrl);
			return browser;
		}
		catch {
			await browser.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	// --remote-debugging-port=0 lets Chromium pick a free loopback port and write it to DevToolsActivePort; the
	// endpoint is unauthenticated and binds to 127.0.0.1 only. The profile is new and private to this run, so nothing of
	// the user's own browser (cookies, extensions, sessions) is visible to the page or to the check.
	internal static IReadOnlyList<string> BuildArguments(string userDataDir, bool noSandbox) {
		List<string> arguments = [
			"--headless=new",
			"--remote-debugging-port=0",
			$"--user-data-dir={userDataDir}",
			"--no-first-run",
			"--no-default-browser-check",
			"--disable-extensions",
			"--disable-background-networking",
			"--disable-sync",
			"--mute-audio",
			// The profile's cookie store is encrypted with a key from the OS keychain. Without these a macOS keychain
			// lookup (or a Linux keyring) can block Network.setCookie indefinitely - it did whenever HOME pointed away
			// from the user's keychain - or raise an access prompt; the profile is thrown away, so a mock key is enough.
			"--use-mock-keychain",
			"--password-store=basic",
			"--window-size=1440,900"
		];
		if (noSandbox) {
			// Chromium refuses to start as root with its sandbox on; a CI agent running as root needs this.
			arguments.Add("--no-sandbox");
		}
		arguments.Add("about:blank");
		return arguments;
	}

	private static bool RunsAsRootOnLinux() =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
		&& string.Equals(Environment.UserName, "root", StringComparison.Ordinal);

	private void DeleteStaleProfiles() {
		try {
			DateTime cutoff = DateTime.UtcNow - StaleProfileAge;
			foreach (string dir in fileSystem.GetDirectories(Path.GetTempPath(), ProfilePrefix + "*",
					SearchOption.TopDirectoryOnly)) {
				if (fileSystem.GetDirectoryInfo(dir).CreationTimeUtc < cutoff) {
					DeleteProfile(fileSystem, dir);
				}
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// The temp directory is not enumerable; leave the leftovers.
		}
	}

	internal static bool DeleteProfile(IFileSystem fileSystem, string dir) {
		try {
			if (fileSystem.ExistsDirectory(dir)) {
				fileSystem.DeleteDirectory(dir, recursive: true);
			}
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// Still locked by a browser that is shutting down.
			return false;
		}
	}

	private sealed class HeadlessBrowser(int? processId, string userDataDir, IFileSystem fileSystem) : IHeadlessBrowser {

		private const int ExitWaitMs = 5_000;
		private const int DeleteAttempts = 5;
		private const int DeleteRetryMs = 200;
		private int _disposed;

		public Uri PageWebSocketUrl { get; set; }

		public async ValueTask DisposeAsync() {
			if (Interlocked.Exchange(ref _disposed, 1) == 1) {
				return;
			}
			Kill();
			// The browser releases its profile files a moment after it exits.
			for (int attempt = 0; attempt < DeleteAttempts; attempt++) {
				if (DeleteProfile(fileSystem, userDataDir)) {
					return;
				}
				await Task.Delay(DeleteRetryMs).ConfigureAwait(false);
			}
		}

		private void Kill() {
			if (processId is null) {
				return;
			}
			try {
				// Direct use of System.Diagnostics.Process is intentional, as in AuthenticatedBrowserLauncher: this
				// terminates the browser IProcessExecutor launched for us, and IProcessExecutor does not own kills.
#pragma warning disable CLIO004
				using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId.Value);
				process.Kill(entireProcessTree: true);
				process.WaitForExit(ExitWaitMs);
#pragma warning restore CLIO004
			}
			catch (ArgumentException) {
				// Already exited.
			}
			catch (InvalidOperationException) {
				// Exited between the lookup and the kill.
			}
			catch (System.ComponentModel.Win32Exception) {
				// Not accessible; nothing more can be done.
			}
		}
	}
}
