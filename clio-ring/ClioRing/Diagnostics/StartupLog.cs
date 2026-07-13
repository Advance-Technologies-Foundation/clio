using System;
using System.IO;

namespace ClioRing.Diagnostics;

/// <summary>
/// Minimal append-only startup diagnostic log written to
/// <c>%LOCALAPPDATA%\clio-ring\startup.log</c>. Records lifecycle milestones (startup, tray,
/// hotkey registration success/failure with Win32 code, shutdown reason) so a "nothing happened"
/// launch is diagnosable. Contains no secrets. All writes are best-effort and never throw.
/// </summary>
public static class StartupLog {
	private static readonly object Gate = new();
	private static string? _path;

	/// <summary>Resolved log file path.</summary>
	public static string Path {
		get {
			if (_path is not null) {
				return _path;
			}

			try {
				string dir = System.IO.Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					"clio-ring");
				Directory.CreateDirectory(dir);
				_path = System.IO.Path.Combine(dir, "startup.log");
			}
			catch (Exception) {
				_path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clio-ring-startup.log");
			}

			return _path;
		}
	}

	/// <summary>Appends a timestamped line. Best-effort; swallows all I/O errors.</summary>
	public static void Log(string message) {
		try {
			lock (Gate) {
				File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
			}
		}
		catch (Exception) {
			// Diagnostics must never break the app.
		}
	}
}
