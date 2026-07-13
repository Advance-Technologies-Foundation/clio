using System;
using System.IO;
using System.Text.Json;
using ClioRing.Models;

namespace ClioRing.Services;

/// <summary>
/// File-backed <see cref="IEnvStateStore"/> writing <c>%LOCALAPPDATA%\clio-ring\state.json</c> via
/// the source-generated <see cref="RingJsonContext"/> (AOT-safe). All I/O is best-effort.
/// </summary>
public sealed class EnvStateStore : IEnvStateStore {
	private readonly string _path;

	/// <summary>Creates the store using the default per-user state path.</summary>
	public EnvStateStore() {
		string dir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"clio-ring");
		_path = Path.Combine(dir, "state.json");
	}

	/// <inheritdoc />
	public EnvState Load() {
		try {
			if (File.Exists(_path)) {
				string json = File.ReadAllText(_path);
				return JsonSerializer.Deserialize(json, RingJsonContext.Default.EnvState) ?? new EnvState();
			}
		}
		catch (Exception) {
			// Corrupt/unreadable state must not break launch.
		}

		return new EnvState();
	}

	/// <inheritdoc />
	public void Save(EnvState state) {
		try {
			string? dir = Path.GetDirectoryName(_path);
			if (!string.IsNullOrEmpty(dir)) {
				Directory.CreateDirectory(dir);
			}

			string json = JsonSerializer.Serialize(state, RingJsonContext.Default.EnvState);
			File.WriteAllText(_path, json);
		}
		catch (Exception) {
			// Persistence is best-effort.
		}
	}
}

/// <summary>In-memory <see cref="IEnvStateStore"/> for design-time / screenshots / tests.</summary>
public sealed class InMemoryEnvStateStore : IEnvStateStore {
	private EnvState _state;

	/// <summary>Creates the store with an optional seed state.</summary>
	public InMemoryEnvStateStore(EnvState? seed = null) {
		_state = seed ?? new EnvState();
	}

	/// <inheritdoc />
	public EnvState Load() => _state;

	/// <inheritdoc />
	public void Save(EnvState state) => _state = state;
}
