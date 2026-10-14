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
	/// <param name="browserPath">The browser executable, chosen by the caller (page-smoke asks the user first).</param>
	/// <param name="ct">Cancels the launch; a browser already started is killed and its profile deleted.</param>
	/// <returns>The running browser; disposing it kills the process tree and deletes the profile.</returns>
	Task<IHeadlessBrowser> LaunchAsync(string browserPath, CancellationToken ct);
}

/// <summary>A running headless browser. Disposal never throws.</summary>
public interface IHeadlessBrowser : IAsyncDisposable {

	/// <summary>The WebSocket URL of the browser's page target.</summary>
	Uri PageWebSocketUrl { get; }

	/// <summary>The browser's throw-away profile directory, deleted on disposal.</summary>
	string ProfileDirectory { get; }
}

/// <inheritdoc cref="IHeadlessBrowserLauncher" />
public sealed class HeadlessBrowserLauncher(
	IProcessExecutor processExecutor,
	IFileSystem fileSystem,
	IFileSecurityHardening fileSecurityHardening,
	IHttpClientFactory httpClientFactory) : IHeadlessBrowserLauncher {

	internal const string ProfilePrefix = "clio-headless-profile-";
	// ~30 s: a cold Chromium start can take a while to write DevToolsActivePort - measured past 15 s on a loaded
	// windows-latest agent running the MCP e2e suite. Still well inside page-smoke's 100 s budget.
	private const int PortFilePollAttempts = 120;
	private const int PageTargetPollAttempts = 16;
	private const int PollDelayMs = 250;
	// A profile this old belongs to no running check (every check ends well within it), so it is a leftover.
	private static readonly TimeSpan StaleProfileAge = TimeSpan.FromMinutes(10);

	/// <inheritdoc />
	public async Task<IHeadlessBrowser> LaunchAsync(string browserPath, CancellationToken ct) {
		DeleteStaleProfiles();
		string userDataDir = Path.Combine(Path.GetTempPath(), ProfilePrefix + Guid.NewGuid().ToString("N"));
		fileSystem.CreateDirectory(userDataDir, throwWhenExists: true);
		ProcessLaunchResult launch;
		try {
			// Owner-only (0700 on Unix) BEFORE the browser starts: the profile holds the session cookies and the
			// DevToolsActivePort file that names the unauthenticated debugging port, and on Linux it sits in a shared
			// /tmp. An empty directory is all another user could have seen before this line.
			fileSecurityHardening.HardenDirectory(userDataDir);
			launch = await processExecutor.FireAndForgetAsync(new ProcessExecutionOptions(browserPath, string.Empty) {
				ArgumentList = BuildArguments(userDataDir, RunsAsRootOnLinux()),
				// Chromium logs to stdout/stderr ("DevTools listening on ws://..."); inherited, that would land in the
				// MCP JSON-RPC pipe of whichever process runs the check.
				DiscardOutput = true
			}).ConfigureAwait(false);
		}
		catch {
			DeleteProfile(fileSystem, userDataDir);
			throw;
		}
		// A handle around the process just started, not a service: it is excluded from the DI auto-scan.
#pragma warning disable CLIO001
		HeadlessBrowser browser = new(launch.ProcessId, launch.ProcessStartedAtUtc, userDataDir, fileSystem);
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
		catch (InvalidOperationException exception) when (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
			await browser.DisposeAsync().ConfigureAwait(false);
			throw new InvalidOperationException(exception.Message + " " + LinuxLibrariesHint, exception);
		}
		catch {
			await browser.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	// A browser that exits at once on Linux is most often missing shared libraries, which a fresh container or a minimal
	// server lacks; its own stderr is discarded (it would land in the MCP stream), so the likely cause is named here.
	internal const string LinuxLibrariesHint = "On Linux a browser that never starts usually lacks system libraries "
		+ "(for example libnss3, libatk-bridge2.0-0, libgbm1, libxkbcommon0, libasound2); install them with the "
		+ "distribution's package manager, or run the browser once by hand to see which one is missing.";

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

	// The process is killed only while the id still names the browser that was started: identity is the id AND its start
	// time. Two seconds absorbs the precision of two reads of the same start time; a process that reused the id started
	// after the browser died, which is later than that.
	internal static bool IsSameProcess(DateTimeOffset recordedStart, DateTimeOffset observedStart) =>
		Math.Abs((observedStart - recordedStart).TotalSeconds) < StartTimeToleranceSeconds;

	private const double StartTimeToleranceSeconds = 2;

	private sealed class HeadlessBrowser(int? processId, DateTimeOffset? startedAtUtc, string userDataDir,
		IFileSystem fileSystem) : IHeadlessBrowser {

		private const int ExitWaitMs = 5_000;
		private const int DeleteAttempts = 5;
		private const int DeleteRetryMs = 200;
		private int _disposed;

		public Uri PageWebSocketUrl { get; set; }

		public string ProfileDirectory => userDataDir;

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
			// Without a recorded start time there is no identity to check, and the id alone may name a stranger by now. A
			// start time is unreadable only when the browser exited at once, so there is nothing of ours to kill then.
			if (processId is null || startedAtUtc is null) {
				return;
			}
			try {
				// Direct use of System.Diagnostics.Process is intentional, as in AuthenticatedBrowserLauncher: this
				// terminates the browser IProcessExecutor launched for us, and IProcessExecutor does not own kills.
#pragma warning disable CLIO004
				using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId.Value);
				// The browser can die mid-run and its id be reused before disposal; a stranger's tree is never killed.
				if (!IsSameProcess(startedAtUtc.Value, new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero))) {
					return;
				}
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
