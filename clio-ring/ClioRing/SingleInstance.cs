using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using ClioRing.Diagnostics;

namespace ClioRing;

/// <summary>
/// Single-instance guard with PER-EXECUTABLE-PATH identity. The first process for a given install
/// location owns a named mutex and listens on a named event; a second launch of the SAME build signals
/// that event (asking the first to show/activate) and exits. Because the identity is derived from the
/// running executable's normalized absolute path, DIFFERENT builds (dev / preview / ipc-preview /
/// install) get distinct identities and never cross-activate — clicking one build always surfaces that
/// build, not a different one that happens to be running. Prefers a machine-wide <c>Global\</c> name,
/// falling back to per-session <c>Local\</c> if that is denied.
/// </summary>
public static class SingleInstance {
	private static readonly string InstanceId = ComputeInstanceId(AppContext.BaseDirectory);

	private static Mutex? _mutex;
	private static EventWaitHandle? _showEvent;
	private static string _baseName = $@"Local\clio-ring-{InstanceId}";

	/// <summary>
	/// The stable per-install identity token (a hash of the normalized executable directory) that scopes
	/// this build's mutex/event names. Exposed for diagnostics and tests.
	/// </summary>
	public static string Id => InstanceId;

	/// <summary>
	/// Derives a stable, deterministic identity from an executable base directory. The path is first
	/// normalized (resolved to a canonical absolute path, trailing separators trimmed, lower-cased for
	/// the case-insensitive Windows filesystem) so the same install is never seen as two identities due
	/// to casing/format differences, then hashed (SHA-256, first 6 bytes → 12 hex chars). Deterministic
	/// across processes and runs; AOT-safe (no reflection).
	/// </summary>
	/// <param name="baseDirectory">The executable directory (typically <see cref="AppContext.BaseDirectory"/>).</param>
	public static string ComputeInstanceId(string baseDirectory) {
		string normalized = NormalizePath(baseDirectory);
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
		return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
	}

	/// <summary>
	/// Normalizes a filesystem path for identity comparison: canonical absolute form, trailing directory
	/// separators removed, lower-cased. Falls back to the trimmed/lower-cased input if the path cannot be
	/// resolved (never throws).
	/// </summary>
	public static string NormalizePath(string path) {
		if (string.IsNullOrWhiteSpace(path)) {
			return string.Empty;
		}
		string result;
		try {
			result = Path.GetFullPath(path);
		}
		catch (Exception) {
			result = path;
		}
		result = result.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return result.ToLowerInvariant();
	}

	/// <summary>Attempts to become the primary instance for THIS build. Returns false if the same build is already running.</summary>
	public static bool TryAcquire() {
		foreach (string scope in new[] { "Global", "Local" }) {
			string name = $@"{scope}\clio-ring-{InstanceId}";
			try {
				var mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
				_mutex = mutex;
				_baseName = name;
				return createdNew;
			}
			catch (UnauthorizedAccessException) {
				// Not allowed to create at this scope — try the next candidate.
			}
			catch (IOException) {
			}
		}

		return true; // could not probe; behave as primary rather than block launch
	}

	/// <summary>Signals the already-running primary instance of THIS build to show/activate its window.</summary>
	public static void SignalExisting() {
		if (!OperatingSystem.IsWindows()) {
			return;
		}

		foreach (string scope in new[] { "Global", "Local" }) {
			string name = $@"{scope}\clio-ring-{InstanceId}-show";
			try {
				if (EventWaitHandle.TryOpenExisting(name, out EventWaitHandle? ev)) {
					ev.Set();
					ev.Dispose();
					return;
				}
			}
			catch (Exception) {
				// try next / give up
			}
		}
	}

	/// <summary>Starts a background listener that invokes <paramref name="onShowRequested"/> when a second launch of this build signals.</summary>
	public static void StartShowListener(Action onShowRequested) {
		if (!OperatingSystem.IsWindows()) {
			return;
		}

		try {
			_showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _baseName + "-show");
		}
		catch (Exception ex) {
			StartupLog.Log($"single-instance listener unavailable: {ex.Message}");
			return;
		}

