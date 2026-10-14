using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Starts a headless Chromium with a throw-away profile, driven over a private DevTools pipe: no debugging port is opened,
/// so no other local process can attach to the browser and read the session it is given.
/// </summary>
public interface IHeadlessBrowserLauncher {

	/// <summary>Launches the browser and attaches a CDP session to its page.</summary>
	/// <param name="browserPath">The browser executable, chosen by the caller (page-smoke asks the user first).</param>
	/// <param name="ct">Cancels the launch; a browser already started is killed and its profile deleted.</param>
	/// <returns>The running browser; disposing it closes the session, kills the process tree and deletes the profile.</returns>
	/// <exception cref="InvalidOperationException">
	/// The browser could not be started with a DevTools pipe, or did not answer over it. There is no fallback to a
	/// debugging port.
	/// </exception>
	Task<IHeadlessBrowser> LaunchAsync(string browserPath, CancellationToken ct);
}

/// <summary>A running headless browser. Disposal never throws.</summary>
public interface IHeadlessBrowser : IAsyncDisposable {

	/// <summary>The CDP session attached to the browser's page; owned by the browser and closed on its disposal.</summary>
	ICdpSession Page { get; }

	/// <summary>The browser's throw-away profile directory, deleted on disposal.</summary>
	string ProfileDirectory { get; }
}

