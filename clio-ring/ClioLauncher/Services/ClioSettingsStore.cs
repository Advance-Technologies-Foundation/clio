using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClioLauncher.Services;

/// <summary>
/// Default <see cref="IClioSettingsStore"/> backed by <c>app-settings.json</c> beside the executable.
/// Reads/writes through <see cref="JsonNode"/> (no reflection-based serialization) so it stays AOT-safe
/// and preserves any settings the strongly-typed model does not know about. A read-modify-write updates
/// only the <c>DevClioPath</c> field; every other key is left byte-for-byte in place.
/// </summary>
public sealed class ClioSettingsStore : IClioSettingsStore {
	private const string DevClioPathKey = "DevClioPath";

	private readonly string _settingsPath;

	/// <summary>Creates a store over the default <c>app-settings.json</c> beside the executable.</summary>
	public ClioSettingsStore() : this(AppSettingsReader.SettingsPath) { }

	/// <summary>Creates a store over an explicit settings file (test seam).</summary>
	/// <param name="settingsPath">Absolute path to the <c>app-settings.json</c> file to read/write.</param>
	public ClioSettingsStore(string settingsPath) {
		_settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
	}

	/// <inheritdoc />
	public string SettingsPath => _settingsPath;

	/// <inheritdoc />
	public string? ReadDevClioPath() {
		JsonObject? root = TryReadRoot();
		string? value = root?[DevClioPathKey]?.GetValue<string>();
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	/// <inheritdoc />
	public void SaveDevClioPath(string? path) {
		JsonObject root = TryReadRoot() ?? new JsonObject();
		if (string.IsNullOrWhiteSpace(path)) {
			root.Remove(DevClioPathKey);
		}
		else {
			root[DevClioPathKey] = path.Trim();
		}

		string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(_settingsPath, json);
	}

	private JsonObject? TryReadRoot() {
		try {
			if (!File.Exists(_settingsPath)) {
				return null;
			}
			return JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject;
		}
		catch (Exception) {
			// A missing/unreadable/invalid file behaves like "no override"; a subsequent save rewrites it.
			return null;
		}
	}
}