		var thread = new Thread(() => {
			while (true) {
				try {
					_showEvent.WaitOne();
					onShowRequested();
				}
				catch (Exception) {
					break;
				}
			}
		}) {
			IsBackground = true,
			Name = "clio-ring-single-instance"
		};
		thread.Start();
	}

	/// <summary>
	/// Best-effort detection of ANOTHER clio-ring build (same executable name, DIFFERENT install
	/// directory) already running on this machine. Returns its details for a cross-build notice, or null
	/// when no different build is running (or detection is not possible). Never throws.
	/// </summary>
	public static CrossBuildInfo? DetectOtherRunningBuild() {
		if (!OperatingSystem.IsWindows()) {
			return null;
		}
		try {
			using Process self = Process.GetCurrentProcess();
			string selfExe = SafeModulePath(self);
			string selfDir = NormalizePath(string.IsNullOrEmpty(selfExe) ? AppContext.BaseDirectory : Path.GetDirectoryName(selfExe) ?? "");

			foreach (Process other in Process.GetProcessesByName(self.ProcessName)) {
				try {
					if (other.Id == self.Id) {
						continue;
					}
					string otherExe = SafeModulePath(other);
					if (string.IsNullOrEmpty(otherExe)) {
						continue;
					}
					string otherDir = NormalizePath(Path.GetDirectoryName(otherExe) ?? "");
					if (!string.Equals(otherDir, selfDir, StringComparison.Ordinal)) {
						return DescribeBuild(otherExe);
					}
				}
				catch (Exception) {
					// Access denied / process exited mid-scan — skip it.
				}
				finally {
					other.Dispose();
				}
			}
		}
		catch (Exception) {
			// Enumeration not available; treat as no conflict.
		}
		return null;
	}

	private static string SafeModulePath(Process p) {
		try {
			return p.MainModule?.FileName ?? string.Empty;
		}
		catch (Exception) {
			return string.Empty;
		}
	}

	private static CrossBuildInfo DescribeBuild(string exePath) {
		string version = "?";
		try {
			FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
			version = fvi.ProductVersion ?? fvi.FileVersion ?? "?";
		}
		catch (Exception) {
		}

		string channel = ReadChannel(exePath);
		return new CrossBuildInfo(exePath, version, channel);
	}

	// Reads the "Channel" from the app-settings.json next to the other build's exe (best-effort,
	// AOT-safe via JsonDocument). Returns "?" when unavailable.
	private static string ReadChannel(string exePath) {
		try {
			string? dir = Path.GetDirectoryName(exePath);
			if (dir is null) {
				return "?";
			}
			string settings = Path.Combine(dir, "app-settings.json");
			if (!File.Exists(settings)) {
				return "?";
			}
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(settings));
			if (doc.RootElement.ValueKind == JsonValueKind.Object
				&& doc.RootElement.TryGetProperty("Channel", out JsonElement ch)
				&& ch.ValueKind == JsonValueKind.String) {
				return ch.GetString() ?? "?";
			}
		}
		catch (Exception) {
		}
		return "?";
	}
}

/// <summary>Details of a different clio-ring build detected running from another install location.</summary>
/// <param name="ExecutablePath">Full path to the other build's executable.</param>
/// <param name="Version">The other build's product/file version (best-effort).</param>
/// <param name="Channel">The other build's channel from its app-settings.json (best-effort).</param>
public sealed record CrossBuildInfo(string ExecutablePath, string Version, string Channel) {
	/// <summary>A one-line, user-facing description, e.g. <c>C:\Tools\clio-ring-dev\... · dev · 0.1.0-spike</c>.</summary>
	public string Describe() => $"{ExecutablePath} · {Channel} · {Version}";
}
