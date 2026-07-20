using System;
using System.IO;
using System.Threading;
using ClioRing.Diagnostics;

namespace ClioRing.Services;

/// <summary>Watches clio's environment settings and signals when the catalog must be re-read.</summary>
public interface IEnvironmentSettingsWatcher : IDisposable {
	/// <summary>Raised after a debounced settings-file change.</summary>
	event Action? Changed;

	/// <summary>Starts watching the default clio settings file, or an explicit test path.</summary>
	void Start(string? path = null);

	/// <summary>Stops watching the settings file.</summary>
	void Stop();
}

/// <summary>Debounced <see cref="FileSystemWatcher"/> implementation for clio appsettings.json.</summary>
public sealed class EnvironmentSettingsWatcher : IEnvironmentSettingsWatcher {
	private const int DebounceMs = 400;
	private readonly object _gate = new();
	private FileSystemWatcher? _watcher;
	private Timer? _debounce;
	private bool _disposed;

	/// <inheritdoc />
	public event Action? Changed;

	/// <summary>Gets the per-user clio settings path.</summary>
	public static string DefaultPath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"creatio", "clio", "appsettings.json");

	/// <inheritdoc />
	public void Start(string? path = null) {
		Stop();
		string settingsPath = path ?? DefaultPath;
		string? directory = Path.GetDirectoryName(settingsPath);
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
			StartupLog.Log($"environment watcher skipped; directory missing: {directory}");
			return;
		}

		_debounce = new Timer(_ => Changed?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);
		_watcher = new FileSystemWatcher(directory, Path.GetFileName(settingsPath)) {
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
			IncludeSubdirectories = false
		};
		_watcher.Changed += OnChanged;
		_watcher.Created += OnChanged;
		_watcher.Renamed += OnRenamed;
		_watcher.EnableRaisingEvents = true;
		StartupLog.Log($"environment watcher started on {settingsPath}");
	}

	private void OnChanged(object sender, FileSystemEventArgs args) => Schedule();
	private void OnRenamed(object sender, RenamedEventArgs args) => Schedule();

	private void Schedule() {
		lock (_gate) {
			_debounce?.Change(DebounceMs, Timeout.Infinite);
		}
	}

	/// <inheritdoc />
	public void Stop() {
		lock (_gate) {
			if (_watcher is not null) {
				_watcher.EnableRaisingEvents = false;
				_watcher.Changed -= OnChanged;
				_watcher.Created -= OnChanged;
				_watcher.Renamed -= OnRenamed;
				_watcher.Dispose();
				_watcher = null;
			}
			_debounce?.Dispose();
			_debounce = null;
		}
	}

	/// <inheritdoc />
	public void Dispose() {
		if (_disposed) {
			return;
		}
		_disposed = true;
		Stop();
	}
}

/// <summary>No-op environment watcher for design-time and tests.</summary>
public sealed class NullEnvironmentSettingsWatcher : IEnvironmentSettingsWatcher {
	/// <inheritdoc />
	public event Action? Changed { add { } remove { } }
	/// <inheritdoc />
	public void Start(string? path = null) { }
	/// <inheritdoc />
	public void Stop() { }
	/// <inheritdoc />
	public void Dispose() { }
}
