using System;
using System.IO;
using System.Text.Json;
using ClioRing.Models;

namespace ClioRing.Services;

/// <summary>Non-sensitive persisted state used to throttle checks and deduplicate desktop notices.</summary>
public sealed record ClioUpdateState(DateTimeOffset LastCheckedUtc, string InstalledVersion,
	string AvailableVersion, string? NotifiedVersion);

/// <summary>Persists bounded clio update-check and notification state.</summary>
public interface IClioUpdateStateStore {
	/// <summary>Reads the last valid state, or null when absent or malformed.</summary>
	ClioUpdateState? Read();

	/// <summary>Atomically saves a successful update-check snapshot.</summary>
	void Write(ClioUpdateState state);

	/// <summary>Marks a version notified and returns true only for its first notification.</summary>
	bool TryMarkNotified(string availableVersion);

}

/// <summary>Atomic JSON implementation stored under the current user's local application data.</summary>
public sealed class ClioUpdateStateStore : IClioUpdateStateStore {
	private readonly string _path;
	private readonly object _sync = new();

	/// <summary>Creates the production store in the per-user ClioRing data folder.</summary>
	public ClioUpdateStateStore() : this(Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Creatio", "clio-ring", "clio-update-state.json")) { }

	/// <summary>Creates a store at an explicit path for isolated validation.</summary>
	public ClioUpdateStateStore(string path) {
		_path = Path.GetFullPath(path);
	}

	/// <inheritdoc />
	public ClioUpdateState? Read() {
		lock (_sync) {
			return ReadLocked();
		}
	}

	/// <inheritdoc />
	public void Write(ClioUpdateState state) {
		ArgumentNullException.ThrowIfNull(state);
		lock (_sync) {
			try { WriteLocked(state); }
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
		}
	}

	/// <inheritdoc />
	public bool TryMarkNotified(string availableVersion) {
		if (!ClioToolVersion.IsStable(availableVersion)) {
			return false;
		}
		lock (_sync) {
			ClioUpdateState? state = ReadLocked();
			if (state is null || string.Equals(state.NotifiedVersion, availableVersion,
				StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			try {
				WriteLocked(state with { NotifiedVersion = availableVersion });
				return true;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
				return false;
			}
		}
	}

	private ClioUpdateState? ReadLocked() {
		try {
			if (!File.Exists(_path)) { return null; }
			string json = File.ReadAllText(_path);
			ClioUpdateState? state = JsonSerializer.Deserialize(json, RingJsonContext.Default.ClioUpdateState);
			return state is not null
				&& ClioToolVersion.IsStable(state.InstalledVersion)
				&& ClioToolVersion.IsStable(state.AvailableVersion)
				? state
				: null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
			or JsonException) {
			return null;
		}
	}

	private void WriteLocked(ClioUpdateState state) {
		string? directory = Path.GetDirectoryName(_path);
		if (directory is null) { return; }
		Directory.CreateDirectory(directory);
		string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
		try {
			File.WriteAllText(temporaryPath,
				JsonSerializer.Serialize(state, RingJsonContext.Default.ClioUpdateState));
			File.Move(temporaryPath, _path, overwrite: true);
		}
		finally {
			try { File.Delete(temporaryPath); }
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
		}
	}
}
