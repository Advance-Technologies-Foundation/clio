using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ClioLauncher.Ipc;
using ClioLauncher.Models;
using ClioLauncher.Services;

namespace ClioLauncher.Diagnostics;

/// <summary>
/// The Ring's run-log facility (story 10, ADR D6). Logs and per-run deployment receipts live in a single
/// known folder: <c>app-settings.json</c> → <c>LogsFolder</c> when set, otherwise a <c>Logs</c> folder
/// next to the executable (<c>C:\Tools\clio-ring\Logs</c> for an installed build). This static surface
/// resolves that folder, opens it in the OS file browser ("Open logs"), and exposes a rotating, redacted,
/// best-effort writer. Nothing here ever throws — a diagnostic must never break the app (mirrors
/// <see cref="StartupLog"/>).
/// </summary>
public static class RingLog {
	private static readonly object Gate = new();
	private static string? _folder;
	private static RingLogWriter? _writer;

	/// <summary>
	/// Resolves the active logs folder from settings: <paramref name="settings"/>'s <c>LogsFolder</c> when
	/// non-blank, otherwise a <c>Logs</c> folder beside the executable. Pure — creates nothing.
	/// </summary>
	public static string ResolveLogsFolder(AppSettings? settings) {
		string? configured = settings?.LogsFolder;
		if (!string.IsNullOrWhiteSpace(configured)) {
			return configured.Trim();
		}
		return Path.Combine(AppContext.BaseDirectory, "Logs");
	}

	/// <summary>The active logs folder (resolved once from <c>app-settings.json</c> and created best-effort).</summary>
	public static string LogsFolder {
		get {
			lock (Gate) {
				if (_folder is not null) {
					return _folder;
				}
				_folder = ResolveLogsFolder(AppSettingsReader.TryRead());
				try {
					Directory.CreateDirectory(_folder);
				}
				catch (Exception) {
					// Fall back to a temp folder if the configured/derived one cannot be created.
					_folder = Path.Combine(Path.GetTempPath(), "clio-ring", "Logs");
					try { Directory.CreateDirectory(_folder); } catch (Exception) { /* give up quietly */ }
				}
				return _folder;
			}
		}
	}

	/// <summary>
	/// Appends a timestamped line to the active run log, redacting <paramref name="jsonContext"/> through the
	/// shared <see cref="SecretRedactor"/> before it touches disk. Best-effort; never throws.
	/// </summary>
	/// <param name="message">Developer-controlled, secret-free message.</param>
	/// <param name="jsonContext">Optional JSON context; scrubbed of credential-shaped values before writing.</param>
	public static void Log(string message, string? jsonContext = null) {
		lock (Gate) {
			_writer ??= new RingLogWriter(LogsFolder);
			_writer.Log(message, jsonContext);
		}
	}

	/// <summary>
	/// Opens the active logs folder in the OS file browser (the "Open logs" action). Best-effort: a missing
	/// shell handler or non-desktop platform is swallowed. Returns the folder it targeted.
	/// </summary>
	public static string OpenLogsFolder() {
		string folder = LogsFolder;
		try {
			Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
			StartupLog.Log($"open logs requested -> {folder}");
		}
		catch (System.ComponentModel.Win32Exception) {
			// No shell handler / blocked — ignore; the folder path is still known.
		}
		catch (System.PlatformNotSupportedException) {
			// Non-desktop platform — ignore.
		}
		catch (InvalidOperationException) {
			// No associated application — ignore.
		}
		return folder;
	}
}

/// <summary>
/// A rotating, redacted, append-only text log writer. Rotation is by file size (a new file is started when
/// the current one reaches the byte cap) and the folder is pruned to a bounded number of log files (oldest
/// first). Redaction routes JSON context through <see cref="SecretRedactor"/> so no credential-shaped value
/// reaches disk. Every write is best-effort and never throws.
/// </summary>
public sealed class RingLogWriter {
	private readonly object _gate = new();
	private readonly string _folder;
	private readonly long _maxBytesPerFile;
	private readonly int _maxFiles;
	private string? _currentFile;
	private int _rollCounter;

	/// <summary>Creates a writer over <paramref name="folder"/>.</summary>
	/// <param name="folder">Target folder for <c>ring-*.log</c> files.</param>
	/// <param name="maxBytesPerFile">Size cap before a new file is started. Default 1 MB.</param>
	/// <param name="maxFiles">Cap on retained <c>ring-*.log</c> files (oldest pruned first). Default 10.</param>
	public RingLogWriter(string folder, long maxBytesPerFile = 1024L * 1024, int maxFiles = 10) {
		_folder = folder ?? throw new ArgumentNullException(nameof(folder));
		_maxBytesPerFile = Math.Max(1, maxBytesPerFile);
		_maxFiles = Math.Max(1, maxFiles);
	}

	/// <summary>Appends a timestamped, redacted line. Best-effort; never throws.</summary>
	public void Log(string message, string? jsonContext = null) {
		try {
			lock (_gate) {
				Directory.CreateDirectory(_folder);
				RollIfNeeded();

				string redacted = string.IsNullOrWhiteSpace(jsonContext)
					? string.Empty
					: " " + SecretRedactor.Redact(jsonContext);
				string line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {message}{redacted}{Environment.NewLine}";
				File.AppendAllText(_currentFile!, line);

				Prune();
			}
		}
		catch (Exception) {
			// Diagnostics must never break the app.
		}
	}

	private void RollIfNeeded() {
		if (_currentFile is not null && File.Exists(_currentFile)) {
			long length = new FileInfo(_currentFile).Length;
			if (length < _maxBytesPerFile) {
				return;
			}
		}
		else if (_currentFile is not null) {
			return; // named but not yet created — reuse the name for the first write
		}

		string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
		_currentFile = Path.Combine(_folder, $"ring-{stamp}-{_rollCounter++.ToString(CultureInfo.InvariantCulture)}.log");
	}

	private void Prune() {
		try {
			FileInfo[] files = new DirectoryInfo(_folder)
				.GetFiles("ring-*.log")
				.OrderByDescending(f => f.LastWriteTimeUtc)
				.ThenByDescending(f => f.Name, StringComparer.Ordinal)
				.ToArray();

			for (int i = _maxFiles; i < files.Length; i++) {
				try { files[i].Delete(); }
				catch (IOException) { /* locked — skip */ }
				catch (UnauthorizedAccessException) { /* skip */ }
			}
		}
		catch (DirectoryNotFoundException) {
			// Nothing to prune.
		}
	}
}
