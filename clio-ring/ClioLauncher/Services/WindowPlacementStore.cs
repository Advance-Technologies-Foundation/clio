using System;
using System.IO;
using System.Text.Json;
using ClioLauncher.Models;

namespace ClioLauncher.Services;

/// <summary>Loads/persists/clears the window placement (separate file from env state to avoid write races).</summary>
public interface IWindowPlacementStore {
	/// <summary>Loads the saved placement, or null when none / on error.</summary>
	WindowPlacement? Load();

	/// <summary>Persists the placement (best-effort).</summary>
	void Save(WindowPlacement placement);

	/// <summary>Forgets any saved placement.</summary>
	void Clear();
}

/// <summary>
/// File-backed <see cref="IWindowPlacementStore"/> writing
/// <c>%LOCALAPPDATA%\clio-ring\window.json</c> via source-gen (AOT-safe). All I/O is best-effort.
/// </summary>
public sealed class WindowPlacementStore : IWindowPlacementStore {
	private readonly string _path;

	/// <summary>Creates the store using the default per-user path.</summary>
	public WindowPlacementStore() {
		string dir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"clio-ring");
		_path = Path.Combine(dir, "window.json");
	}

	/// <inheritdoc />
	public WindowPlacement? Load() {
		try {
			if (File.Exists(_path)) {
				string json = File.ReadAllText(_path);
				return JsonSerializer.Deserialize(json, LauncherJsonContext.Default.WindowPlacement);
			}
		}
		catch (Exception) {
			// ignore
		}

		return null;
	}

	/// <inheritdoc />
	public void Save(WindowPlacement placement) {
		try {
			string? dir = Path.GetDirectoryName(_path);
			if (!string.IsNullOrEmpty(dir)) {
				Directory.CreateDirectory(dir);
			}

			File.WriteAllText(_path, JsonSerializer.Serialize(placement, LauncherJsonContext.Default.WindowPlacement));
		}
		catch (Exception) {
			// best-effort
		}
	}

	/// <inheritdoc />
	public void Clear() {
		try {
			if (File.Exists(_path)) {
				File.Delete(_path);
			}
		}
		catch (Exception) {
			// best-effort
		}
	}
}

/// <summary>No-op <see cref="IWindowPlacementStore"/> for screenshots/tests (never touches disk).</summary>
public sealed class InMemoryWindowPlacementStore : IWindowPlacementStore {
	private WindowPlacement? _placement;

	/// <inheritdoc />
	public WindowPlacement? Load() => _placement;

	/// <inheritdoc />
	public void Save(WindowPlacement placement) => _placement = placement;

	/// <inheritdoc />
	public void Clear() => _placement = null;
}