/// <inheritdoc cref="IHeadlessBrowserLauncher" />
public sealed class HeadlessBrowserLauncher(
	IPipeBrowserSpawner spawner,
	IFileSystem fileSystem,
	IFileSecurityHardening fileSecurityHardening) : IHeadlessBrowserLauncher {

	internal const string ProfilePrefix = "clio-headless-profile-";
	// ~30 s for the first answer over the pipe: a cold Chromium start was measured past 15 s on a loaded windows-latest
	// agent running the MCP e2e suite. Still well inside page-smoke's 100 s budget. A browser that dies sooner closes the
	// pipe, and the launch fails at once instead of waiting this out.
	private const int StartupTimeoutMs = 30_000;
	private const int PageTargetPollAttempts = 16;
	private const int PollDelayMs = 250;
	// A profile this old belongs to no running check (every check ends well within it), so it is a leftover.
	private static readonly TimeSpan StaleProfileAge = TimeSpan.FromMinutes(10);

	/// <inheritdoc />
	public async Task<IHeadlessBrowser> LaunchAsync(string browserPath, CancellationToken ct) {
		DeleteStaleProfiles();
		string userDataDir = Path.Combine(Path.GetTempPath(), ProfilePrefix + Guid.NewGuid().ToString("N"));
		fileSystem.CreateDirectory(userDataDir, throwWhenExists: true);
		PipeBrowserProcess process;
		try {
			// Owner-only (0700 on Unix) BEFORE the browser starts: the profile holds the session cookies, and on Linux it
			// sits in a shared /tmp. An empty directory is all another user could have seen before this line.
			fileSecurityHardening.HardenDirectory(userDataDir);
			process = StartBrowser(browserPath, userDataDir);
		}
		catch {
			DeleteProfile(fileSystem, userDataDir);
			throw;
		}
		// Handles around the process just started and its pipe, not services: excluded from the DI auto-scan.
#pragma warning disable CLIO001
		CdpSession session = new(new PipeCdpTransport(process.FromBrowser, process.ToBrowser));
		HeadlessBrowser browser = new(process, session, userDataDir, fileSystem);
#pragma warning restore CLIO001
		try {
			await AttachToPageAsync(session, ct).ConfigureAwait(false);
			return browser;
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested) {
			await browser.DisposeAsync().ConfigureAwait(false);
			throw new InvalidOperationException(ExplainNoAnswer(browserPath, exception), exception);
		}
		catch {
			await browser.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	private PipeBrowserProcess StartBrowser(string browserPath, string userDataDir) {
		try {
			return spawner.Start(browserPath, BuildArguments(userDataDir, RunsAsRootOnLinux()));
		}
		catch (Exception exception) when (exception is Win32Exception or PlatformNotSupportedException
			or IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException) {
			throw new InvalidOperationException(
				$"Error: failed to launch the browser at '{browserPath}' with a private DevTools pipe ({exception.Message}). "
				+ "page-smoke never falls back to an open debugging port.", exception);
		}
	}

	// The pipe reaches the browser target; the page started with about:blank appears among its targets a moment after
	// the browser answers, and is then attached as a flat session the CdpSession routes every command to.
	private static async Task AttachToPageAsync(CdpSession session, CancellationToken ct) {
		using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
		startup.CancelAfter(StartupTimeoutMs);
		for (int attempt = 0; attempt < PageTargetPollAttempts; attempt++) {
			JsonElement targets = await session.SendAsync("Target.getTargets", null, startup.Token).ConfigureAwait(false);
			string targetId = FindPageTargetId(targets);
			if (targetId is not null) {
				JsonElement attached = await session.SendAsync("Target.attachToTarget",
					new { targetId, flatten = true }, startup.Token).ConfigureAwait(false);
				session.RouteToSession(attached.GetProperty("sessionId").GetString());
				return;
			}
			await Task.Delay(PollDelayMs, startup.Token).ConfigureAwait(false);
		}
		throw new InvalidOperationException("Error: the browser opened no page to drive.");
	}

	/// <summary>The <c>targetId</c> of the first <c>page</c> target in a <c>Target.getTargets</c> result, or null.</summary>
	internal static string FindPageTargetId(JsonElement result) {
		if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("targetInfos", out JsonElement infos)
			|| infos.ValueKind != JsonValueKind.Array) {
			return null;
		}
		foreach (JsonElement target in infos.EnumerateArray()) {
			if (target.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String
				&& type.GetString() == "page" && target.TryGetProperty("targetId", out JsonElement id)
				&& id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString())) {
				return id.GetString();
			}
		}
		return null;
	}

	private static string ExplainNoAnswer(string browserPath, Exception exception) {
		string why = exception switch {
			OperationCanceledException => "it did not answer over its DevTools pipe in time",
			CdpSessionClosedException => "it exited, or closed its DevTools pipe, before it answered",
			_ => exception.Message.StartsWith("Error: ", StringComparison.Ordinal)
				? exception.Message["Error: ".Length..].TrimEnd('.')
				: exception.Message.TrimEnd('.')
		};
		string message = $"Error: the browser at '{browserPath}' could not be driven: {why}.";
		// The platform hints explain a browser that never answers at all, not one that answered and then refused.
		if (exception is OperationCanceledException or CdpSessionClosedException) {
			if (OperatingSystem.IsWindows()) {
				message += " " + WindowsVersionHint;
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				message += " " + LinuxLibrariesHint;
			}
		}
		return message;
	}

	// A Windows browser older than Chromium 117 ignores --remote-debugging-io-pipes and so never sees its pipe.
	internal const string WindowsVersionHint = "On Windows the browser must be Chromium 117 or later (Chrome, Edge), "
		+ "which can take its DevTools pipe from clio.";

	// A browser that exits at once on Linux is most often missing shared libraries, which a fresh container or a minimal
	// server lacks; its own stderr is discarded (it would land in the MCP stream), so the likely cause is named here.
	internal const string LinuxLibrariesHint = "On Linux a browser that never starts usually lacks system libraries "
		+ "(for example libnss3, libatk-bridge2.0-0, libgbm1, libxkbcommon0, libasound2); install them with the "
		+ "distribution's package manager, or run the browser once by hand to see which one is missing.";

	// No --remote-debugging-port: the spawner adds --remote-debugging-pipe, so DevTools is reachable only through the
	// pipe clio holds. The profile is new and private to this run, so nothing of the user's own browser (cookies,
	// extensions, sessions) is visible to the page or to the check.
	internal static IReadOnlyList<string> BuildArguments(string userDataDir, bool noSandbox) {
		List<string> arguments = [
			"--headless=new",
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

	private sealed class HeadlessBrowser(PipeBrowserProcess process, CdpSession session, string userDataDir,
		IFileSystem fileSystem) : IHeadlessBrowser {

		private const int ExitWaitMs = 5_000;
		private const int DeleteAttempts = 5;
		private const int DeleteRetryMs = 200;
		private int _disposed;

		public ICdpSession Page => session;

		public string ProfileDirectory => userDataDir;

		public async ValueTask DisposeAsync() {
			if (Interlocked.Exchange(ref _disposed, 1) == 1) {
				return;
			}
			try {
				// Closes the pipe, which is Chromium's own signal to shut down; the kill below is what guarantees it.
				await session.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException) {
				// Disposal never throws; the kill below still runs.
			}
			Kill();
			// The tree kill above needs a readable start time; the browser itself is clio's own (an unreaped child, an open
			// process handle), so it is ended directly as well, whatever that read gave.
			process.Terminate();
			// On Unix this also reaps the browser; on Windows it releases the process handle.
			process.WaitForExit(ExitWaitMs);
			process.Dispose();
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
			if (process.StartedAtUtc is null) {
				return;
			}
			try {
				// Direct use of System.Diagnostics.Process is intentional, as in AuthenticatedBrowserLauncher: this
				// terminates the browser the pipe spawner started, and IProcessExecutor does not own kills.
#pragma warning disable CLIO004
				using System.Diagnostics.Process running = System.Diagnostics.Process.GetProcessById(process.ProcessId);
				// The browser can die mid-run and its id be reused before disposal; a stranger's tree is never killed.
				if (!IsSameProcess(process.StartedAtUtc.Value,
						new DateTimeOffset(running.StartTime.ToUniversalTime(), TimeSpan.Zero))) {
					return;
				}
				running.Kill(entireProcessTree: true);
#pragma warning restore CLIO004
			}
			catch (ArgumentException) {
				// Already exited.
			}
			catch (InvalidOperationException) {
				// Exited between the lookup and the kill.
			}
			catch (Win32Exception) {
				// Not accessible; nothing more can be done.
			}
		}
	}
}
