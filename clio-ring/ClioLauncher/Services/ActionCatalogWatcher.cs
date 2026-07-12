using System;
using System.IO;
using System.Threading;
using ClioLauncher.Diagnostics;
using ClioLauncher.Models;

namespace ClioLauncher.Services;

/// <summary>
/// Watches the actions.json file and raises a debounced reload when it changes on disk, so edits
/// apply live (no restart). Reload + re-validation happen on a background thread; the consumer
/// marshals UI updates. A valid reload raises <see cref="Reloaded"/>; a malformed one raises
/// <see cref="ReloadFailed"/> (the consumer keeps the last-good catalog).
/// </summary>
public interface IActionCatalogWatcher : IDisposable {
	/// <summary>Raised (off the UI thread) with a freshly loaded, valid catalog.</summary>
	event Action<ActionCatalog>? Reloaded;

	/// <summary>Raised (off the UI thread) with a validation/parse error message.</summary>
	event Action<string>? ReloadFailed;

	/// <summary>Begins watching the given actions.json path (default = beside the executable).</summary>
	void Start(string? path = null);

	/// <summary>Stops watching and releases the OS watcher.</summary>
	void Stop();
}

/// <summary>Default <see cref="IActionCatalogWatcher"/> backed by <see cref="FileSystemWatcher"/>.</summary>
public sealed class ActionCatalogWatcher : IActionCatalogWatcher {
	private const int DebounceMs = 400;

	private readonly IActionCatalogLoader _loader;
	private readonly object _gate = new();

	private FileSystemWatcher? _watcher;
	private Timer? _debounce;
	private string _path = string.Empty;
	private bool _disposed;

	/// <summary>Creates the watcher.</summary>
	public ActionCatalogWatcher(IActionCatalogLoader loader) {
		_loader = loader;
	}

	/// <inheritdoc />
	public event Action<ActionCatalog>? Reloaded;

	/// <inheritdoc />
	public event Action<string>? ReloadFailed;

	/// <inheritdoc />
	public void Start(string? path = null) {
		try {
			_path = path ?? Path.Combine(AppContext.BaseDirectory, ActionCatalogLoader.DefaultFileName);
			string? dir = Path.GetDirectoryName(_path);
			string file = Path.GetFileName(_path);
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) {
				return;
			}

			_debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);

			// Watch the whole file name across write/create/rename so editor "write-temp-then-rename"
			// saves are caught (VS Code, etc.). Debounce coalesces the burst of events.
			_watcher = new FileSystemWatcher(dir, file) {
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
				IncludeSubdirectories = false
			};
			_watcher.Changed += OnChanged;
			_watcher.Created += OnChanged;
			_watcher.Renamed += OnRenamed;
			_watcher.EnableRaisingEvents = true;
			StartupLog.Log($"actions.json watcher started on {_path}");
		}
		catch (Exception ex) {
			StartupLog.Log($"actions.json watcher failed to start: {ex.Message}");
		}
	}

	private void OnChanged(object sender, FileSystemEventArgs e) => Schedule();

	private void OnRenamed(object sender, RenamedEventArgs e) => Schedule();

	private void Schedule() {
		lock (_gate) {
			_debounce?.Change(DebounceMs, Timeout.Infinite);
		}
	}

	private void Reload() {
		// File may be momentarily absent mid-rename; skip quietly and wait for the next event.
		if (!File.Exists(_path)) {
			StartupLog.Log("actions.json reload skipped (file momentarily absent)");
			return;
		}

		try {
			ActionCatalog catalog = _loader.Load(_path);
			StartupLog.Log($"actions.json reloaded ({catalog.Actions.Count} actions)");
			Reloaded?.Invoke(catalog);
		}
		catch (ActionCatalogException ex) {
			StartupLog.Log($"actions.json reload rejected (kept last-good): {ex.Message}");
			ReloadFailed?.Invoke(ex.Message);
		}
		catch (Exception ex) {
			StartupLog.Log($"actions.json reload error (kept last-good): {ex.Message}");
			ReloadFailed?.Invoke(ex.Message);
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

/// <summary>No-op watcher for design-time / screenshots / tests.</summary>
public sealed class NullActionCatalogWatcher : IActionCatalogWatcher {
	/// <inheritdoc />
	public event Action<ActionCatalog>? Reloaded { add { } remove { } }

	/// <inheritdoc />
	public event Action<string>? ReloadFailed { add { } remove { } }

	/// <inheritdoc />
	public void Start(string? path = null) { }

	/// <inheritdoc />
	public void Stop() { }

	/// <inheritdoc />
	public void Dispose() { }
}
